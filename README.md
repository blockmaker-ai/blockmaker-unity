# Blockmaker Unity SDK

Add Algorand wallet auth and transaction signing to your Unity game. Open-source, free, built for Unity 6+.

## Supported Wallets

| Wallet | How it works |
|--------|-------------|
| **Pera** | QR code scan (WalletConnect v1) |
| **Defly** | QR code scan (WalletConnect v2) |
| **X-Chain** | Any EVM wallet (MetaMask, Rainbow, Coinbase + more) via [xChain Accounts](https://github.com/algorandfoundation/xchain-accounts) |
| **Email** | Magic SDK (WebGL) or server-managed OTP (all platforms) |

## Installation

### Step 1 — Add the Reown registry

Open `Packages/manifest.json` in your Unity project and add this `scopedRegistries` block at the end, just before the final `}`:

```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": [
      "com.reown",
      "com.nethereum",
      "com.cysharp"
    ]
  }
]
```

Then add this line inside the `"dependencies"` section:

```json
"com.reown.sign.unity": "1.6.0",
```

Save the file.

### Step 2 — Add the Blockmaker SDK

In Unity: **Window > Package Manager > + > Add package from git URL**

```
https://github.com/blockmaker-ai/blockmaker-unity.git
```

Wait for Unity to resolve both packages. Once it compiles with no errors, you're ready.

### Step 3 — Set up your scene

1. **Assets > Create > Blockmaker > Config** — creates a config asset (no fields required, defaults work)
2. Add a **BlockmakerAuth** component to any GameObject
3. Assign your config asset to it

Done — all wallet types work out of the box.

### Full manifest.json example

If you prefer, here's what a clean `manifest.json` looks like with both packages:

```json
{
  "dependencies": {
    "com.blockmaker.sdk": "https://github.com/blockmaker-ai/blockmaker-unity.git",
    "com.reown.sign.unity": "1.6.0",
    ...your other packages...
  },
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.reown",
        "com.nethereum",
        "com.cysharp"
      ]
    }
  ]
}
```

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

// Email login
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

All fields are optional — the SDK works with defaults out of the box.

| Field | Description |
|-------|-------------|
| `serverUrl` | Your own Blockmaker server URL. Leave empty to use the shared default |
| `apiKey` | Your own API key (`sk_` prefix). **Only used in the Unity Editor, never shipped in player builds** |
| `walletConnectProjectId` | Your own WalletConnect project ID. Leave empty to use the shared default |
| `magicPublishableKey` | Magic SDK key for email login on WebGL |
| `dAppUrl` | URL shown in wallet apps when players approve a connection |
| `dAppIconUrl` | Icon shown in wallet apps |
| `walletSignTimeoutSeconds` | How long to wait for wallet approval (default: 120 seconds) |

## Requirements

- Unity 6+ (6000.0)
- .NET Standard 2.1+

## License

MIT — see [LICENSE](LICENSE).

## Links

- [Report an issue](https://github.com/blockmaker-ai/blockmaker-unity/issues)
- Built on [Algorand](https://algorand.co)
