# Validation — Unity Package Manager SDK 2.0.0

The package contains canonical wallet runtime
`unity-webgl-package-sha256-cicBnjQf1cPzH5Sz3Gt7wX8seHXG9Cj1VTf6eRIAFlQ`, from
runtime revision `226bd9136b268cd1c7005efdb3bccf23be50e607`. All nine runtime files
are unchanged from the package used by NFTURBO build `20260915-6f70eb687b39`.
Version 2.0.0 describes the public UPM distribution and setup tools; the canonical
runtime manifest retains backend release version `0.2.5`.

## Checked for this distribution

- The root package resolves from its public Git revision in Unity 6000.3.15f1
  using Unity Package Manager. It compiles without Reown or separately installed
  wallet dependencies. The imported sample scene retains its document, panel,
  theme and camera references, with no game ID or email client ID prefilled.
- The setup menu creates a scene with a UI document, panel, camera and wallet
  demo. Repeating setup does not create a second demo. The `.jslib` plugin is
  enabled for WebGL and disabled for Editor.
- A small actual WebGL build succeeded with zero errors. Its five browser files
  match the canonical hashes, and no vendor copies were written into the
  project's Assets/StreamingAssets folder. The full NFTURBO game was not rebuilt.
- A headless browser loaded the demo and all three executable wallet modules
  from the built StreamingAssets with HTTP 200. The fullscreen template entered
  document-root fullscreen; Pera's in-game QR and cancellation were checked.
- Five focused checks cover package/dependency layout, the full canonical
  runtime hashes, Unity metadata/plugin settings, removal of the old active
  runtime, documentation links and sample/template presence.

The same unchanged core previously completed NFTURBO's desktop, ASTC and ETC2
builds and focused authentication/session/recovery-boundary tests. Its email
form opening/focus, close/retry and fullscreen behavior were checked in that
integration. These earlier checks do not prove every game or device works.

## Still requires attended player testing

Actual email/OTP and Pera wallet approval, C# acknowledgment after a real login,
returning login/recovery and stable identity across two actual game origins
remain player acceptance checks. The packaging test did not enter an email or
create a wallet. Opening a provider form is not proof of a completed login.

No live transaction was signed or submitted for this release. Shop delivery,
ASA opt-ins, Garage groups, Loadout ownership and Marketplace operations need
their own game-specific acceptance under existing backend policy. The sample
makes no economic requests. Preserve ambiguous-operation recovery and never
retry a submission automatically.

Physical mobile devices, provider popups and the complete verification/recovery
screens still need checks. The package supports Unity WebGL only; native exports
and unrelated email-provider configurations have not been implemented here.

## Recheck locally

Use **Blockmaker → Verify Installed Package** in Unity to check the nine runtime
hashes. Build with the included template and inspect its StreamingAssets output.
For contributors, `node --test 'Tools~/package.test.mjs'` runs the lightweight
packaging checks; Node.js is not required to install or use the SDK in Unity.
Installation and file verification do not activate a provider, qualify a game or
prove completed wallet/economic flows.
