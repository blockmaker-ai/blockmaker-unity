import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { resolve, dirname, join } from 'node:path'
import { parseWalletPackageManifest, integrityForWalletPackageBytes } from './wallet-package-contract.mjs'
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const text = path => readFile(join(root, path), 'utf8')
const json = async path => JSON.parse(await text(path))
const location = name => name.endsWith('.cs') ? 'Runtime/' + name : name.endsWith('.jslib') ? 'Plugins/WebGL/' + name : 'Browser~/' + name
async function allFiles(path = '') {
  const out = []
  for (const entry of await readdir(join(root, path), { withFileTypes: true })) {
    if (entry.name.startsWith('.')) continue
    const next = path ? path + '/' + entry.name : entry.name
    if (entry.isDirectory()) out.push(...await allFiles(next)); else out.push(next)
  }
  return out
}

test('UPM root is version 2 with only Unity module dependencies and current installer', async () => {
  const pkg = await json('package.json')
  assert.equal(pkg.name, 'com.blockmaker.sdk'); assert.equal(pkg.version, '2.0.0')
  assert.ok(Object.keys(pkg.dependencies).every(name => name.startsWith('com.unity.modules.')))
  const runtime = await json('Runtime/Blockmaker.asmdef')
  assert.equal(runtime.name, 'Blockmaker'); assert.deepEqual(runtime.references, [])
  const editor = await json('Editor/Blockmaker.Editor.asmdef')
  assert.deepEqual(editor.includePlatforms, ['Editor']); assert.ok(editor.references.includes('Blockmaker'))
  const installer = await text('Installer~/BlockmakerInstaller.cs')
  assert.ok(installer.includes('Client.Add(PackageUrl)'))
  assert.ok(installer.includes('https://github.com/blockmaker-ai/blockmaker-unity.git#v2.0.0'))
})

test('UPM carries the complete unmodified canonical runtime and matching build map', async () => {
  const value = await json('package-manifest.json')
  const manifest = parseWalletPackageManifest(value, { apiOrigin: new URL(value.members[0].url).origin, target: 'unity_webgl' })
  assert.equal(manifest.packageId, 'unity-webgl-package-sha256-cicBnjQf1cPzH5Sz3Gt7wX8seHXG9Cj1VTf6eRIAFlQ')
  assert.equal(manifest.members.length, 9)
  const hook = await text('Editor/BlockmakerWebGLBuild.cs')
  for (const member of manifest.members) {
    const path = location(member.file), bytes = await readFile(join(root, path))
    assert.equal(bytes.length, member.rawBytes); assert.equal(integrityForWalletPackageBytes(bytes), member.integrity)
    assert.ok(hook.includes('"' + path + '"'), path)
  }
  assert.equal((await readdir(join(root, 'Browser~'))).length, 5)
  assert.ok(hook.includes('AddAdditionalPathToStreamingAssets'))
})

test('all imported assets have unique metadata and the bridge is WebGL-only', async () => {
  const files = await allFiles(), guids = new Set()
  for (const path of files.filter(path => !path.split('/').some(part => part.endsWith('~')) && !path.endsWith('.meta'))) {
    const meta = await text(path + '.meta')
    const guid = meta.match(/^guid: ([a-f0-9]{32})$/m)?.[1]
    assert.ok(guid, path); assert.ok(!guids.has(guid), path); guids.add(guid)
  }
  const plugin = await text('Plugins/WebGL/BlockmakerWalletPackageWebGL.jslib.meta')
  assert.match(plugin, /WebGL: WebGL\n\s+second:\n\s+enabled: 1/)
  assert.match(plugin, /Editor: Editor\n\s+second:\n\s+enabled: 0/)
})

test('active runtime has one wallet facade and no obsolete implementation', async () => {
  const files = await allFiles()
  assert.ok(!files.some(path => /^(Core|Identity|UI|WebGL~)\//.test(path)))
  assert.ok(!files.some(path => /BlockmakerAuth\.cs|BlockmakerWalletBridge\.jslib|SecurePrefs\.cs/.test(path)))
  assert.equal(files.filter(path => path.endsWith('BlockmakerWalletPackageWebGL.cs')).length, 1)
})

test('documentation links, sample and fullscreen template resolve', async () => {
  for (const path of (await allFiles()).filter(path => path.endsWith('.md') && path !== 'CHANGELOG.md')) {
    for (const [, link] of (await text(path)).matchAll(/\]\(([^)]+)\)/g)) {
      if (/^https?:|^#/.test(link)) continue
      await readFile(resolve(root, dirname(path), link.split('#')[0]))
    }
  }
  const template = await text('Templates~/Blockmaker/index.html')
  assert.ok(template.includes('document.documentElement.requestFullscreen()'))
  assert.ok(!template.includes('SetFullscreen'))
  for (const sample of (await json('package.json')).samples) assert.ok((await readdir(join(root, sample.path))).length > 0)
})
