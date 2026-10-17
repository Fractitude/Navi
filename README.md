# Navi

A computer for s&box games: a desktop with windows, a taskbar, and apps that can be installed from a shared Store. This is the `fractitude.navi` library. Any game can install it.

## Add Navi to a game

1. Install the `fractitude.navi` library with the Library Manager (View > Library Manager).
2. Create a GameObject with a `WorldPanel` (or `ScreenPanel`) and add a `NaviDesktop` component. It adds a `NaviComputer`.
3. For world panels, the player's camera needs a `WorldInput` component to click the screen.

## Parts

- `NaviComputer` (component) is the OS. It holds the installed apps (`InstalledApps`, editable in the inspector) and the open windows. Call `Install`, `Uninstall`, `Launch` and `Close` with an app id.
- `NaviDesktop` (panel component) draws the desktop, windows and taskbar. It adds a `NaviComputer` to its GameObject if there is none.
- `NaviAppRegistry` finds every app, so a new app needs no registration code. An app is either a `NaviApp` class with a `[NaviApp]` attribute (built-in and game apps), or any panel described by a `NaviAppResource` (Store apps).
- `Packages` is the system app for installing and uninstalling. Open it with the NAVI button. Its Store tab lists apps players published to sbox.game.
- `NaviAppResource` (`.napp`) describes an app and publishes it from an addon project. `NaviStore` finds and downloads published apps.

## Windows

- The window in front has focus. Clicking a window focuses it, a new window opens focused, and opening a minimized app restores and focuses it.
- Drag the title bar to move a window. Windows can't leave the screen.
- Drag a corner to resize. Double-click the title bar, or use its button, to maximize.
- Minimize hides the window and keeps the app running. Click the taskbar icon to bring it back. Clicking the icon of the window in front minimizes it.

## Make an app in your game

Apps can live in any game that has the library. Calculator and Packages are in `Libraries/navi/Code/Navi/Apps/`.

1. Create a folder in your game's `Code/`, e.g. `Code/Apps/Notes/`.
2. Add `Notes.razor`:

   ```razor
   @using Sandbox.UI
   @inherits NaviApp
   @namespace Sandbox
   @attribute [NaviApp( "notes", "Notes", Description = "Write things down.", Icon = "edit_note", Color = "#c0d890" )]

   <root>
   	<div>Hello from @Info.Name</div>
   </root>
   ```

3. Add `Notes.razor.scss`. Give the root a size, because a new window fits its content. When the window is resized or maximized the app is stretched to fill it, so lay it out with flex rather than fixed sizes if it can resize:

   ```scss
   Notes
   {
   	width: 240px;
   	height: 160px;
   	padding: 6px;
   }
   ```

4. Save. The app is now in Packages. Click Install, then click its taskbar icon.

The desktop panel is 640 x 360 logical pixels and the area above the taskbar is 312 high, so keep apps small.

## Attribute fields

| Field | Meaning |
|---|---|
| `id` | Stable unique id. It is saved in `InstalledApps`, so do not change it after you use it. |
| `name` | Name on the taskbar tooltip, window title and package list. |
| `Description` | One line in the package list. |
| `Icon` | Material icon name, e.g. `calculate`. |
| `Color` | Icon background, as a CSS color. |
| `IsSystem` | Always installed. Can't be uninstalled. |
| `CanMinimize` | Has a minimize button. Default `true`. |
| `CanMaximize` | Has a maximize button and maximizes on title bar double-click. Default `true`. |
| `CanResize` | Can be resized by its corners. Default `true`. |
| `CapturesInput` | Takes over the player's controls while its window is in front. See below. Default `false`. |

For example, Calculator turns off maximize and resize:

```razor
@attribute [NaviApp( "calculator", "Calculator", CanMaximize = false, CanResize = false )]
```

## Apps that take over the controls

An app with `CapturesInput = true` (on `[NaviApp]` or a `.napp`) gets the player's controls while its window is in front. The Pac-Man Store app (`fractitude.napp_pacman`) uses it.

- The camera flies to the computer until the screen fills the view (`NaviInputCapture.FlyTime`, `Fill`).
- The local player's `PlayerController` stops moving, looking and moving the camera, and `WorldInput` clicks stop. The app reads keys itself, e.g. `Input.Down( "Forward" )` for W.
- An Escape press is swallowed so the game menu doesn't open. Holding Escape for one second force-quits the app, with a progress bar on screen (`QuitHoldTime`). It can't be longer: s&box closes the whole game when Escape is held for about 1.7 seconds, and game code can't stop that.
- Closing, minimizing or focusing another window gives control back, and the camera flies back.
- While an app has the controls, its panel has the `navi-input` class, so a Store app that can't use Navi types can check `HasClass( "navi-input" )`, e.g. to pause when it doesn't.
- Game code can check `NaviInputCapture.Current`, e.g. to hide a crosshair while it isn't null.

`NaviInputCapture` is added to the computer automatically by `NaviDesktop`. The camera move needs a `WorldPanel`; with a `ScreenPanel` only the controls are taken.

## Inside a `NaviApp`

- `Computer` is the `NaviComputer` the app runs on. Use it to install, launch or close other apps.
- `Info` is the app's registration (id, name, icon...).
- `Close()` closes the app's window.
- App state lives in the panel. Minimizing keeps it. Closing the window deletes the panel, so the state resets on the next launch.
- Install state is local. It is not synced over the network yet.

## Make an app as an addon (players)

Players make apps in their own addon project and publish them to sbox.game. Other players install them from the Store tab in Packages, in any game that has the Navi library. In the Omnipresence repo, `Addons/navi_counter/` is an example: a counter with - and + buttons.

**Don't install the Navi library in an app addon, and don't use Navi types in its code.** When an addon asset is published with code, s&box bundles every library in the addon project. A bundled Navi would replace the host game's Navi when the app is installed. Instead, the app is a plain `Panel` and a `.napp` asset describes it. The `.napp` asset type comes from the target game, which already has Navi.

1. Create an addon project: in the s&box launcher, New Project > Addon.
2. In Project Settings, set Target Game to a game that has the Navi library, e.g. `fractitude.omnipresence`. Restart the editor.
3. Write the app as a plain Razor panel (`@inherits Panel`) in the addon's `Code/` folder. Give it its own namespace, e.g. `@namespace NaviCounter`.
4. In the addon's `Assets/`, create a Navi App asset (`.napp`). Set App Id, Title, Icon, Color and Panel Type (the panel's full class name, e.g. `NaviCounter.Counter`). Turn off the window features the app doesn't want.
5. Press Play to test. Code changes hot-reload. Friends who join your editor session get the changes too.
6. Publish the `.napp` from the asset browser. The addon's code is published with it. Prefix the package ident with `napp_`, e.g. `fractitude.napp_counter`.

An app controls its window by raising panel events, which bubble up to the window:

| Event | Effect |
|---|---|
| `CreateEvent( "navi.close" )` | Close the window |
| `CreateEvent( "navi.minimize" )` | Minimize the window |
| `CreateEvent( "navi.maximize" )` | Maximize or restore the window |

How the Store works:

- The Store lists packages of type `napp`. These are shared by every game with the library.
- Install downloads the package with `Cloud.Load<NaviAppResource>( ident, withCode: true )`, which loads its code. The resource names the panel class, and the app is then found like any other app.
- Installed Store packages are kept in `NaviComputer.InstalledPackages` and downloaded again when the game starts.
- Addon code must pass the s&box API whitelist, like all game code.
- Uninstalling a Store app keeps its code loaded until the game restarts.
- Each game has its own copy of the library's source. Keep `NaviAppResource` and the `navi.*` events stable, or published apps can break.
