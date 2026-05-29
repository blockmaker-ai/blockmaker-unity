# Changelog

## [1.0.0] - 2026-05-29

### Added
- Wallet connection: Pera (WalletConnect v1), Defly (WalletConnect v2/Reown), MetaMask (EVM xChain)
- Email login via Magic SDK (WebGL) and server-managed wallets
- Guest identity with device-persistent ID
- Transaction signing across all identity types
- Atomic group transaction support
- Session persistence via encrypted SecurePrefs (AES-256-CBC + HMAC)
- Proactive JWT token refresh
- Profile management (username claim/change, NFT profile pictures, default avatars)
- Token balance tracking (TokenBalanceTracker)
- NFT metadata parsing (ARC-69) with configurable IPFS gateway
- QR code generation for wallet pairing
- Full Algorand address checksum validation
- Configurable logging (BlockmakerLog) with log levels and event hooks
- Async/await API overloads (BlockmakerAsyncExtensions)
- Domain reload support for Unity Enter Play Mode
- UI controllers for auth flow, wallet bar, profile, and OTP
