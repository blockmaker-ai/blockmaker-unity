import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtemp, mkdir, writeFile, readFile, rm, cp, symlink, readdir } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { install, verifyDistribution } from './install.mjs'
import { walletPackageIdentity } from './wallet-package-contract.mjs'

const source = fileURLToPath(new URL('../', import.meta.url))
const lockPath = 'Assets/Blockmaker/blockmaker-unity-webgl-package.lock.json'
const facade = 'Assets/Blockmaker/Runtime/BlockmakerWalletPackageWebGL.cs'
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'blockmaker-public-sdk-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const project = join(root, 'game')
  await mkdir(join(project, 'ProjectSettings'), { recursive: true })
  await mkdir(join(project, 'Assets'))
  await writeFile(join(project, 'ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 6000.3.15f1\n')
  return { root, project, gameId: 'sample_game_one' }
}

test('the complete runtime has its canonical identity and hashes', async () => {
  const { manifest, files } = await verifyDistribution()
  assert.equal(manifest.packageId, 'unity-webgl-package-sha256-cicBnjQf1cPzH5Sz3Gt7wX8seHXG9Cj1VTf6eRIAFlQ')
  assert.equal(files.length, 9)
})

test('preview is read-only; complete install is idempotent and preserves GUIDs', async t => {
  const options = await fixture(t)
  const meta = join(options.project, facade + '.meta')
  await mkdir(join(options.project, 'Assets/Blockmaker/Runtime'), { recursive: true })
  await writeFile(meta, 'fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n')
  const before = await readFile(meta)
  assert.equal((await install(options)).writesPerformed, false)
  await assert.rejects(readFile(join(options.project, lockPath)), { code: 'ENOENT' })
  assert.equal((await install({ ...options, write: true })).writesPerformed, true)
  assert.equal((await install({ ...options, check: true })).changed, false)
  assert.equal((await install({ ...options, write: true })).writesPerformed, false)
  assert.deepEqual(await readFile(meta), before)
  assert.ok(!(await readdir(options.project)).some(name => name.startsWith('.blockmaker-webgl-stage-')))
})

test('per-game receipts remain distinct and cannot silently change games', async t => {
  const one = await fixture(t), two = await fixture(t)
  two.gameId = 'sample_game_two'
  await install({ ...one, write: true }); await install({ ...two, write: true })
  const first = JSON.parse(await readFile(join(one.project, lockPath)))
  const second = JSON.parse(await readFile(join(two.project, lockPath)))
  assert.equal(first.package.packageId, second.package.packageId)
  assert.notEqual(first.gameId, second.gameId)
  await assert.rejects(install({ ...one, gameId: two.gameId, write: true }), /another game/)
  assert.deepEqual(JSON.parse(await readFile(join(one.project, lockPath))), first)
})

test('corrupt distribution is rejected before writing', async t => {
  const options = await fixture(t)
  const broken = join(options.root, 'broken')
  await cp(source, broken, { recursive: true })
  await writeFile(join(broken, facade), 'corrupt')
  await assert.rejects(install({ ...options, source: broken, write: true }), /checksum mismatch/)
  assert.deepEqual(await readdir(join(options.project, 'Assets')), [])
})

test('authored changes are preserved, including untracked collisions', async t => {
  const options = await fixture(t)
  await mkdir(join(options.project, 'Assets/Blockmaker/Runtime'), { recursive: true })
  await writeFile(join(options.project, facade), 'authored work')
  await assert.rejects(install({ ...options, write: true }), /Untracked package file/)
  assert.equal(await readFile(join(options.project, facade), 'utf8'), 'authored work')
  await rm(join(options.project, facade))
  await install({ ...options, write: true })
  await writeFile(join(options.project, facade), 'new authored work')
  await assert.rejects(install({ ...options, write: true }), /missing or modified/)
  assert.equal(await readFile(join(options.project, facade), 'utf8'), 'new authored work')
})

test('a different package requires its exact reviewed replacement ID', async t => {
  const options = await fixture(t)
  await install({ ...options, write: true })
  const lock = JSON.parse(await readFile(join(options.project, lockPath)))
  lock.package.releaseVersion = '0.2.6'
  lock.package.packageId = walletPackageIdentity(lock.package)
  await writeFile(join(options.project, lockPath), JSON.stringify(lock))
  await assert.rejects(install({ ...options, write: true }), /Another package is installed/)
  await assert.rejects(install({ ...options, write: true, replacePackage: 'wrong-id' }), /Another package is installed/)
  await install({ ...options, write: true, replacePackage: lock.package.packageId })
  assert.equal((await install({ ...options, check: true })).changed, false)
})

test('legacy UPM and symlink destinations are rejected without writes', async t => {
  const options = await fixture(t)
  await mkdir(join(options.project, 'Packages'))
  const upm = join(options.project, 'Packages/manifest.json')
  await writeFile(upm, JSON.stringify({ dependencies: { 'com.blockmaker.sdk': 'legacy' } }))
  await assert.rejects(install({ ...options, write: true }), /Legacy com.blockmaker.sdk/)
  assert.deepEqual(await readdir(join(options.project, 'Assets')), [])
  await rm(upm)
  const outside = join(options.root, 'outside')
  await mkdir(outside)
  await symlink(outside, join(options.project, 'Assets/Blockmaker'), 'dir')
  await assert.rejects(install({ ...options, write: true }), /symbolic link/)
  assert.deepEqual(await readdir(outside), [])
})
