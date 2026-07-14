#!/usr/bin/env node
// build-magic-vendor.mjs — builds TemplateData/bm-magic-vendor.js, the vendored
// Magic email-wallet bundle the WebGL bridge prefers over its jsDelivr fallback
// (window.BmMagicVendor in Plugins/WebGL/BlockmakerWalletBridge.jslib).
//
// Same pattern as the existing bm-wc-vendor.js (@walletconnect/sign-client +
// qrcode) and bm-pera-vendor.js (@perawallet/connect + algosdk) bundles: a
// self-contained minified IIFE, no runtime network imports, loaded via a
// <script> tag in the hosting page BEFORE the Unity loader.
//
// Usage:  node Tools~/build-magic-vendor.mjs [outfile]
//   Default outfile: ./bm-magic-vendor.js (cwd). Copy the result into the
//   game's WebGL template TemplateData/ and any hosting wrapper, and add
//   <script src="TemplateData/bm-magic-vendor.js"></script> next to the other
//   bm-*-vendor.js tags.
//
// Pinned versions MUST match the jsDelivr fallback URLs in the jslib:
//   magic-sdk@33.7.1, @magic-ext/algorand@26.2.0

import { execSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, copyFileSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const MAGIC_SDK = 'magic-sdk@33.7.1';
const MAGIC_ALGO = '@magic-ext/algorand@26.2.0';
const ESBUILD = 'esbuild@0.24.2';

const outfile = resolve(process.argv[2] || 'bm-magic-vendor.js');
const work = mkdtempSync(join(tmpdir(), 'bm-magic-vendor-'));

console.log(`[build-magic-vendor] work dir: ${work}`);
execSync(`npm init -y >/dev/null && npm install --no-audit --no-fund ${MAGIC_SDK} ${MAGIC_ALGO} ${ESBUILD}`, {
  cwd: work, stdio: 'inherit', shell: '/bin/bash',
});

writeFileSync(join(work, 'entry.js'), `
import { Magic } from 'magic-sdk';
import { AlgorandExtension } from '@magic-ext/algorand';
window.BmMagicVendor = { Magic: Magic, AlgorandExtension: AlgorandExtension };
`);

execSync(
  `npx esbuild entry.js --bundle --minify --format=iife --platform=browser ` +
  `--target=es2019 --define:process.env.NODE_ENV='"production"' ` +
  `--legal-comments=eof --outfile=out.js`,
  { cwd: work, stdio: 'inherit', shell: '/bin/bash' }
);

execSync('node --check out.js', { cwd: work, stdio: 'inherit' });
copyFileSync(join(work, 'out.js'), outfile);
console.log(`[build-magic-vendor] wrote ${outfile} (${statSync(outfile).size} bytes)`);
