# Upgrading to version 2

Version 2 is now the repository's root Package Manager SDK. It replaces the old
`BlockmakerAuth`, Reown/Magic/server-OTP and native-wallet implementation with the
current WebGL Pera/Lute and optional TxnLab/MetaMask email package. This is a major
API change. Previous releases remain in Git tags for reference; they are not the
current installation route.

## From the previous v1 UPM package

1. Commit or back up your project. Preserve profile/player history and exact
   pending-operation recovery records.
2. Migrate old `BlockmakerAuth`/`BlockmakerConfig`/identity and profile consumers to
   the client, wallet facade and presentation shown in the [integration guide](integration.md).
   Remove obsolete scene objects after wiring their replacements. Existing API
   callers will need changes; the package does not rewrite gameplay code.
3. Follow the [current installation instructions](../README.md) in
   Package Manager. Keep one wallet manager. Old Reown/Nethereum dependencies can
   be removed through Package Manager only if no other project code uses them.
4. Confirm the same owner wallet/profile, cancellation and pending-operation
   recovery before resuming live game actions.

## From the September Assets/ZIP package

The version 2 package manages the nine wallet runtime files and their WebGL
build inclusion. The local 2.0.1 candidate changes only the presentation file;
the other eight runtime files retain their 2.0.0 contents. Close Unity and back up the complete existing SDK
files and their `.meta` files outside Assets before removing their old copies
from compilation. Preserve your own game UI, configuration and recovery data.

Remove only the previous vendor SDK files and its lock/installation receipt,
including its `.jslib` and five `Assets/StreamingAssets/Blockmaker` files. Do not
delete the entire Blockmaker folder if it contains your own work. Install the
UPM package, reopen Unity and check scene references. The canonical runtime
script/plugin GUIDs are retained from the existing integration to support this
move. Assembly placement changes, so custom assembly definitions may need an
explicit reference to the `Blockmaker` assembly.

The drop-in installer refuses to overwrite an SDK still in Assets. The build
hook also refuses old browser-file collisions. It never automatically deletes
files, migrates email wallets, merges profiles or clears recovery state.

## Returning email players

Changing email provider/project/login connection can change the wallet address.
Do not assume the same email restores a legacy address. Keep existing provider
identity pinned and use a reviewed proof-of-control recovery/linking arrangement
where needed. No account migration is performed by updating this package.
