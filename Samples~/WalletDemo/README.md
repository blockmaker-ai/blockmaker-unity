# Wallet demo

Open the imported **WalletDemo.unity** scene, select **Blockmaker Wallet Demo**
and set its public Game ID, optional public email client ID and app name in the
Inspector. Then choose **Blockmaker → Install Fullscreen Web Template**, add the
scene to a Web build profile and build for your game's approved origin.

Alternatively, **Blockmaker → Setup Wallet Demo** creates the same UI document,
panel and `BlockmakerWalletDemo` component in your current scene. Use one route;
do not create a second wallet manager alongside an existing integration.

The sample signs in, signs out and reads the shared profile. It never prepares,
signs or submits economic transactions. Fullscreen/browser authentication needs
a WebGL build; Editor Play mode explains this. No game ID, email project, player
wallet or licensed font is embedded in the sample.

For cross-game identity, use two existing approved game contexts in the same
reviewed provider group, each with its own Game ID and origin. Compare the exact
wallet and shared profile after using the same login method. Read the package's
email setup and validation guides before onboarding players.
