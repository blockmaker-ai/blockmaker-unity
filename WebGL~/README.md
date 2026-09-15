# Unity WebGL wallet package

This is the complete nine-member canonical package used by the September 2026
Unity integration. It keeps one wallet manager and the current proof/rekey
verification pipeline. Email is optional. This distribution is a **preview**;
see [validation](VALIDATION.md) for what has and has not been tested.

## Install

Use Unity 6 (validated with 6000.3.15f1) and Node 22.13+.
Download the `Blockmaker-Unity-WebGL-webgl-preview-20260916.zip` release asset,
verify its SHA-256 against the release checksums, and extract it **outside** your
Unity project's Assets folder. From the extracted directory:

```sh
node Tools/install.mjs --project /path/to/UnityGame --game YOUR_GAME_ID
# Close Unity, review the preview, then install:
node Tools/install.mjs --project /path/to/UnityGame --game YOUR_GAME_ID --write
node Tools/install.mjs --project /path/to/UnityGame --game YOUR_GAME_ID --check
```

Use `--api https://your-blockmaker-api.example` for a compatible alternative API.
The installer needs no npm dependencies or network access. It validates every
member before writing, generates game-specific lock/installation records with
the canonical contract, and writes the lock last. Existing `.meta` files are
untouched; Unity creates metadata for new assets on import. Commit those files.
Close Unity during installation so it does not import an intermediate state.

The installer refuses modified files, a different game/API identity, the legacy
UPM SDK or a different installed package. After reviewing an intentional
upgrade, pass `--replace-package EXACT_INSTALLED_PACKAGE_ID`. This never guesses
whether a different package is newer. Keep your project under version control.
Do not patch or copy individual vendor members or run a live-manifest sync over
this package: an API's published archive can lag this preview. Installation is
not provider activation or a claim of game qualification.

If migrating v1, replace `BlockmakerAuth`/legacy profile/UI callers and remove
its scene objects/package first. Retain your game data and explicit recovery
records. Any legacy SDK installed elsewhere in Assets must also be removed
from compilation after its consumers are migrated. Do not retain two clients
with competing `BlockmakerClient` definitions or two wallet managers.

## Configure and show sign-in

Create one `BlockmakerClient` and one `BlockmakerWalletPackageWebGL`. Initialize
when the game loads with its own public game ID, API origin, app name, and either
an empty client ID (Pera/Lute) or a reviewed **public** email provider client ID.
There is no private/server key to place in Unity.

```csharp
var client = new BlockmakerClient(apiOrigin, gameId);
var wallet = gameObject.AddComponent<BlockmakerWalletPackageWebGL>();
wallet.Initialize(client, publicEmailClientIdOrEmpty, appName, result => {
    signInButton.SetEnabled(result.Success);
});

// On the player's Sign in click; keep the view alive until completion.
view = new BlockmakerUnityWalletView(document.rootVisualElement, wallet,
    new BlockmakerUnityWalletAppearance { AppName = appName, Font = gameFont });
view.OpenAccount(result => {
    bool ready = result.Success && wallet.HasAcknowledgedPlayerSession
        && wallet.CanSignTransactions;
    // Update the game UI from ready and result.Code; dispose the view when done.
});
```

The view exposes configurable labels/colours/font, in-game QR/progress/cancel,
and the optional “Continue with email” choice. Use `RuntimeSupported` to provide
a friendly Editor message. `PLAYER_CANCELLED` is informational. Never treat a
cached address or browser-only success as a verified C# player session.
Dispose the view at scene teardown; use the package's cancellation/logout paths.
A failed or uncertain logout may require a page reload before another attempt.

Copy `Samples~/EmailWallet/BlockmakerEmailSample.cs` into your game's Assets and
follow its [scene and fullscreen instructions](Samples~/EmailWallet/README.md).
Use your own licensed font. The sample intentionally reads profiles only and
sends no transactions.

## Fullscreen and hosting

From a user gesture, fullscreen `document.documentElement`, not the Unity canvas
or game-only wrapper. Do not call `unityInstance.SetFullscreen(1)` afterward.
This keeps provider forms mounted under `body` in the interactive fullscreen
subtree. The package handles the existing provider forms/popovers and releases
Unity keyboard capture during authentication. Do not reparent provider iframes.
A provider verification popup may still change fullscreen according to browser
rules. Pera's Unity QR path does not request a fullscreen exit.

Serve StreamingAssets unchanged, with `.mjs`/`.js` JavaScript MIME types. Allow
the API origin and provider connections in your host's CSP. In particular the
provider forms need their Web3Auth frames, scripts/connections and hCaptcha;
restricting these to `self` will prevent email login. Keep popup-compatible
`Cross-Origin-Opener-Policy: same-origin-allow-popups` for this non-threaded
WebGL integration. Do not add COEP isolation without separately validating the
provider's cross-origin frames. Check current provider hosting requirements.

## Shared profiles and game actions

`GetGameProfile` projects the canonical shared identity: registered username,
NFD/display-name choice, default avatar and owned NFT picture. Use the shared
profile update methods and preserve deliberate clears. Loading a profile or
showing a placeholder must never save a default selection. Identity is the
connected owner wallet, including rekeyed accounts, not the signing authority.
Separate game progress/loadouts from this shared appearance.

Use the existing provider-independent balance, inventory and prepared-operation
APIs. An embedded wallet must receive the same **explicit Unity transaction
review**: action, assets, total cost and fees, followed by the player's Sign
click. Then call `PrepareTransactionGroup` / `SignPreparedTransactionGroup` on
the exact intended group. Preserve signer indexes and any application operation
ID/recovery record. These signing methods do not submit. Use the appropriate
one-transmission submission method and recover uncertain outcomes by the
existing operation ID; never automatically rebroadcast an ambiguous operation.

Show the exact receiving address, actual ALGO/token balances and the game's
funding guidance. Email login provides no funds and enables no paused trading or
rewards. Store no Blockmaker sessions in PlayerPrefs: they remain in memory.
Keep email, OTP, private keys and recovery material inside the supported provider
adapter; do not pass them into C#, backend APIs or logs.

For optional email provisioning, returning accounts and cross-game wallet
continuity, read [EMAIL_SETUP.md](EMAIL_SETUP.md) before onboarding players.
