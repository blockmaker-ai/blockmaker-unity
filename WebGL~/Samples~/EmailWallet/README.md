# Unity WebGL email sample

Install the complete current Blockmaker Unity package. Copy the sample C# file
from this folder into Assets in your game. Create a scene with a
UIDocument and PanelSettings, then attach `BlockmakerEmailSample` beside it.
Assign that UIDocument, your own public Game ID, API origin, branding/font and
the reviewed public provider client ID. Leave the client ID blank for Pera/Lute.
The sample uses an ordinary GameObject receiver; keep its name unique and run
one sample instance in the scene. No sample font is supplied.
Use one receiver/manager and run as Unity WebGL on the exact approved origin.

Repeat with a second existing game context and its own Game ID. Both origins
must belong to the same reviewed identity project. Same email login method →
same wallet → same canonical profile. The sample reads shared identity via the
compatible game-profile projection. No funds move and no keys/tokens are logged.

The sample deliberately does not invent economic API requests. For those use
the game's validated prepare → Unity review → explicit sign → exact one-time
submit/recovery flow described in [the setup guide](../../README.md).

## Fullscreen host

Fullscreen the **document root** so provider-owned email, verification and
recovery controls under `body` can receive input:

```js
async function enterGameFullscreen() {
  const root = document.documentElement
  if (root.requestFullscreen) await root.requestFullscreen()
  else if (root.webkitRequestFullscreen) root.webkitRequestFullscreen()
  else throw new Error('Fullscreen is unavailable in this browser.')
}
fullscreenButton.addEventListener('click', () => {
  enterGameFullscreen().catch(error => { statusLabel.textContent = error.message })
})
```

Keep the canvas responsive to the viewport. Replace every host fullscreen entry
point; a later `unityInstance.SetFullscreen(1)` would return to canvas-only
fullscreen and prevent input in the provider's body-level forms. Keep provider
iframes in their existing DOM position and let the package present them. Do not
copy emails, OTPs or recovery material into Unity. Test actual clicks and text
input in the email form, then close/retry and confirm Pera still stays fullscreen.

Native Unity exports are outside this package's scope. Returning login and
provider-supported recovery still need an attended provider check.
