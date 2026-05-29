# Blockmaker SDK for Unity

Open-source SDK to add Algorand wallet connectivity, NFTs, and transactions to Unity games. Built for Unity 6+.

## Features

- **Wallet Connection** — Pera, Defly, MetaMask (EVM xChain), and email login via Magic SDK
- **Transaction Signing** — Single and atomic group transactions across all wallet types
- **Session Management** — Encrypted local storage, automatic token refresh, session restore
- **Profile System** — Usernames, NFT profile pictures, default avatars
- **NFT Support** — ARC-69 metadata parsing, wallet search, image loading
- **Token Tracking** — Real-time balance polling with optimistic updates
- **Pre-built UI** — Auth screens, wallet bar, QR modals, OTP flow (all customizable)
- **Async/Await** — Modern C# API alongside callback-based methods

## Quick Start

### 1. Deploy your server

The SDK needs a [Blockmaker server](https://github.com/blockmaker-ai/blockmaker) backend. Deploy your own in one click:

[![Deploy on Railway](https://railway.app/button.svg)](https://railway.app/template/blockmaker)

### 2. Install the SDK

In Unity, go to **Window > Package Manager > + > Add package from git URL** and enter:

```
https://github.com/blockmaker-ai/blockmaker-unity.git
```

> **Prerequisite:** Install the [Reown Unity SDK](https://docs.reown.com/appkit/unity/core/installation) first — it provides WalletConnect v2 support for Defly and EVM wallets.

### 3. Create a config

**Assets > Create > Blockmaker > Config**, then fill in:
- **Server URL** — Your deployed server's URL (e.g. `https://your-app.up.railway.app`)
- **API Key** — From your server dashboard (`sk_` prefix)
- **WalletConnect Project ID** — Free from [cloud.walletconnect.com](https://cloud.walletconnect.com)

### 4. Add to your scene

Add `BlockmakerAuth` to a GameObject. Assign your config. That's it — the SDK auto-creates `BlockmakerClient` and `BlockmakerProfileManager`.

## Usage

```csharp
using Blockmaker;

// Connect a wallet
BlockmakerAuth.Instance.ConnectWallet("Pera",
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log($"Error: {error}")
);

// Or use async/await
var identity = await BlockmakerAuth.Instance.ConnectWalletAsync("Pera");

// Sign a transaction
yield return identity.SignTransaction(unsignedTxnBase64,
    signed => Debug.Log("Signed!"),
    error  => Debug.Log(error)
);

// Check identity state
if (BlockmakerAuth.Instance.HasWallet)
    Debug.Log($"Address: {BlockmakerAuth.Instance.Address}");
```

## Architecture

```
BlockmakerAuth          — Singleton auth manager, identity lifecycle
BlockmakerClient        — HTTP client for your server
BlockmakerProfileManager — Profile state (username, avatar, onboarding)
IBlockmakerIdentity     — Identity interface (Guest, Email, WC, Magic, EVM)
SecurePrefs             — Encrypted PlayerPrefs (AES-256-CBC + HMAC)
ReownWalletConnector    — WalletConnect v2 via Reown SDK
BlockmakerLog           — Configurable logging with levels and hooks
```

## Configuration

All settings live on the `BlockmakerConfig` ScriptableObject:

| Field | Description |
|-------|-------------|
| `serverUrl` | Your Blockmaker server URL |
| `apiKey` | Server API key (sk_ prefix) — **Editor only, never shipped in builds** |
| `walletConnectProjectId` | Free from cloud.walletconnect.com |
| `magicPublishableKey` | Magic SDK key for email login (WebGL only) |
| `dAppUrl` | URL shown in wallet apps during approval (defaults to serverUrl) |
| `dAppIconUrl` | Icon shown in wallet apps (defaults to dAppUrl + /icon.png) |
| `walletSignTimeoutSeconds` | Timeout for wallet interactions (default: 120s) |

## Logging

```csharp
// Silence SDK logs in production
BlockmakerLog.Level = BlockmakerLogLevel.Error;

// Intercept logs for your analytics
BlockmakerLog.OnLog += (level, msg) => MyAnalytics.Track(msg);
```

## Namespace

All SDK types are in the `Blockmaker` namespace:

```csharp
using Blockmaker;
```

## Requirements

- Unity 6+ (6000.0)
- .NET Standard 2.1+
- [Reown Unity SDK](https://docs.reown.com/appkit/unity/core/installation) (for WalletConnect v2)

## License

MIT — see [LICENSE](LICENSE).
