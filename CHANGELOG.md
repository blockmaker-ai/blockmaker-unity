# Changelog

## [2.0.1] - 2026-08-15

### Fixed
- Keep temporary Magic email outages, disabled-provider states, and website-binding failures actionable in player UI while preventing unknown server diagnostics from being rendered

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
