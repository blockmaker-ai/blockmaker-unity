# Blockmaker Unity SDK

Add Algorand wallet auth and transaction signing to your Unity game. Open-source, free, built for Unity 6+.

## Supported Wallets

| Wallet | How it works | Server needed? |
|--------|-------------|----------------|
| **Pera** | QR code scan (WalletConnect v1) | No |
| **Defly** | QR code scan (WalletConnect v2) | No |
| **X-Chain** | Any EVM wallet (MetaMask, Rainbow, Coinbase + more) via [xChain Accounts](https://github.com/algorandfoundation/xchain-accounts) | No |
| **Email** | Magic SDK (WebGL) or server-managed OTP (all platforms) | Yes |

## Getting Started

### 1. Install the SDK

In Unity: **Window > Package Manager > + > Add package from git URL**

```
https://github.com/blockmaker-ai/blockmaker-unity.git
```

> **Note:** You also need the [Reown Unity SDK](https://docs.reown.com/appkit/unity/core/installation) installed in your project.

### 2. Add to your scene

Add a `BlockmakerAuth` component to any GameObject. That's it — Pera, Defly, and X-Chain wallet connections work immediately. No server, no API key, no signup.

### 3. Optional — enable email login and server features

If you want email login, server-signed transactions, or profile management:

1. Go to [blockmaker.ai/developers](https://blockmaker.ai/developers) — get your API key instantly
2. **Assets > Create > Blockmaker > Config** — paste your Server URL and API Key
3. Assign the config to your BlockmakerAuth component

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
BlockmakerAuth.Instance.ConnectEvm(
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Email login (requires server)
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

| Field | Description |
|-------|-------------|
| `serverUrl` | Optional — your Blockmaker server URL. Only needed for email login and server features |
| `apiKey` | Optional — your API key (`sk_` prefix). **Only used in the Unity Editor, never shipped in player builds** |
| `walletConnectProjectId` | Optional — your own WalletConnect project ID. Leave empty to use the shared default |
| `magicPublishableKey` | Optional — Magic SDK key for email login on WebGL |
| `dAppUrl` | Optional — URL shown in wallet apps when players approve a connection |
| `dAppIconUrl` | Optional — icon shown in wallet apps |
| `walletSignTimeoutSeconds` | How long to wait for wallet approval (default: 120 seconds) |

## Requirements

- Unity 6+ (6000.0)
- .NET Standard 2.1+
- [Reown Unity SDK](https://docs.reown.com/appkit/unity/core/installation)

## License

MIT — see [LICENSE](LICENSE).

## Links

- [Get your API key](https://blockmaker.ai/developers) (optional — for email login and server features)
- [Report an issue](https://github.com/blockmaker-ai/blockmaker-unity/issues)
- Built on [Algorand](https://algorand.co)
