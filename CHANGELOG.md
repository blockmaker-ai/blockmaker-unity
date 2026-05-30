# Changelog

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
