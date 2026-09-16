# Integrating Blockmaker Wallets

Install version 2 through Unity Package Manager as described in the [README](../README.md).
The package contains the complete current wallet runtime; no manually added CDN
script tags or separately copied vendor files are needed. Keep one wallet manager per page.

## Custom game UI

Create one `BlockmakerClient` and one `BlockmakerWalletPackageWebGL`, initialize
while loading, then open a presentation view on the player's Sign in click:

```csharp
using Blockmaker;

var client = new BlockmakerClient(apiOrigin, publicGameId);
var wallet = gameObject.AddComponent<BlockmakerWalletPackageWebGL>();
wallet.Initialize(client, publicEmailClientIdOrEmpty, appName, result => {
    signInButton.SetEnabled(result.Success);
});

// In the Sign in button callback. Keep this view alive until the result arrives.
view = new BlockmakerUnityWalletView(document.rootVisualElement, wallet,
    new BlockmakerUnityWalletAppearance { AppName = appName, Font = gameFont });
view.OpenAccount(result => {
    bool ready = result.Success && wallet.HasAcknowledgedPlayerSession
        && wallet.CanSignTransactions;
    // Update the game UI, then dispose the view.
});
```

These are integration fragments; the complete working component is
[`BlockmakerWalletDemo`](../Runtime/BlockmakerWalletDemo.cs). Only initialize once,
keep the receiving GameObject's name unique, and dispose the view at teardown.
`RuntimeSupported` lets the game show a friendly message in Editor/native builds.
A blank public email client ID selects `pera_lute`; a configured ID selects
`pera_lute_txnlab_web3auth`. The Unity presentation shows Pera and optional email;
`OpenAccount` is available for the broader account UI. The older
`web3auth_avm_email` authentication-only broker is separate and remains restricted.

Use `HasAcknowledgedPlayerSession` and `CanSignTransactions` for readiness.
An address hint or JavaScript-only success is insufficient. `PLAYER_CANCELLED`
is informational. Use the package cancellation/logout methods, honour a required
reload after uncertain cleanup, and never start a hidden recovery flow mid-signing.
Blockmaker session tokens stay in memory, including across refresh acknowledgment.

## Fullscreen and deployment

The included template is installed through **Blockmaker → Install Fullscreen Web
Template**. This creates a project-owned template; updates never overwrite a
locally changed template. With a custom host, fullscreen
`document.documentElement` from a user gesture. Do not subsequently call
`unityInstance.SetFullscreen(1)`, which fullscreens only the canvas and can make
provider controls outside that subtree unusable.

The package uses Unity's supported build callback to add its five browser files
under `StreamingAssets/Blockmaker`. Source assets stay in the package; no copies
are written into your Assets folder. The `.jslib` plugin is enabled for WebGL
only. Builds verify all nine canonical members first and reject conflicting old
StreamingAssets copies. Native builds are rejected because this integration is
WebGL-only. Build from an actual Unity project with this UPM package installed.

Serve `.js` and `.mjs` as JavaScript, and preserve Unity's content/encoding headers.
Allow the Blockmaker API, Pera connections and the provider's Web3Auth/hCaptcha
frames, scripts and connections in your hosting policy. This non-threaded wallet
integration uses popup-compatible `Cross-Origin-Opener-Policy:
same-origin-allow-popups`; do not impose cross-origin isolation without separately
validating provider frames. Keep provider iframes where the provider mounts them.
The presentation releases Unity's keyboard capture for email inputs. A provider
popup may still change fullscreen according to browser rules.

## Shared profiles and actions

`GetGameProfile` reads the canonical shared username, NFD/display-name choice,
default avatar and NFT picture. Use supported profile update methods, distinguish
NFD names from registered usernames, preserve deliberate clears and never save a
placeholder while loading. Identity follows the connected owner wallet,
including rekeyed accounts, rather than the signing authority. Progress, loadouts
and statistics remain game-specific. See [email setup](email-setup.md) for wallet
identity across games.

For economic operations, use the game's validated prepare → Unity review →
explicit Sign → exact one-time submission/recovery flow. Review the action,
assets, costs and fees before signing. `PrepareTransactionGroup` and
`SignPreparedTransactionGroup` preserve the exact prepared group; they do not
submit it. Preserve signer indexes, operation IDs and recovery records. Never
automatically re-sign/rebroadcast an ambiguous submission.

Show the exact receiving address, balances and funding guidance. Authentication
adds no ALGO or other assets. Keep credentials, OTPs, private keys and recovery
material inside the provider adapter, outside C#, PlayerPrefs, API bodies and logs.

## Package maintenance

`Packages/packages-lock.json` pins the UPM revision. `package-manifest.json` pins
the same nine canonical runtime members used by the earlier complete package;
its original `installPath` fields describe the Assets-based distribution, while
the UPM build hook maps those members to package paths. Their bytes, content ID,
network and provider policy are unchanged. UPM installation does not emit a
separate game-bound Assets installation receipt or activate provider policy.
The old `sync-unity-webgl-sdk` command targets the Assets distribution and must
not be run over a UPM installation. Any operator tooling that assumes that old
layout needs a UPM-aware check; the menu **Verify Installed Package** verifies
this package locally without claiming provider or game qualification.

Commit scene/template changes and Unity metadata. Use complete tagged releases,
not individual vendor-file patches. Required setup and attended acceptance remain
in [email setup](email-setup.md) and [validation](validation.md).
