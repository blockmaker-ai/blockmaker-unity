# Blockmaker Unity SDK

Add Pera and optional passwordless email wallets to Unity WebGL games on
Algorand MainNet. The current package provides an in-game Pera QR/approval UI,
provider-owned email authentication, verified player sessions and exact-group
transaction signing. Fonts, colours, wording, game ID and API are configurable.

**Current release: [WebGL Pera + email preview, 16 September 2026](https://github.com/blockmaker-ai/blockmaker-unity/releases/tag/webgl-preview-20260916).**

- [Install and integrate](WebGL~/README.md)
- [Configure email and shared player identity](WebGL~/EMAIL_SETUP.md)
- [Minimal Unity sample and fullscreen host](WebGL~/Samples~/EmailWallet/README.md)
- [Validation and remaining checks](WebGL~/VALIDATION.md)

| Profile | Wallets |
| --- | --- |
| `pera_lute` | Pera and Lute; email is disabled |
| `pera_lute_txnlab_web3auth` | Pera, Lute and TxnLab/MetaMask Embedded Wallets email; all are economic Algorand wallets |

Email requires a reviewed provider project and the game's exact approved
origin. It is optional, does not fund a wallet and does not bypass game rules.
The older `web3auth_avm_email` authentication-only broker remains separate.

Shared usernames and profile pictures follow the connected **owner wallet**
across games. Game sessions, progress and assets retain their existing scope.
Equal email addresses alone never establish the same wallet or merge accounts.

This release is for **Unity WebGL/MainNet**. Unity Editor can check C# and UI;
wallet authentication and browser fullscreen require an actual WebGL build.
Native Windows, macOS, Android and iOS exports are not supported by this package.

## Existing v1 projects

The repository's root UPM package and `Installer~` remain **legacy v1.2.0**.
Installing the root Git URL does not install the current WebGL package. Existing
projects can stay pinned to `#v1.2.0`; see the [historical guide](LEGACY_V1.md).
Migrate the old APIs and scene objects explicitly before installing the new
package. Never run both wallet managers in one game. Unrelated legacy work is
preserved in its existing branches and pull requests.

## Licensing

Blockmaker contributions use the [MIT licence](LICENSE). Bundled dependencies
retain their own licences and provider terms, including MetaMask's terms.
Distribute the complete [third-party notices](WebGL~/Assets/StreamingAssets/Blockmaker/blockmaker-txnlab-wallet.NOTICES.txt).
No game artwork, commercial fonts, provider credentials or player data are
included.
