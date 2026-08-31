# Blockmaker Unity SDK

Add Algorand wallet auth and transaction signing to your Unity game. Open-source, free, built for Unity 6+.

## Supported Wallets

| Wallet | How it works |
|--------|-------------|
| **Pera** | QR code scan (WalletConnect v1) |
| **Defly** | QR code scan (WalletConnect v2) |
| **Lute** | Browser wallet or extension (WebGL) |
| **X-Chain** | Any EVM wallet (MetaMask, Rainbow, Coinbase + more) via [xChain Accounts](https://github.com/algorandfoundation/xchain-accounts) |
| **Email** | Magic SDK (WebGL) or server-managed OTP (all platforms) |

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
"com.blockmaker.sdk": "https://github.com/blockmaker-ai/blockmaker-unity.git",
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

## Create or connect a Blockmaker project

1. Open [blockmaker.ai](https://blockmaker.ai) and start setup from the public site.
2. Sign the free setup message with the wallet that will own the project.
3. Add your game's exact web origin when Blockmaker asks for it.
4. On the project's **Integration** page, copy the public game ID. It is safe to ship in a game client.

Each Blockmaker account can own multiple games. Every game has its own public ID, player data,
allowed origins, keys, and treasury policy. Never copy a server key (`sk_...`) into Unity code,
a `BlockmakerConfig`, a WebGL template, or source control.

## Unity setup

Go to **Blockmaker > Setup Scene** in the menu bar. This automatically:

- Creates a BlockmakerConfig asset
- Adds BlockmakerAuth with the config wired up
- Adds the auth UI with all UXML assets pre-assigned
- Creates PanelSettings if needed
- Adds a Connect Wallet button

Select the generated `BlockmakerConfig` and paste the public game ID from the Integration page
into `gameId`. Leave `serverUrl` empty to use `https://blockmaker.polaris.city`, or set an exact
HTTPS origin for a self-hosted deployment. Hit Play — no server key is required in the game.

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

// Lute (browser wallet or extension in WebGL)
BlockmakerAuth.Instance.ConnectWallet("Lute",
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

If game code owns a signing deadline and stops either coroutine, release that
exact wallet's pending WebGL callback first. The identity check prevents an
unrelated wallet request from being cancelled:

```csharp
BlockmakerAuth.Instance.CancelPendingWalletSign(identity);
StopCoroutine(signCoroutine);
```

Use transaction bytes returned by a Blockmaker builder (`BuildAssetOptIn`, a shop prepare call,
and similar purpose-built endpoints). For server-managed email wallets, the SDK automatically
binds the builder's short-lived `signingIntent` to those exact bytes and echoes it when signing.
It refuses arbitrary or expired transaction bytes. Self-custody wallets still show the player the
normal wallet approval.

Lute's web signer opens a separate approval window. If a button first calls your server to prepare
a transaction and signs it later, call `BlockmakerAuth.Instance.PrimeWalletApprovalWindow()`
directly inside that button's click handler before starting the request. It is a no-op for Pera,
Defly, X-Chain and email wallets. For Lute it reserves the approval window without sending account
or transaction data, then the exact prepared transaction reuses that window when it is ready.

Managed email wallets can sign at most five transactions in one group. Chunk larger opt-in batches
at five if the same integration must support both managed and self-custody wallets (Algorand itself
allows up to sixteen transactions per atomic group).

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

## NFTURBO Pack-Shop scoped access

NFTURBO's WebGL Store uses a separate short-lived Pera/Lute proof. It is not a
normal Blockmaker player session: the credential stays in memory, is never
returned to game code, and can be attached only by the dedicated Pack-Shop
request helpers.

The built-in prompt is the narrowest integration for a Store entry button. It
shows only Pera and Lute and fires its own success event; the normal auth event
and Email/Defly/EVM choices are unchanged:

```csharp
AuthPromptController.OnNfturboPackShopAuthSucceeded += LoadStore;
AuthPromptController.Instance.ShowNfturboPackShopWallets();
```

Use `ConnectNfturboPackShopWallet` instead of `ConnectWallet` when the Store
needs a new wallet connection. Pera can continue to `Ensure...` from the
connection callback. Lute needs a second player click after connecting so the
browser can open its approval window; call `Ensure...` directly from that click.

```csharp
void ConnectStoreWallet(string provider)
{
    BlockmakerAuth.Instance.ConnectNfturboPackShopWallet(
        provider,
        identity =>
        {
            if (identity is LuteIdentity)
            {
                ShowContinueWithLuteButton(BeginStoreSession);
                return;
            }
            BeginStoreSession(); // Pera
        },
        ShowStoreError);
}

void BeginStoreSession()
{
    BlockmakerClient.Instance.EnsureNfturboPackShopSession(
        onSuccess: LoadStore,
        onError: ShowStoreError);
}

void LoadStore()
{
    BlockmakerClient.Instance.GetNfturboPackShop<StoreResponse>(
        "/v1/pack-shop/info",
        RenderStore,
        ShowStoreError);
}
```

Use `GetNfturboPackShop` / `PostNfturboPackShop` for every player-facing
`/v1/pack-shop` request. Keep all non-Shop traffic on the existing generic SDK
methods. A 401/403 clears only the scoped credential; ask the player to approve
Store access again rather than falling back to a generic token or API key.

Paid purchases that still need ASA acceptance must use the scoped, commit-bound
opt-in pair. Sign only `unsignedTxnsBase64`, validate against the returned
`assetIds` subset, and echo the opaque `optInIntent` unchanged. When that subset
is empty, every asset is already opted in and no wallet prompt is needed.

```csharp
BlockmakerClient.Instance.PrepareNfturboPackShopOptIns(
    commitId, requestedAssetIds,
    prepared =>
    {
        if (prepared.assetIds.Length == 0) { ResumeReveal(); return; }
        StartCoroutine(BlockmakerAuth.Instance.Identity.SignTransactions(
            prepared.unsignedTxnsBase64,
            signed => BlockmakerClient.Instance.SubmitNfturboPackShopOptIns(
                commitId, signed, prepared.optInIntent,
                submitted => ResumeReveal(), ShowStoreError),
            ShowStoreError));
    },
    ShowStoreError);
```

Scoped challenge, verification, GET, and POST requests do not follow redirects,
so the signed proof and short-lived credential remain bound to the configured
Blockmaker origin.

## Logging

SDK logs are silent in release builds by default. To adjust:

```csharp
// Show all SDK logs (useful for debugging)
BlockmakerLog.Level = BlockmakerLogLevel.Verbose;

// Intercept logs for your own system
BlockmakerLog.OnLog += (level, msg) => MyLogger.Log(msg);
```

## Configuration Reference

The public `gameId` is required. Other integration fields have safe defaults or are optional.

| Field | Description |
|-------|-------------|
| `gameId` | **Required public game ID** from the project's Integration page; safe to ship |
| `serverUrl` | Exact Blockmaker HTTPS origin. Leave empty for `https://blockmaker.polaris.city`; localhost HTTP is allowed for development |
| `apiKey` | Optional server key for trusted Editor tools only. **Never use it in gameplay code or a player build** |
| `walletConnectProjectId` | Your own WalletConnect project ID. Leave empty to use the shared default |
| `magicPublishableKey` | Magic SDK key for email login on WebGL |
| `dAppUrl` | URL shown in wallet apps when players approve a connection |
| `dAppIconUrl` | Icon shown in wallet apps |
| `walletSignTimeoutSeconds` | How long to wait for wallet approval (default: 120 seconds) |

## Security model

- Every SDK request asserts the configured public game ID. A session or refresh token from one
  game cannot be silently reused for another game.
- Player builds use short-lived player JWTs. The build-time key stripper removes an Editor server
  key if one was placed in a config asset, but you should still keep server keys out of the repo.
- Refresh-token rotation is single-flight: concurrent requests share one exchange, avoiding the
  replay protection that correctly revokes a refresh token used twice.
- Managed-wallet signing accepts only exact, short-lived transaction intents created by a
  Blockmaker builder. If you see `TX_INTENT_REQUIRED` or `TX_INTENT_EXPIRED`, rebuild the action;
  never retry with hand-authored transaction bytes.
- `GAME_MISMATCH` means the config, player session, and requested project do not agree. Clear the
  saved session, verify `gameId`, and sign in again. Do not work around it by removing the game ID.
- API paths passed to `Get` and `Post` must start with `/v1/`; the SDK will not send credentials to
  an absolute or off-origin URL.

## Requirements

- Unity 6+ (6000.0)
- .NET Standard 2.1+

## License

MIT — see [LICENSE](LICENSE).

## Links

- [Report an issue](https://github.com/blockmaker-ai/blockmaker-unity/issues)
- Built on [Algorand](https://algorand.co)
