# Blockmaker Unity SDK

Add **Pera and optional passwordless email wallets** to Unity WebGL games on
Algorand MainNet. Email uses TxnLab with MetaMask Embedded Wallets. Players can
use an email wallet for the same supported game actions as a Pera wallet,
subject to balances, asset ownership and the game's rules.

**Version 2.0.1 is a local release candidate.** The published SDK is still 2.0.0.
This package supports **Unity WebGL**, including compatible desktop and mobile
browsers. It does not provide native Windows, macOS, Android or iOS wallet exports.

## Install through Unity Package Manager

Requires **Unity 6** with Web Build Support and **Git** installed.

For this local candidate, choose **Install package from disk** and select its
`package.json`. The `v2.0.1` Git tag and tarball below are release targets, not
published downloads. To install the current public release now, use
`https://github.com/blockmaker-ai/blockmaker-unity.git#v2.0.0`.

After version 2.0.1 is published:

1. In Unity, open **Window → Package Management → Package Manager**.
2. Choose **+ → Install package from Git URL** (called “Add package from Git URL”
   in some versions).
3. Paste this URL and select **Install**:

   ```text
   https://github.com/blockmaker-ai/blockmaker-unity.git#v2.0.1
   ```

Unity installs the SDK and its required Unity modules. No Node.js, separate
Reown installation or manual browser-file copying is needed. The package adds
its verified wallet browser files to WebGL builds automatically.

Prefer the previous drag-in method? [Download BlockmakerInstaller.cs](https://raw.githubusercontent.com/blockmaker-ai/blockmaker-unity/main/Installer~/BlockmakerInstaller.cs),
place it in `Assets/Editor`, and choose **Install**. It installs the same version
through Package Manager. You can delete that installer afterward.

The release will also provide a [`.tgz` download](https://github.com/blockmaker-ai/blockmaker-unity/releases/tag/v2.0.1)
for Package Manager's **Install package from tarball** option.
Projects using an earlier SDK should read [upgrading](Documentation~/upgrading.md)
before installing. The SDK never deletes your existing integration or player data.

## Try the wallet demo

1. Choose **Blockmaker → Setup Wallet Demo** to add a small wallet UI to your
   current scene. It creates a UI document and panel settings for you.
2. On the new **Blockmaker Wallet Demo** component, set your **public Game ID**
   from your registered Blockmaker game. The API defaults to
   `https://blockmaker.polaris.city`; set the app name and your own font if desired.
3. Leave **Public Email Client ID** empty for Pera. To enable email, complete
   [email setup](Documentation~/email-setup.md) and enter the reviewed **public**
   provider client ID. Email is optional; installing the SDK does not provision it.
4. Choose **Blockmaker → Install Fullscreen Web Template**, save the scene, add
   it to your Web build profile and build. Host it on the exact origin registered
   for your game and, for email, allowed by the provider project.
5. Open the browser build and select **Sign in**. With email configured, the
   game offers **Email** first and **Wallet** second. Choose **Wallet → Pera**
   to connect Pera; its original logo is included.

Unity Editor is useful for scene/UI checks. Wallet sign-in runs in the **WebGL
browser build**, not Editor Play mode. The demo performs no transactions.
If you already have a wallet integration, use its existing receiver rather than
adding a second demo/manager. An importable demo is also in Package Manager's
**Samples** section.

## What is included

- A reusable C# client and wallet bridge with verified player-session acknowledgment.
- In-game Pera QR, approval progress and cancellation; configurable fonts,
  colours and wording.
- Provider-owned email, verification and recovery UI. A popup may be required by
  the provider/browser; the included template uses document-root fullscreen.
- Transaction signing and shared-profile APIs. Your game supplies transaction
  review, submission/recovery and its own gameplay rules.

The wallet manager also supports Lute through its lower-level account path;
the included in-game picker presents Email and Wallet → Pera.
Registered usernames and profile pictures follow the connected **owner wallet**.
Game sessions and game progress stay scoped to the game. Using the same email
in unrelated provider projects does **not** guarantee the same wallet; see
[email identity setup](Documentation~/email-setup.md).

## Integration and current validation

Use the [integration guide](Documentation~/integration.md) for custom UI,
profiles, transaction signing and hosting. Never put server keys or wallet
recovery material in Unity. Email login does not fund a wallet or enable paused
trading/rewards.

[Validation details](Documentation~/validation.md) distinguish package/build/UI
checks from attended wallet tests. Real email verification, returning-wallet
recovery, cross-game identity and live economic operations still need player
acceptance checks. A successful install or visible sign-in form does not prove
those flows are complete.

## Licence

Blockmaker code uses the [MIT licence](LICENSE). Dependencies retain their own
licences and provider terms. Keep the bundled
[third-party notices](Browser~/blockmaker-txnlab-wallet.NOTICES.txt), which are
included automatically in the build. No game assets, commercial fonts, private
credentials or player data are distributed in this SDK.
