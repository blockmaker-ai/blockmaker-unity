# Release validation — 16 September 2026

Public distribution: `webgl-preview-20260916`.
Canonical package:
`unity-webgl-package-sha256-cicBnjQf1cPzH5Sz3Gt7wX8seHXG9Cj1VTf6eRIAFlQ`.
Runtime source revision: `226bd9136b268cd1c7005efdb3bccf23be50e607`.
The package manifest retains the canonical backend release version `0.2.5`;
the public preview tag identifies this distribution separately.

## Passed

- All nine runtime members match the canonical source and the package used in
  NFTURBO build `20260915-6f70eb687b39`, byte for byte. The canonical package
  identity and all member hashes verify. The canonical contract module and
  sample were copied unchanged from the same source revision.
- All three C# runtime files plus the sample compile against Unity 6000.3.15f1
  assemblies for the Editor and WebGL branches. Zero errors; 29/26 warnings
  respectively, principally existing JSON-populated fields and unused fields.
  This is a compiler check, not a new Unity sample build or gameplay test.
- Seven focused installer checks: complete hash verification; preview/no-write,
  idempotent installation and GUID preservation; separate game receipts;
  rejection of corrupt distributions, modified files, unreviewed package
  replacement, legacy UPM and symbolic-link destinations.
- The unchanged runtime previously passed focused authentication/session,
  cancellation, transaction-boundary and shared-profile checks during the game
  integration. Desktop, ASTC and ETC2 WebGL game builds completed.
- A short headless browser check of that game build loaded the desktop and
  mobile ASTC game. Desktop Pera QR and cancellation, opening/focusing/closing
  the provider's email form, retry, and bottom notices worked. The document root
  stayed fullscreen through those tested actions, with no requested exits.
  The phone check was browser emulation, not a physical-device qualification.

## Still requires an attended player check

No real email/OTP was submitted in that final browser pass. Actual provider
sign-in followed by C# session acknowledgment, returning login/recovery, and
wallet/profile continuity across two actual game origins remain owner acceptance
checks. Pera wallet approval also remains an attended check. Opening an email
form is not proof of a completed login.

No live transaction was signed or submitted during the final release checks.
Shop purchase/delivery, ASA opt-in, Garage construction, Loadout ownership and
Marketplace LIST/CANCEL/BUY must be checked individually under each game's
existing policy. Preserve ambiguous-operation recovery; do not auto-resubmit.
The sample makes no economic requests and does not claim these operations pass.

Physical mobile input, provider popup behavior and the full verification/recovery
screens still need device testing. A visible email field alone does not prove
that keyboard input and the entire flow work on every browser.

## Recheck the public distribution

From the extracted package, with Node 22.13+:

```sh
node --test Tools/install.test.mjs
node Tools/install.mjs --project /path/to/UnityGame --game YOUR_GAME_ID --check
```

The tests use the operating system's temporary directory; set `TMPDIR` to your
preferred scratch volume first. They never contact a backend, send email or
create wallets. Installation records prove local package contents, not provider
activation or release qualification. A deployed backend manifest may lag this
preview; do not overwrite a reviewed package with older manifest members.
