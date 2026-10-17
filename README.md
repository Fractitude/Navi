# Navi

A computer for s&box games: a desktop with windows, a taskbar, and apps that can be installed from a shared Store. This is the `fractitude.navi` library. Any game can install it.

## Add Navi to a game

1. Install the `fractitude.navi` library with the Library Manager (View > Library Manager).
2. Create a GameObject with a `WorldPanel` (or `ScreenPanel`) and add a `NaviDesktop` component. It adds a `NaviComputer`.
3. For world panels, the player's camera needs a `WorldInput` component to click the screen.

## Parts

- `NaviComputer` (component) is the OS. It holds the installed apps (`InstalledApps`, editable in the inspector), the open windows and the permissions granted to apps. Gameplay code can call `Install`, `Uninstall`, `Launch` and `Close` with an app id.
- `NaviDesktop` (panel component) draws the desktop, windows, taskbar and permission dialogs. It adds a `NaviComputer` to its GameObject if there is none.
- `NaviAppResource` (`.napp`) is an app's manifest. Every app has one. `NaviAppRegistry` finds them all, so a new app needs no registration code.
- `Navi` (`Code/Navi/Client/Navi.cs`) is the client every app uses to talk to the computer. `NaviSystemCalls` answers it.
- `NaviPermissions` lists what apps can be allowed to do.
- `Packages` and `Calculator` are the built-in apps, in `Code/Navi/Apps/` with their manifests in `Assets/navi/apps/`. Open Packages with the NAVI button. Its Store tab lists apps players published to sbox.game.

## Windows

- The window in front has focus. Clicking a window focuses it and draws it above the others. A new window opens focused, and opening a minimized app restores and focuses it.
- Drag the title bar to move a window. Windows can't leave the screen.
- Drag a corner to resize. Double-click the title bar, or use its button, to maximize.
- Minimize hides the window and keeps the app running. Click the taskbar icon to bring it back. Clicking the icon of the window in front minimizes it.
- Right-click a taskbar icon for its menu: Open, Permissions..., Close.
- A shield in a window's title bar means the app has full system access (root). Nothing else shows it.

## Make an app

Every app is made the same way, whether it is built in, part of a game, or published to the Store: a plain Razor panel, plus a `.napp` manifest that describes it. The panel uses no Navi types; it talks to the computer through the `Navi` client.

1. Write the panel. Give it its own namespace so class names don't clash:

   ```razor
   @using Sandbox.UI
   @inherits Panel
   @namespace NotesApp

   <root>
   	<div class="button" @onclick=@(() => Navi.Close( this ))>Done</div>
   </root>
   ```

2. Add `Notes.razor.scss`. Give the root a size, because a new window fits its content. When the window is resized or maximized the app is stretched to fill it, so lay it out with flex rather than fixed sizes if it can resize:

   ```scss
   Notes
   {
   	width: 240px;
   	height: 160px;
   	padding: 6px;
   }
   ```

3. In `Assets/`, create a Navi App asset (`.napp`). Set App Id, Title and Panel Type (the panel's full class name, e.g. `NotesApp.Notes`), and fill in the rest of the manifest below.
4. Save. The app is now in Packages. Click Install, then click its taskbar icon.

The desktop panel is 640 x 360 logical pixels and the area above the taskbar is 312 high, so keep apps small.

### The manifest (`.napp`)

| Field | Meaning |
|---|---|
| `AppId` | Stable unique id. It is saved in `InstalledApps`, so do not change it after you use it. |
| `Title` | Name on the taskbar tooltip, window title and package list. |
| `Description` | One line in the package list. |
| `Icon` | Material icon name, e.g. `calculate`. |
| `Color` | Icon background, as a CSS color. |
| `PanelType` | Full name of the app's panel class. |
| `CanMinimize` | Has a minimize button. Default `true`. |
| `CanMaximize` | Has a maximize button and maximizes on title bar double-click. Default `true`. |
| `CanResize` | Can be resized by its corners. Default `true`. |
| `CapturesInput` | Takes over the player's controls while its window is in front. See below. Default `false`. |
| `Permissions` | The permissions the app may have. It is never granted others. See below. |
| `IsSystem` | A system app: it asks for full system access (root), which allows everything. See "System apps" below. |

## Talking to the computer

Apps call the static `Navi` class with their own panel. Calls are async: they travel up the panel tree as a `navi.call` event and are answered a frame or so later.

| Call | Does | Needs |
|---|---|---|
| `Navi.Close( this )`, `Minimize`, `ToggleMaximize` | The app's own window | |
| `Navi.HasInput( this )` | True while the app has the player's controls (not async) | |
| `Navi.GetApps( this )` | Every app, with installed and system flags | |
| `Navi.Launch( this, id )` | Open an app | |
| `Navi.Install( this, id )`, `Uninstall` | Install or uninstall an app | `navi.apps.install` |
| `Navi.CloseApp( this, id )` | Close another app | `navi.apps.manage` |
| `Navi.SearchStore( this )` | Store packages, with an official flag | |
| `Navi.GetStoreStatus( this )` | Installed and downloading package idents | |
| `Navi.InstallPackage( this, ident )` | Download and install a Store app | `navi.store.install` |
| `Navi.GetVersion( this )` | Goes up on every change. Poll it to know when to refresh | |
| `Navi.GetPermissions( this )`, `RequestPermissions`, `HasRoot` | See below | |

## Permissions

Every app can open apps, list them and control its own window. Anything more needs a permission (`Navi.Permission.*`, `NaviPermissions`), and the player decides which an app gets, like on Android:

- **The manifest declares them.** An app can only ever have the permissions its `.napp` lists.
- **First launch asks first.** The first time the player opens an app that declares permissions, a dialog lists them, all ticked. The player unticks what they don't want and clicks Open; the app then runs with the rest. Cancel doesn't open it.
- **Apps can ask while running.** `await Navi.RequestPermissions( this, Navi.Permission.InstallApps )` shows the same dialog for the ones it lacks and returns `(Granted, Denied)`.
- **Missing permissions fail.** A call without its permission throws `UnauthorizedAccessException`. `Navi.MissingPermission( e )` says which one. `Navi.WithPermission( this, () => Navi.Install( this, id ) )` makes a call, asks for a missing permission, and tries once more.
- **Grants are remembered** per app (`NaviComputer.GrantedPermissions`), across launches. Uninstalling forgets them.
- **The player can change them** from the taskbar menu: Permissions... (or Open with permissions... when the app is closed) shows every declared permission, ticked as granted. Unticking revokes.
- **System apps** have root, which allows everything, and skip these dialogs. See below.

The app's job is to cope with what it was denied: hide or disable those features, or explain why they don't work.

Permission and root prompts are shown by Navi's secure layer (`NaviSecurePrompt`), like Windows' secure desktop. It is a panel of its own, floating just in front of the screen, so it draws above every app and gets clicks first. While a prompt is open it covers the screen, so no app can be clicked. Apps can't reach it through their panel's parents. Its controls only react to a real left mouse press, made at least `ArmTime` (0.75 s) after the prompt appeared and released on the same control. An app raising fake click events gets nothing.

### System apps (root)

An app with `IsSystem` in its manifest asks for full system access: root. Root has every permission, declared or not, and its window shows a shield.

- **Pre-packaged system apps** are part of the OS. They have root without asking, are always installed and can't be uninstalled. Packages is one.
- **Pre-packaged means a trusted folder.** Only a manifest in one of `NaviAppRegistry.SystemAppFolders` counts: the library's own `navi/apps/`, plus the folders a game lists in `NaviComputer.SystemAppFolders` (default `navi/system/`, so a game's system apps go in `Assets/navi/system/`). A manifest downloaded through the Store never counts, even in one of those folders. Anything else is a normal app, so a system app from an unknown source has to ask for root like a Store one.
- **Ids can't be taken over.** If two manifests share an app id, a core app wins, then a pre-packaged one, so a Store app can never replace an app that ships with the game.
- **Store system apps** ask when they are installed. After the download, before anything is installed, the player sees "Install ... with full system access?". Cancel, and the app isn't installed. The Store can't tell before the download, because the manifest is inside the package.
- If a Store app becomes a system app in an update, it asks once before its next launch.
- Accepted apps are kept in `NaviComputer.RootApps`. Uninstalling forgets them.
- `Navi.HasRoot( this )` tells an app whether it has root.

## Apps that take over the controls

An app with `CapturesInput = true` gets the player's controls while its window is in front. The Pac-Man Store app (`fractitude.napp_pacman`) uses it.

- The camera flies to the computer until the screen fills the view (`NaviInputCapture.FlyTime`, `Fill`).
- The local player's `PlayerController` stops moving, looking and moving the camera. The app reads keys itself, e.g. `Input.Down( "Forward" )` for W.
- Once the screen fills the view, the mouse cursor is shown, and `WorldInput` aims with it, so the player can point and click anywhere on the computer (`FreeCursor`). Game code that hides the cursor every frame, like a crosshair, should skip that while `NaviInputCapture.Current` isn't null.
- An Escape press is swallowed so the game menu doesn't open. Pressing Escape 5 times, each within a second of the last, force-quits the app, with a bar on screen filling up with each press (`QuitPresses`, `QuitWindow`). It can't be a hold: s&box tells games when Escape goes down, but never whether it's still held.
- Closing, minimizing or focusing another window gives control back, and the camera flies back.
- `Navi.HasInput( this )` is true while the app has the controls (its panel has the `navi-input` class). Pause when it isn't.
- Game code can check `NaviInputCapture.Current`, e.g. to hide a crosshair while it isn't null.

`NaviInputCapture` is added to the computer automatically by `NaviDesktop`. The camera move needs a `WorldPanel`; with a `ScreenPanel` only the controls are taken.

## Publish an app to the Store (players)

Players make apps in their own addon project and publish them to sbox.game. Other players install them from the Store tab in Packages, in any game that has the Navi library. In the Omnipresence repo, `Addons/navi_counter/` is an example: a counter with - and + buttons.

**Don't install the Navi library in an app addon.** When an addon asset is published with code, s&box bundles every library in the addon project, and a bundled Navi would replace the host game's Navi when the app is installed. That's why apps use no Navi types: copy `Code/Navi/Client/Navi.cs` into the addon's `Code/` and change only its namespace line. The `.napp` asset type comes from the target game, which already has Navi.

1. Create an addon project: in the s&box launcher, New Project > Addon.
2. In Project Settings, set Target Game to a game that has the Navi library, e.g. `fractitude.omnipresence`. Restart the editor.
3. Copy the `Navi` client in, if the app calls the computer. Write the app as in "Make an app".
4. Press Play to test. Code changes hot-reload. Friends who join your editor session get the changes too.
5. Publish the `.napp` from the asset browser. The addon's code is published with it. Prefix the package ident with `napp_`, e.g. `fractitude.napp_counter`.

How the Store works:

- The Store lists packages of type `napp`. These are shared by every game with the library.
- Install downloads the package with `Cloud.Load<NaviAppResource>( ident, withCode: true )`, which loads its code. The manifest names the panel class, and the app is then found like any other app. A system app asks for root before it is installed; other apps have their declared permissions reviewed on first launch.
- Installed Store packages are kept in `NaviComputer.InstalledPackages` and downloaded again when the game starts.
- Addon code must pass the s&box API whitelist, like all game code.
- Uninstalling a Store app keeps its code loaded until the game restarts.
- Each game has its own copy of the library's source. Keep `NaviAppResource`, the `navi.call` methods (`NaviSystemCalls`) and the permission ids stable, or published apps can break. The older `navi.close`, `navi.minimize` and `navi.maximize` events still work.
