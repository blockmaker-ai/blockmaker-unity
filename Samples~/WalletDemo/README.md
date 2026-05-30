# Wallet Demo

A minimal sample showing the Blockmaker wallet system in action.

## How to use

1. Create an empty scene
2. Create an empty GameObject
3. Add the `WalletDemo` component to it
4. Hit Play

You'll see a "Connect Wallet" button. Click it to open the wallet selection modal. Connect with Pera, Defly, X-Chain (MetaMask etc.), or email.

## What this demo does

- Creates a BlockmakerConfig at runtime (no asset needed)
- Sets up BlockmakerAuth with all defaults
- Adds the auth prompt modal (wallet selection UI)
- Adds a connect button in the top-right corner
- Logs wallet connection events to the console

## For production

Use **Blockmaker > Setup Scene** instead of this script. It creates proper assets and wires everything through the Inspector.
