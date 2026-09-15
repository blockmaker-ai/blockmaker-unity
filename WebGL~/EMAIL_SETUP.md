# Optional email and shared identity

Use **TxnLab/MetaMask Embedded Wallets** (`txnlab_web3auth`) with
`pera_lute_txnlab_web3auth`. This produces a full economic Algorand MainNet wallet.
It is different from the older `web3auth_avm_email` authentication-only broker;
that provider's restrictions remain intact. Google is optional and is not needed
for passwordless email.

## Decide identity before provisioning

Shared Blockmaker usernames, primary-name choices, NFT pictures and default
avatars follow the owner wallet. Keep the same provider project/client ID,
Sapphire Mainnet network, passwordless connection/grouping and TxnLab Algorand
key derivation pinned across participating game origins. Use the same login
method. Separate projects or changed connections can produce different wallets
for the same email address. Do not silently replace an existing configuration.

Games under one Blockmaker owner can use an explicitly reviewed shared project.
Sharing it with another publisher requires a separate trust/account-linking
arrangement. Game sessions remain tenant-bound to each game and its approved
origin. Matching emails never links Pera, legacy email or existing distinct
wallets; linking requires proof of control and never grants ownership of the
other wallet's assets. Legacy returning players need a deliberate recovery or
migration flow before switching addresses.

## Set up a game

1. Keep the existing game ID and Pera/Lute policy. Record its current provider,
   approved-origin and managed-setup revisions. Existing player history is
   preserved; never delete it to enroll email or recreate the game.
2. Choose the existing pinned provider project. Allowlist each exact HTTPS game
   origin and keep its Email Passwordless connection unchanged. Inspect the
   account's current plan, terms, recovery/MFA and key-export options. These
   private dashboard settings cannot be inferred from public configuration.
3. Ask the authorized Blockmaker operator to perform **reviewed enrollment** for
   the current game, exact origin and shared identity group. Existing games with
   players require this controlled route. It verifies current revisions and
   public provider settings before changing provider policy atomically; it does
   not rewrite players, profiles, inventory or progress. This distribution does
   not contain operator credentials or provision projects automatically.
4. Check `/v1/integrations/managed-email-setup?gameId=YOUR_GAME_ID` on the API.
   Confirm the configured state, exact origin, public client ID, network and
   shared-identity fingerprint. Give that public client ID to `Initialize`.
   Keep it blank for Pera/Lute only. A fresh page load is needed after changing
   provider configuration.
5. Have a player complete email verification, returning login and supported
   recovery, and confirm the exact same wallet each time. Use two existing game
   contexts in the same reviewed group to compare the address/shared profile;
   change a profile in one and refresh the other. Do not create a production
   game purely as a test fixture.

The Unity host checks the reviewed shared fingerprint before enabling email.
Configuration errors leave the explicit Pera path available. Neither a configured
registry nor a successful login proves Shop, Garage or Marketplace operations.
Games already bound to the old broker's permanent authentication-only protocol
floor require their own supported migration; enrollment never downgrades it.

## Provider UI and recovery

The provider owns email, verification and recovery forms. Keep them visible and
interactive in document-root fullscreen as described in the [integration guide](README.md).
Handle popup close/cancellation normally, and let the player return to the game.
Do not create another wallet after an authentication failure. No database restore
or Blockmaker password reset can recreate a missing wallet key.

Provider plans, recovery options and terms can change. Review the actual account
and the current primary documentation before launch:

- [Email Passwordless](https://docs.metamask.io/embedded-wallets/authentication/basic-logins/email-passwordless/)
- [Grouped connections](https://docs.metamask.io/embedded-wallets/authentication/group-connections/)
- [Origin allowlist](https://docs.metamask.io/embedded-wallets/dashboard/allowlist/)
- [MFA and recovery](https://docs.metamask.io/embedded-wallets/sdk/js/advanced/mfa/)
- [Current service terms](https://metamask.io/terms-of-use)

Retain the complete bundled notices. The runtime includes dependencies whose
licences/usage conditions differ from Blockmaker's MIT licence.
