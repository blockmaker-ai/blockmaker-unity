#!/usr/bin/env node
// Offline distribution installer. Runtime validation and receipt formats come
// from the canonical Blockmaker contract; no provider or backend is contacted.
import { lstat, readFile, mkdir, mkdtemp, writeFile, rename, rm, access } from 'node:fs/promises'
import { resolve, dirname, join, relative } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import {
  parseWalletPackageManifest, parseWalletPackageLock, createWalletPackageLock,
  createWalletPackageInstallationEvidence, canonicalJsonBytes,
  integrityForWalletPackageBytes,
} from './wallet-package-contract.mjs'

const distributionRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const lockPath = 'Assets/Blockmaker/blockmaker-unity-webgl-package.lock.json'
const evidencePath = 'Assets/Blockmaker/blockmaker-unity-webgl-package.installation.json'

async function optionalBytes(path) {
  try { return await readFile(path) } catch (error) { if (error.code === 'ENOENT') return null; throw error }
}

async function directPath(root, target) {
  const path = relative(root, target)
  if (path.startsWith('..') || resolve(root, path) !== target) throw new Error('Package path escaped the project.')
  let cursor = root
  for (const part of path.split(/[\\/]/)) {
    cursor = join(cursor, part)
    try {
      const entry = await lstat(cursor)
      if (entry.isSymbolicLink()) throw new Error(`Refusing symbolic link: ${relative(root, cursor)}`)
      if (cursor !== target && !entry.isDirectory()) throw new Error(`Not a directory: ${relative(root, cursor)}`)
      if (cursor === target && !entry.isFile()) throw new Error(`Not a regular file: ${path}`)
    } catch (error) { if (error.code !== 'ENOENT') throw error }
  }
}

export async function verifyDistribution(root = distributionRoot) {
  const value = JSON.parse(await readFile(join(root, 'package-manifest.json'), 'utf8'))
  const apiOrigin = new URL(value.members[0].url).origin
  const manifest = parseWalletPackageManifest(value, { apiOrigin, target: 'unity_webgl' })
  if (manifest.members.length !== 9) throw new Error('This distribution requires the complete nine-member package.')
  const files = []
  for (const member of manifest.members) {
    const path = join(root, member.installPath)
    await directPath(root, path)
    const bytes = await readFile(path)
    if (bytes.length !== member.rawBytes || integrityForWalletPackageBytes(bytes) !== member.integrity)
      throw new Error(`Distribution checksum mismatch: ${member.file}`)
    files.push({ path: member.installPath, bytes })
  }
  return { manifest, files }
}

export async function install({ project, gameId, apiOrigin = 'https://blockmaker.polaris.city',
  write = false, check = false, replacePackage = '', source = distributionRoot } = {}) {
  if (!project || !gameId || (write && check)) throw new Error('Provide --project and --game; choose --write or --check, not both.')
  const root = resolve(project)
  const entry = await lstat(root)
  if (!entry.isDirectory() || entry.isSymbolicLink()) throw new Error('Choose an existing Unity project directory, not a symbolic link.')
  await access(join(root, 'ProjectSettings/ProjectVersion.txt'))
  const upmBytes = await optionalBytes(join(root, 'Packages/manifest.json'))
  if (upmBytes && JSON.parse(upmBytes).dependencies?.['com.blockmaker.sdk'])
    throw new Error('Legacy com.blockmaker.sdk is installed. Migrate its scene/scripts and remove that package before installing this WebGL runtime.')
  for (const legacy of ['Assets/Blockmaker/Core/BlockmakerAuth.cs', 'Assets/Blockmaker/BlockmakerAuth.cs'])
    if (await optionalBytes(join(root, legacy))) throw new Error('Legacy BlockmakerAuth is installed. Complete the migration before installing this WebGL runtime.')
  const verified = await verifyDistribution(source)
  // The release ships bytes locally. URLs describe the configured API's archive;
  // they are not fetched and do not claim that archive already serves this release.
  const value = structuredClone(verified.manifest)
  for (const member of value.members) member.url = apiOrigin + new URL(member.url).pathname
  const manifest = parseWalletPackageManifest(value, { apiOrigin, target: 'unity_webgl' })
  const lock = createWalletPackageLock({ gameId, apiOrigin, packageManifest: manifest })
  const lockBytes = Buffer.from(canonicalJsonBytes(lock))
  const evidence = createWalletPackageInstallationEvidence({ gameId, apiOrigin: lock.apiOrigin,
    packageManifest: manifest, packageLockIntegrity: integrityForWalletPackageBytes(lockBytes) })
  const files = [...verified.files,
    { path: evidencePath, bytes: Buffer.from(canonicalJsonBytes(evidence)) },
    { path: lockPath, bytes: lockBytes }, // complete package marker written last
  ]
  for (const file of files) await directPath(root, join(root, file.path))
  const oldLockBytes = await optionalBytes(join(root, lockPath))
  const old = oldLockBytes ? parseWalletPackageLock(JSON.parse(oldLockBytes), {
    gameId, apiOrigin: lock.apiOrigin, target: 'unity_webgl',
  }) : null
  if (old && old.package.packageId !== manifest.packageId && replacePackage !== old.package.packageId)
    throw new Error(`Another package is installed: ${old.package.packageId}. Review it, then use --replace-package with that exact ID. No automatic upgrade or downgrade.`)
  if (replacePackage && (!old || replacePackage !== old.package.packageId))
    throw new Error('--replace-package does not match the installed package.')
  if (old) {
    for (const member of old.package.members) {
      const target = join(root, member.installPath)
      await directPath(root, target)
      const bytes = await optionalBytes(target)
      if (!bytes || bytes.length !== member.rawBytes || integrityForWalletPackageBytes(bytes) !== member.integrity)
        throw new Error(`Installed file is missing or modified: ${member.installPath}. Preserve/reconcile that work before installing.`)
    }
  } else {
    for (const file of files) {
      const bytes = await optionalBytes(join(root, file.path))
      if (bytes && !bytes.equals(file.bytes)) throw new Error(`Untracked package file already exists: ${file.path}. Preserve/reconcile it before installing.`)
    }
  }
  const before = await Promise.all(files.map(file => optionalBytes(join(root, file.path))))
  const changed = files.some((file, index) => !before[index]?.equals(file.bytes))
  if (check && changed) throw new Error('Installed package or game receipts do not match this distribution.')
  if (write && changed) {
    // Keep the staging folder on the project filesystem for atomic renames.
    // Unity should be closed so it cannot import an intermediate package.
    const stage = await mkdtemp(join(root, '.blockmaker-webgl-stage-'))
    const installed = []
    let keepStage = false
    try {
      for (let index = 0; index < files.length; index++) {
        await writeFile(join(stage, `new-${index}`), files[index].bytes, { flag: 'wx' })
        if (before[index]) await writeFile(join(stage, `old-${index}`), before[index], { flag: 'wx' })
      }
      for (let index = 0; index < files.length; index++) {
        const target = join(root, files[index].path)
        await directPath(root, target)
        const current = await optionalBytes(target)
        if (Boolean(current) !== Boolean(before[index]) || (current && !current.equals(before[index])))
          throw new Error('Package files changed during installation. Previous files will be restored.')
        await mkdir(dirname(target), { recursive: true })
        await rename(join(stage, `new-${index}`), target)
        installed.push(index)
      }
    } catch (error) {
      try {
        for (const index of installed.reverse()) {
          const target = join(root, files[index].path)
          if (before[index]) await rename(join(stage, `old-${index}`), target)
          else await rm(target)
        }
      } catch {
        keepStage = true
        throw new Error(`Installation rollback needs manual recovery. Keep Unity closed; preserved files are in ${stage}.`)
      }
      throw error
    } finally { if (!keepStage) await rm(stage, { recursive: true, force: true }) }
  }
  return { success: true, mode: check ? 'check' : write ? 'install' : 'preview', changed,
    writesPerformed: write && changed, packageId: manifest.packageId, gameId, apiOrigin: lock.apiOrigin,
    members: manifest.members.length, metaFiles: 'untouched', activation: manifest.activation }
}

async function main(argv) {
  if (argv.includes('--help')) {
    console.log('node Tools/install.mjs --project /path/to/UnityGame --game YOUR_GAME_ID [--api https://blockmaker.polaris.city] [--write | --check] [--replace-package EXACT_INSTALLED_ID]\nDefault previews changes. Close Unity before --write. Existing .meta files are never changed.')
    return
  }
  const options = {}
  const values = { '--project': 'project', '--game': 'gameId', '--api': 'apiOrigin', '--replace-package': 'replacePackage' }
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === '--write' || arg === '--check') options[arg.slice(2)] = true
    else if (Object.hasOwn(values, arg) && argv[i + 1] && !argv[i + 1].startsWith('--')) options[values[arg]] = argv[++i]
    else throw new Error(`Unknown or incomplete option: ${arg}`)
  }
  console.log(JSON.stringify(await install(options), null, 2))
}
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url)
  main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1 })
