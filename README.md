# Blockmaker Unity SDK

Add Algorand wallet auth and transaction signing to your Unity game. Open-source, free, built for Unity 6+.

## Supported Wallets

| Wallet | How it works |
|--------|-------------|
| **Pera** | QR code scan (WalletConnect v1). On WebGL the SDK uses Pera's official [@perawallet/connect](https://github.com/perawallet/connect); mobile browsers get an "Open in wallet app" deep link |
| **Defly** | QR code scan (WalletConnect v2), mobile deep link per Defly's official SDK |
| **X-Chain** *(experimental)* | Any EVM wallet (MetaMask, Rainbow, Coinbase + more) via [xChain Accounts](https://github.com/algorandfoundation/xchain-accounts). Native builds only for now — the WebGL browser path requires a self-hosted xChain JS bundle |
| **Email** | Server-managed OTP out of the box (all platforms); optionally Magic SDK on WebGL with your own key |

> Currently Algorand **mainnet** only.

## Installation

### One-click install

1. **[Download BlockmakerInstaller.cs](https://raw.githubusercontent.com/blockmaker-ai/blockmaker-unity/main/Installer~/BlockmakerInstaller.cs)** (right-click → Save As)
2. Drag it into your Unity project (anywhere in the Assets folder)
3. A dialog will appear — click **Install**
4. Unity installs everything automatically. Done.

You can delete the installer file after installation.

<details>
<summary><b>Manual install (if you prefer)</b></summary>

Open `Packages/manifest.json` in your project folder and add:

**In `"dependencies"`:**
```json
"com.blockmaker.sdk": "https://github.com/blockmaker-ai/blockmaker-unity.git#v1.1.0",
"com.nethereum.unity": "4.19.2",
"com.reown.sign.nethereum": "1.6.0",
"com.reown.sign.unity": "1.6.0",
```

**At the bottom of the file, before the last `}`:**
```json
,
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": ["com.reown", "com.nethereum", "com.cysharp"]
  }
]
```

Save and reopen Unity.

</details>

## Setup

Go to **Blockmaker > Setup Scene** in the menu bar. This automatically:

- Creates a BlockmakerConfig asset
- Adds BlockmakerAuth with the config wired up
- Adds the auth UI with all UXML assets pre-assigned
- Creates PanelSettings if needed
- Adds a Connect Wallet button

Hit Play — the wallet system is ready. No API keys or signups needed.

### Sample scene

Want to see it working first? In **Package Manager > Blockmaker SDK > Samples**, import the **Wallet Demo**. Add the `WalletDemo` script to an empty GameObject and hit Play — includes wallet connection and transaction signing.

## Usage

### Connect a wallet

```csharp
using Blockmaker;

// Pera (QR code)
BlockmakerAuth.Instance.ConnectWallet("Pera",
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Defly (QR code)
BlockmakerAuth.Instance.ConnectWallet("Defly",
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// X-Chain — any EVM wallet (MetaMask, Rainbow, Coinbase + more)
// Experimental: native builds only for now (see Supported Wallets); enable via
// enableEvmXChain in your BlockmakerConfig.
BlockmakerAuth.Instance.ConnectEvm(
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Email login — built-in server OTP (works everywhere, no keys needed)
BlockmakerAuth.Instance.RequestEmailOTP("player@example.com",
    ()    => Debug.Log("Code sent — check your inbox"),
    error => Debug.Log(error)
);
BlockmakerAuth.Instance.VerifyEmailOTP("player@example.com", "123456",
    identity => Debug.Log($"Signed in: {identity.Address}"),
    error    => Debug.Log(error)
);

// Email login via Magic SDK — WebGL only, requires magicPublishableKey in your config
// (without a key the built-in auth UI automatically uses the OTP flow above)
BlockmakerAuth.Instance.ConnectMagicEmail("player@example.com",
    identity => Debug.Log($"Signed in: {identity.Address}"),
    error    => Debug.Log(error)
);
```

### Async/await

```csharp
var identity = await BlockmakerAuth.Instance.ConnectWalletAsync("Pera");
var evmIdentity = await BlockmakerAuth.Instance.ConnectEvmAsync();
```

### Sign transactions

```csharp
var identity = BlockmakerAuth.Instance.Identity;

// Single transaction
yield return identity.SignTransaction(unsignedTxnBase64,
    signed => Debug.Log("Signed!"),
    error  => Debug.Log(error)
);

// Atomic group
yield return identity.SignTransactions(unsignedTxnsBase64,
    signed => Debug.Log($"Signed {signed.Length} transactions"),
    error  => Debug.Log(error)
);
```

### Check state

```csharp
BlockmakerAuth.Instance.HasWallet    // true if a wallet is connected
BlockmakerAuth.Instance.Address      // Algorand address
BlockmakerAuth.Instance.CanSign      // true if the wallet can sign right now
BlockmakerAuth.Instance.IsLoggedIn   // true for Email or SelfCustody tier
BlockmakerAuth.Instance.Tier         // Guest, Email, or SelfCustody
```

### Listen for changes

```csharp
void OnEnable()
{
    BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;
}

void OnDisable()
{
    BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
}

void HandleIdentityChanged(IBlockmakerIdentity identity)
{
    Debug.Log($"Identity changed: {identity.ProviderName} — {identity.Address}");
}
```

### Session restore & sign-in status

Self-custody sign-in is **two approvals**: connect, then a signature that logs the
player into the backend. The SDK exposes everything a custom UI needs to render that
honestly:

```csharp
// Boot-time restore: don't guess — wait for the restore to settle.
if (BlockmakerAuth.SessionRestoreSettled) { /* auth state is known */ }
BlockmakerAuth.OnSessionRestoreSettled += () =>
    Debug.Log($"Restore settled — tier: {BlockmakerAuth.Instance.Tier}");

// Progress messages while the player must act in their wallet app.
BlockmakerAuth.OnAuthStatus += msg => statusLabel.text = msg;

// The step-2 (login signature) window: offer RESEND / CANCEL instead of a dead end.
if (BlockmakerAuth.CanRetryWalletLogin)
{
    BlockmakerAuth.Instance.RetryWalletLogin();   // push a fresh sign-in request
    // or: BlockmakerAuth.Instance.CancelWalletLogin();
}

// Skip best-effort backend calls that would 401 for guests.
if (BlockmakerClient.Instance.HasBackendSession) { /* safe to call player APIs */ }
```

The built-in auth UI already implements all of this (a "STEP 2 OF 2" panel with
RESEND / CANCEL).

### WebGL: self-hosting the wallet JS (optional)

On WebGL the SDK loads its wallet libraries from jsDelivr at runtime — zero setup.
For immunity to ad-blockers and CDN outages you can ship the bundles with your game:
build IIFE bundles that assign `window.BmPeraVendor = { PeraWalletConnect, algosdk }`
and/or `window.BmWCVendor = { SignClient, QRCode }`, and load them via `<script>` tags
before the Unity loader (e.g. from your WebGL template's `TemplateData/`). The SDK
uses them automatically and only falls back to the CDN when they're absent.
If you distribute such bundles, retain the upstream license notices (MIT for
@perawallet/connect, algosdk and qrcode; Apache-2.0 — including its NOTICE terms —
for @walletconnect/sign-client).

### Logout

```csharp
BlockmakerAuth.Instance.Logout();
```

## Pre-built UI

The SDK includes ready-to-use UI screens built with UI Toolkit:

- **Auth screen** — wallet selection, QR display, email OTP flow
- **Wallet bar** — shows connected address, tier badge
- **QR modal** — Pera/Defly/X-Chain connection with copy-link fallback
- **Wallet upgrade prompt** — nudges guests to connect

All UI is optional — you can build your own using the `BlockmakerAuth` API directly.

## Logging

SDK logs are silent in release builds by default. To adjust:

```csharp
// Show all SDK logs (useful for debugging)
BlockmakerLog.Level = BlockmakerLogLevel.Verbose;

// Intercept logs for your own system
BlockmakerLog.OnLog += (level, msg) => MyLogger.Log(msg);
```

## Configuration Reference

All fields are optional — the SDK works with defaults out of the box.

| Field | Description |
|-------|-------------|
| `serverUrl` | Your own Blockmaker server URL. Leave empty to use the shared default |
| `apiKey` | Your own API key (`sk_` prefix). **Only used in the Unity Editor, never shipped in player builds** |
| `walletConnectProjectId` | Your own WalletConnect project ID. Leave empty to use the shared default |
| `magicPublishableKey` | Magic SDK key for email login on WebGL. Without it, email login uses the built-in server OTP flow |
| `enableEvmXChain` | Enable the experimental X-Chain EVM wallet login (default: off) |
| `dAppUrl` | URL shown in wallet apps when players approve a connection |
| `dAppIconUrl` | Icon shown in wallet apps |
| `walletSignTimeoutSeconds` | How long to wait for wallet approval (default: 120 seconds) |

## Requirements

- Unity 6+ (6000.0)
- .NET Standard 2.1+
- Algorand mainnet (testnet support is on the roadmap)

## License

MIT — see [LICENSE](LICENSE).

## Links

- [Report an issue](https://github.com/blockmaker-ai/blockmaker-unity/issues)
- Built on [Algorand](https://algorand.co)
