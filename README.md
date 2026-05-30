# Blockmaker Unity SDK

Add Algorand wallet auth and transaction signing to your Unity game. Open-source, free, built for Unity 6+.

## Supported Wallets

| Wallet | How it works |
|--------|-------------|
| **Pera** | QR code scan (WalletConnect v1) |
| **Defly** | QR code scan (WalletConnect v2) |
| **EVM wallets** | MetaMask, Rainbow, etc. via [xChain Accounts](https://github.com/algorandfoundation/xchain-accounts) — signs with an EVM wallet, gets an Algorand address |
| **Email** | Magic SDK (WebGL) or server-managed OTP (all platforms) |

## Getting Started

### 1. Get your API key

Go to [blockmaker.ai/developers](https://blockmaker.ai/developers) — enter your name and email, get your key instantly.

### 2. Install the SDK

In Unity: **Window > Package Manager > + > Add package from git URL**

```
https://github.com/blockmaker-ai/blockmaker-unity.git
```

You also need the [Reown Unity SDK](https://docs.reown.com/appkit/unity/core/installation) for Defly and EVM wallet support.

### 3. Configure

1. **Assets > Create > Blockmaker > Config**
2. Fill in:
   - **Server URL** — from step 1
   - **API Key** — from step 1

That's it — Pera, Defly, xChain, and email login all work out of the box.

### 4. Add to your scene

Add a `BlockmakerAuth` component to any GameObject and assign your config. Done.

## Usage

### Connect a wallet

```csharp
using Blockmaker;

// Connect Pera (QR code)
BlockmakerAuth.Instance.ConnectWallet("Pera",
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Connect Defly (QR code)
BlockmakerAuth.Instance.ConnectWallet("Defly",
    identity => Debug.Log($"Connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Connect an EVM wallet (MetaMask, Rainbow, etc.)
BlockmakerAuth.Instance.ConnectEvm(
    identity => Debug.Log($"EVM connected: {identity.Address}"),
    error    => Debug.Log(error)
);

// Email login (Magic SDK on WebGL, OTP on other platforms)
BlockmakerAuth.Instance.ConnectMagicEmail("player@example.com",
    identity => Debug.Log($"Email signed in: {identity.Address}"),
    error    => Debug.Log(error)
);
```

### Async/await

All connect and sign methods have async overloads:

```csharp
var identity = await BlockmakerAuth.Instance.ConnectWalletAsync("Pera");
Debug.Log($"Connected: {identity.Address}");

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

// Atomic group (all wallet types, all platforms)
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
// Identity reverts to Guest, session is cleared, tokens invalidated
```

## Pre-built UI

The SDK includes ready-to-use UI screens built with UI Toolkit:

- **Auth screen** — wallet selection, QR display, email OTP flow
- **Wallet bar** — shows connected address, tier badge
- **QR modal** — Pera/Defly connection with copy-link fallback
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
| `serverUrl` | Your Blockmaker server URL (from the developer signup) |
| `apiKey` | Your API key (`sk_` prefix) — **only used in the Unity Editor, never shipped in player builds** |
| `walletConnectProjectId` | Optional — your own WalletConnect project ID. Leave empty to use the Blockmaker shared ID (works out of the box) |
| `magicPublishableKey` | Optional — Magic SDK key for email login on WebGL. Get one at [dashboard.magic.link](https://dashboard.magic.link) |
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

- [Get your API key](https://blockmaker.ai/developers)
- [Report an issue](https://github.com/blockmaker-ai/blockmaker-unity/issues)
- Built on [Algorand](https://algorand.co)
