# Changelog

## [2.1.0] - 2026-08-31

### Added
- Add NFTURBO-only Pera/Lute WebGL authentication using the server-authored Pack-Shop challenge transaction and a memory-only 15-minute Shop credential.
- Add dedicated Pack-Shop GET/POST helpers and a Shop-only wallet connection entry point that does not start generic player login.
- Add a built-in Shop-purpose Pera/Lute chooser and commit-bound scoped ASA opt-in prepare/submit APIs.
- Add an identity-bound cancellation hook for game-owned transaction signing deadlines.

### Fixed
- Reuse an already primed Lute approval window instead of replacing it during scoped session acquisition.
- Track when a game-owned payment/opt-in sign consumes the NFTURBO Lute window, and expose identity-bound consume/cancel helpers so the next player click really primes a fresh window.
- Release the exact pending Pera/Lute sign on timeout, cancellation, or coroutine disposal; attempt-tagged single/group callbacks cannot complete a later retry.

### Security
- Attach the scoped credential only to the exact `/v1/pack-shop` route family; reject absolute, traversal, encoded-path, and non-Shop targets.
- Keep the Shop credential out of generic session storage, refresh, request builders, public getters, and signing-intent caches.
- Sign the exact challenge bytes returned by `/v1/auth/wallet/nfturbo-pack-shop/challenge`; never use the generic transaction builder in this flow.
- Refuse redirects for scoped challenge, verification, and bearer-token requests.

## [2.0.1] - 2026-08-15

### Added
- Add first-class Lute browser and extension connection, session, single-transaction signing, and atomic-group signing for WebGL.
- Add a popup-safe approval-window priming API for flows that prepare exact transaction bytes asynchronously after a player click.

### Fixed
- Keep temporary Magic email outages, disabled-provider states, and website-binding failures actionable in player UI while preventing unknown server diagnostics from being rendered
- Classify direct Magic/browser login and signing failures into safe cancellation, timeout, network, and availability messages without logging Magic email/address identifiers or raw provider exceptions

### Security
- Keep Lute isolated from WalletConnect session state so a stale Pera or Defly session cannot sign for a selected Lute identity.
- Fail closed on rejected, incomplete, timed-out, or malformed Lute signing responses; no partial transaction group is returned.

## [2.0.0] - 2026-07-21

### Security
- Bind every request, player login, session check, refresh, logout, and managed-wallet signature to the configured public game ID
- Require exact short-lived signing intents for server-managed wallet transactions and bind them to the builder's returned bytes
- Coalesce concurrent refresh-token exchanges so rotation cannot be mistaken for token replay
- Validate the API base URL as an exact HTTPS origin (with localhost HTTP allowed for development) and reject off-origin API paths
- Preserve structured error codes, request IDs, and retry timing without logging credentials or provider response bodies

### Changed
- Use `https://blockmaker.polaris.city` as the shared API default while keeping `serverUrl` configurable
- Require a public `gameId`; server keys are not accepted as game IDs
- Remove the retired flow-runner API and flow DTOs
- Add integration and security guidance for multi-game Blockmaker accounts

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
