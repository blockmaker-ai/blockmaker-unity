# Changelog

## [1.2.0] - 2026-07-10

### Added
- EVM wallets on WebGL (beta): full zero-bundle browser path — no CDN downloads,
  no self-hosted bundle. The browser layer is a thin EIP-1193 transport; address
  derivation, EIP-712 payloads and LogicSig transaction assembly all happen in C#
  (proven against the on-chain xChain LogicSig via mainnet simulation, including
  atomic group signing with a single wallet approval)
- Wallet picker in the built-in auth prompt: one flat list with Pera and Defly on
  top, then every EVM wallet installed in the player's browser (EIP-6963 discovery,
  real wallet icons rasterized in-browser); curated MetaMask / Rainbow /
  Coinbase Wallet / Trust rows on native builds (WalletConnect QR); skeleton row
  while discovery runs, "Find one" link when no wallet is installed
- `BlockmakerAuth.DiscoverEvmWallets(Action<string>)` and
  `ConnectEvmWallet(rdns, onSuccess, onError)` — the discover/connect split behind
  the picker, usable from custom UIs; `CancelEvmConnect()` aborts cleanly
- Per-wallet connect errors: EIP-1193 codes surface as "code|message" on the picker
  path — 4001 (declined, retry offered) and -32002 (request already pending in the
  wallet, no duplicate popup) get dedicated player-friendly states
- EVM session restore on WebGL: silent re-attach after a page reload (eth_accounts,
  no popup), remembered last-used wallet
- `BlockmakerWalletBridge.OpenUrlInNewTab` — informational links open a new tab on
  WebGL instead of navigating the game away

### Changed
- Auth modal redesign: unified wallet list, corner back button, real brand logos,
  crisp font-free chevron/close geometry shared via BlockmakerTheme.uss, hidden
  scrollbar with an overflow fade (wheel/touch scrolling unaffected)
- Wallet-picker safety: a picked wallet that disappears fails cleanly (no silent
  fallback to another wallet), and a stale approval from a cancelled attempt can
  never log the player into the wrong wallet (rdns echo guard); wallet names from
  discovery are length-clamped and rendered with rich text off
- The `OnEvmConnected` bridge callback (public on `BlockmakerAuth`, but
  bridge-internal in practice) now receives "rdns|0xEvmAddress" instead of a
  bare address

## [1.1.0] - 2026-07-09

### Added
- Wallet backend login: Pera, Defly, and EVM (xChain) wallets now obtain a player JWT
  via challenge → wallet signature → verify, so self-custody players can call
  player-authenticated endpoints (previously JWTs existed only for email login).
  Pera signs a 0-amount self-payment carrying the nonce (WalletConnect v1 cannot
  sign raw bytes); EVM wallets sign via personal_sign
- Sign-in step-2 recovery: `BlockmakerAuth.RetryWalletLogin()`, `CancelWalletLogin()`
  and `CanRetryWalletLogin` — resend a missed/expired login-signature request or abort
  cleanly; the built-in auth UI shows a "STEP 2 OF 2" panel with RESEND / CANCEL until
  the session token lands
- `BlockmakerAuth.OnAuthStatus` — progress messages for moments when the player must
  act in their wallet app (e.g. "approve the sign-in request in your wallet")
- `BlockmakerAuth.SessionRestoreSettled` + `OnSessionRestoreSettled` — boot-time
  session restore announces success / sign-in-required / no-session, so custom UIs can
  render a resolving state instead of a stale guess
- `BlockmakerClient.HasBackendSession` — skip best-effort backend calls that would
  401 for guests
- Mobile web wallet support: Pera on WebGL via the official @perawallet/connect
  (WalletConnect v1, in-canvas QR, localStorage session restore), "Open in wallet app"
  deep-link buttons on mobile browsers (Pera universal link; Defly per its official
  SDK), and email/OTP soft-keyboard type hints
- Optional page-vendored JS bundles: `window.BmWCVendor` / `window.BmPeraVendor` are
  used before any CDN import — immune to ad-blockers and CDN outages (see README)
- Build-time API-key stripper (`Editor/BlockmakerBuildKeyStripper`): the dev API key is
  removed from every BlockmakerConfig asset during player builds and restored after —
  it can never ship in a build
- `SendReward` optional `contextId` idempotency key — the server dedups retried sends
- `BlockmakerClient.Post` overload with an explicit timeout for slow endpoints
- `TokenBalanceTracker.AddPendingReward` accepts negative corrections

### Changed
- Session restore hardened: browser-held wallet sessions (Pera JS, Magic, EVM) restore
  independently of Reown initialization; a failed wallet token refresh keeps the wallet
  identity and re-signs for a fresh session instead of dropping the player to Guest
- Email OTP verification errors render inline on the code-entry page — a typo no
  longer ejects the player from the flow
- Balance tracker and profile manager only call the backend once a real player session
  exists (no more guest 401 spam); balance polling survives silent token refresh
- Auth UI: build-safe glyphs (WebGL players have no OS font fallback), visible close
  button, and a monochrome wallet-connect modal restyle
- xChain EVM login now defaults OFF (`enableEvmXChain`) and is marked experimental:
  it works in native builds, while the WebGL browser path currently requires a
  self-hosted xChain JS bundle

### Fixed
- WebGL builds failing at the Emscripten minifier (ES2020 optional chaining removed
  from the .jslib)
- WalletConnect v1 `CanSign` now requires a live connection, not just a constructed
  client (signing no longer dead-ends after a restore without a reconnect)
- Sign-in prompt closing prematurely at approval 1 of 2; zombie login completion after
  logout; duplicate signature prompts from concurrent login triggers (seq-guarded)
- USS import warnings (unsupported z-index / box-shadow removed)

## [1.0.1] - 2026-05-30

### Changed
- Profile screen UI (profile panel, profile button) moved out of the public package
  while it matures; the underlying profile manager remains

## [1.0.0] - 2026-05-29

### Added
- Wallet connection: Pera (WalletConnect v1), Defly (WalletConnect v2/Reown), EVM wallets (xChain Accounts)
- Email login via Magic SDK (WebGL) and server-managed OTP (all platforms)
- Guest identity with device-persistent ID
- Transaction signing across all identity types (single and atomic groups)
- Session persistence via encrypted SecurePrefs (AES-256-CBC + HMAC)
- Proactive JWT token refresh
- QR code generation for wallet pairing
- Full Algorand address checksum validation (Base32 + SHA-512/256)
- EVM signature lower-S normalization (ECDSA malleability protection)
- Configurable logging (BlockmakerLog) with log levels and event hooks
- Async/await API overloads (BlockmakerAsyncExtensions)
- Domain reload support for Unity Enter Play Mode
- Pre-built UI controllers for auth flow, wallet bar, QR modal, and OTP
