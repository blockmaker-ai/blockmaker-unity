# Validation — Unity Package Manager SDK 2.0.1

Canonical runtime: `unity-webgl-package-sha256-ABEU4Xw6yrr_x5QxD0RBoncEqP2IStnSSnbbfQhI-5k`.
Source revision: `4e56ec769561f54b561cea28a181995883e3983d`.
The UPM distribution is version 2.0.1; the canonical manifest retains backend
release version 0.2.5. The complete nine-file runtime is included unchanged.

## Checks

- Thirty-four existing canonical package/manifest checks passed.
- Nineteen Unity UI checks passed: email-first choices, Pera logo, Back,
  cancellation once, Pera-only configuration and duplicate-open protection.
- The dialogs were rendered at desktop, tablet, phone landscape and phone
  portrait sizes. Cancel and account switching remain outside scrolling content.
- The package distribution checks verify runtime hashes, Unity GUIDs, plugin
  platforms, dependencies, installer, sample, template and documentation links.

- A minimal Unity 6000.3.15f1 WebGL consumer built with zero errors and warnings.
- The actual demo loaded all three wallet modules with HTTP 200. At a desktop
  size and an 844×390 phone viewport with a 1688×780 render target, its controls
  stayed readable. Pera QR, Cancel, reopening and Use another account worked;
  document-root fullscreen remained active throughout those wallet actions.

These checks cover the shared component and demo. They do not establish a
completed wallet login or transaction in a consuming game.

## Player checks still needed

The NFTURBO owner reported successful email login on 16 September 2026 using the
previous presentation. Automated checks did not enter an email, OTP or recovery
material and did not sign or submit a live transaction.

Returning login/recovery, identity across actual game origins, physical mobile
browsers and each game's economic operations require attended checks. The demo
reads profiles and makes no economic requests. Native exports are unsupported.

## Recheck locally

Use **Blockmaker → Verify Installed Package** for the nine runtime hashes.
Build using the included WebGL template. For contributors, run
`node --test 'Tools~/package.test.mjs'`; Node.js is not needed to install or use
the SDK through Unity Package Manager.
