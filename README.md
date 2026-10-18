# Navi

A computer for s&box games: a desktop with windows, a taskbar, and apps that can be installed from a shared Store. This is the `fractitude.navi` library. Any game can install it.

## Add Navi to a game

1. Install the `fractitude.navi` library with the Library Manager (View > Library Manager).
2. Create a GameObject with a `WorldPanel` (or `ScreenPanel`) and add a `NaviDesktop` component. It adds a `NaviComputer`.
3. For world panels, the player's camera needs a `WorldInput` component to click the screen.

## Parts

- `NaviComputer` (component) is the OS. It holds the installed apps (`InstalledApps`, editable in the inspector), the running apps with their windows and tray icons, and the permissions granted to apps. Gameplay code can call `Install`, `Uninstall`, `Launch` and `Close` with an app id (see "App ids").
- `NaviDesktop` (panel component) draws the desktop, windows, taskbar and permission dialogs. It adds a `NaviComputer` to its GameObject if there is none.
- `NaviAppResource` (`.napp`) is an app's manifest. Every app has one. `NaviAppRegistry` finds them all, so a new app needs no registration code.
- `Navi` (`Code/Navi/Client/Navi.cs`) is the client every app uses to talk to the computer. `NaviSystemCalls` answers it.
- `NaviPermissions` lists what apps can be allowed to do.
- `Packages`, `Settings`, `Calculator`, `Turntable` (a model drawn in 3D with a `ScenePanel`) and `WebBrowser` (the engine's `WebPanel`, with a history of its own) are the built-in apps, in `Code/Navi/Apps/` with their manifests in `Assets/navi/apps/`. Open Packages with the NAVI button. Its Store tab lists apps players published to sbox.game. Packages is the reference for how an app is split: a `UiState` it draws, a view model that does the work, and a stateless panel (see "Keep the panel stateless").

## Windows

- The window in front has focus. Clicking a window focuses it and draws it above the others. A new window opens focused, and opening a minimized app restores and focuses it.
- Drag the title bar to move a window. Windows can't leave the screen.
- Drag a corner to resize. Double-click the title bar, or use its button, to maximize.
- Minimize hides the window and keeps the app running. Click the taskbar icon to bring it back. Clicking the icon of the window in front minimizes it.
- Right-click a taskbar icon for its menu: Open, Permissions..., Close.
- A shield in a window's title bar means the app has full system access (root). Nothing else shows it.
- An app can have more than one window, and a headless app has none at first, only an icon in the system tray. See "Headless apps and more windows".
- A title bar always starts with the app's name; a window's own title (if the manifest gives one) comes after it. The exception is a pre-packaged app, which is part of the OS: its window's own title stands alone (the Web Browser shows the page's title), and the app's name shows while there is none.
- Clicking the taskbar icon of an app with several windows minimizes or restores all of them. Closing an app's main window closes the app and its other windows.

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

3. In `Assets/`, create a Navi App asset (`.napp`). Set Title and Panel Type (the panel's full class name, e.g. `NotesApp.Notes`), and fill in the rest of the manifest below.
4. Save. The app is now in Packages. Click Install, then click its taskbar icon.

The desktop panel is 640 x 360 logical pixels and the area above the taskbar is 312 high, so keep apps small.

### Keep the panel stateless

An app is three parts, and the panel is only the third:

- **`<App>UiState.cs`** — a record of everything the panel draws, already filtered, already formatted, with a `Revision` that goes up on every change. No logic, no engine objects.
- **`<App>ViewModel.cs`** — the app: the calls to the computer, the polling, the async guards, the filters and the words they produce. It replaces `UiState` through one private `Update`, which is also what bumps the revision.
- **`<App>.razor`** — markup. Its `@code` is a lazily made `ViewModel`, a `UiState` shortcut and `BuildHash`; every control calls a `ViewModel` method, and the markup prints values it is given.

```razor
@code
{
	NotesViewModel viewModel;

	NotesViewModel ViewModel => viewModel ??= new( this );
	NotesUiState UiState => ViewModel.UiState;

	protected override int BuildHash() => ViewModel.Revision;
}
```

One place changes the state, the panel never decides, not every change is worth a redraw, and what the panel must do to its own tree stays in the panel.

The full contract — where each kind of rule goes, async and threads, the optional `Changed` event, and a Kotlin-to-C# translation — is in [`docs/app-architecture.md`](docs/app-architecture.md). `Packages` is the reference implementation: `Code/Navi/Apps/Packages/`. Store apps split the same way; a view model is plain C#, so it needs nothing from Navi.

### The manifest (`.napp`)

| Field | Meaning |
|---|---|
| `Title` | Name on the taskbar tooltip, window title and package list. |
| `Description` | One line in the package list. |
| `Icon` | Material icon name, e.g. `calculate`. |
| `Color` | Icon background, as a CSS color. |
| `PanelType` | Full name of the app's panel class. |
| `Headless` | The app starts with no window, in the system tray. `PanelType` is then its service: a panel that runs while the app does and is never drawn. See "Headless apps and more windows". Default `false`. |
| `Windows` | More windows the app can open, each with an `Id`, an optional `Title`, a `PanelType` and its own `CanMinimize` / `CanMaximize` / `CanResize` / `AspectRatio`. |
| `CanMinimize` | Has a minimize button. Default `true`. |
| `CanMaximize` | Has a maximize button and maximizes on title bar double-click. Default `true`. |
| `CanResize` | Can be resized by its corners. Default `true`. |
| `AspectRatio` | Shape the content (below the title bar) keeps while resized, as width and height, e.g. `4,3` or the app's own size in pixels. Zero for any shape. The desktop holds it during the drag, like macOS's content aspect ratio; the app scales what it draws to fill the window, and centers it when maximized. |
| `Shared` | In multiplayer every player sees the same app: the host runs it, the others draw it. The app must be written for it. See "Multiplayer". Default `false`. |
| `Permissions` | The permissions the app may have. It is never granted others. See below. |
| `IsSystem` | A system app: it asks for full system access (root), which allows everything. See "System apps" below. |

### App ids

The manifest doesn't choose its app's id. Navi derives it (`NaviAppRegistry.IdOf`):

- **A Store app's id is its package ident**, e.g. `fractitude.napp_pacman`. sbox.game keeps idents unique, and only the package's org can publish under one.
- **Any other app's id is its manifest's resource path**, e.g. `navi/apps/packages.napp` (`NaviAppRegistry.PackagesApp`). This includes an addon's app while you test it in the editor.

Ids are saved in `InstalledApps` and the other lists of granted rights, so don't move or rename a manifest once it is used. A path with spaces isn't an id, and its app isn't listed. Idents have no `/` and paths always do, so a Store app can never get the id of another app.

## Headless apps and more windows

An app is not the same thing as a window. A running app (`NaviProcess`) has any number of windows (`NaviWindow`) and may have an icon in the system tray (`NaviTray`).

**More windows.** List them in the manifest under `Windows`, each with a panel class from the app's own code, and open them from the app with `Navi.OpenWindow( this, "settings" )`. An app has at most one window of each id, so opening one that is open brings it to the front. The panels are separate instances: they share state through the app's own code (a static class or service), not through Navi. An app can only open windows its manifest lists, and it needs no permission to.

**Headless.** With `Headless` in the manifest the app's `PanelType` is a service. It is created when the app starts and kept in a hidden layer of the desktop where it keeps ticking, and no window is made for it. It sets its tray icon and menu, and opens windows when asked:

```razor
@using Sandbox.UI
@inherits Panel
@namespace SyncApp

<root></root>

@code
{
	public override void Tick()
	{
		base.Tick();

		if ( !started )
		{
			started = true;
			_ = Navi.SetTray( this, "cloud_sync", "Up to date",
				new Navi.TrayItem( "settings", "Settings...", "settings" ),
				new Navi.TrayItem( "sync", "Sync now", "sync" ) );
		}
	}

	bool started;

	protected override void OnEvent( PanelEvent e )
	{
		if ( Navi.IsTrayClick( e, out var item ) )
		{
			if ( item is "settings" or Navi.TrayPrimary )
				_ = Navi.OpenWindow( this, "settings" );
			else if ( item == "sync" )
				StartSync();

			e.StopPropagation();
			return;
		}

		base.OnEvent( e );
	}
}
```

- **The tray is drawn by the desktop**, at the right end of the taskbar. Right-click opens the menu: the app's items (up to 12), then **Quit**, which the app can't remove or rename. Quit closes the app with all its windows. The tooltip and the menu heading start with the app's name, so an app can't pass for another one. Texts are cut to size (`NaviTray`).
- **Left-click** gives the app `Navi.TrayPrimary`. Opening an app from the taskbar when it is already running without a window gives it the same click.
- **A headless app has a tray icon from the start** (its manifest icon), so the player can always see and quit it. A normal app may set one too.
- **Where a click goes.** A tray click reaches the app's main panel only (the service, for a headless app). In multiplayer a `Shared` app hears it on the host's copy; one that isn't shared hears it on the clicking player's own copy, like every other click in it.
- **Closing.** `Navi.Close( this )` from the service quits the app. Closing a secondary window only closes it.
- **Shared apps** publish per window: each window's panel calls `Navi.Publish` and draws `Navi.GetState` for itself, and `Navi.Act` reaches the host's copy of the same window. State, scrolling and actions are kept by window key (`NaviWindow.Key`: the app's id, then `#` and the window id for the others). App ids can't contain `#`.
- **Not a security boundary.** Window panel classes, like the main one, come from the app's code, and the host can name any panel class in a client's code (see "Multiplayer", last bullet).

## Talking to the computer

Apps call the static `Navi` class with their own panel. Calls are async: they travel up the panel tree as a `navi.call` event and are answered a frame or so later.

| Call | Does | Needs |
|---|---|---|
| `Navi.Close( this )`, `Minimize`, `ToggleMaximize` | The window the panel is in. Closing the main window closes the app | |
| `Navi.SetTitle( this, title )` | Change what the title bar says after the app's name (up to 32 characters; empty restores the `.napp` title) | |
| `Navi.OpenWindow( this, id )`, `CloseWindow` | Open or close one of the windows the manifest lists | |
| `Navi.SetTray( this, icon, tooltip, items )` | Set the app's tray icon and menu | |
| `Navi.IsTrayClick( e, out id )` | In `OnEvent`: the player clicked the tray icon (`Navi.TrayPrimary`) or a menu item | |
| `Navi.HasInput( this )` | True while the app has the player's controls (not async) | |
| `Navi.GetApps( this )` | Every app, with installed and system flags | |
| `Navi.GetStoreApps( this )` | Ids of the apps that came from the Store | |
| `Navi.Launch( this, id )` | Open an app | |
| `Navi.Install( this, id )`, `Uninstall` | Install or uninstall an app | `navi.apps.install` |
| `Navi.CloseApp( this, id )` | Close another app | `navi.apps.manage` |
| `Navi.SearchStore( this )` | Store packages, with an official flag | |
| `Navi.GetStoreStatus( this )` | Installed and downloading package idents | |
| `Navi.GetStoreBlacklist( this )` | Package idents the game blocks | |
| `Navi.InstallPackage( this, ident )` | Download and install a Store app | `navi.store.install` |
| `Navi.GetVersion( this )` | Goes up on every change. Poll it to know when to refresh | |
| `Navi.GetDisplayScale( this )` | The display scale (1 is 100%) and the scales the player can pick | |
| `Navi.SetDisplayScale( this, scale )` | Draw the desktop at one of those scales | `navi.settings.change` |
| `Navi.GetTheme( this )` | The theme, color scheme, blur level and transparency level | |
| `Navi.SetTheme( this, theme )`, `SetColorScheme`, `SetBlurLevel`, `SetTransparencyLevel` | Change the look. See Theming | `navi.settings.change` |
| `Navi.GetSnapping( this )` | Whether windows snap to the screen's edges and to each other's, and the distance in panel pixels | |
| `Navi.SetSnapToScreenEdges( this, on )`, `SetSnapToWindows`, `SetSnapDistance` | Change window snapping | `navi.settings.change` |
| `Navi.GetPermissions( this )`, `RequestPermissions`, `HasRoot` | See below | |

## Theming

The player picks the look of the whole computer in Settings, under Theming, and apps follow it. There are two system-wide settings and two Glass-only ones:

- **Theme**: `Paper` (flat, opaque, hard edges; the original look) or `Glass` (rounded, translucent surfaces that blur what is behind them, over a colorful wallpaper).
- **Color scheme**: `Light` or `Dark`, for every theme.
- **Blur** (Glass): None, Low, Medium, High or Max (levels 0 to 4).
- **Transparency** (Glass): None is solid, up to Max, which shows the most of what is behind (levels 0 to 4).

All four are saved with the computer and shown to every player at it. Apps keep using the engine's own components; there is nothing to call.

### Make an app follow the theme

The engine's SCSS has no CSS variables and no `.dark &`, so a style sheet is written once, as a mixin, and included once per look under the classes the desktop puts on its root panel (`theme-paper` or `theme-glass`, `scheme-light` or `scheme-dark`, `blur-0` to `blur-4`, `clear-0` to `clear-4`; see `NaviThemeClasses`). Colors are palette variables from `Code/Navi/Themes`, named for the Paper light color they replace (`$ink-t` for text, `$surface-bg` for a fill, `$line-bc` for a border). Look at `Apps/Settings/Settings.razor.scss` for the pattern:

```scss
@import "../../Themes/_paper-light.scss";

@mixin settings-style
{
	Settings
	{
		color: $ink-t;

		.sidebar { background-color: $surface2-bg; border-right: 1px solid $line-bc; }
	}
}

@include settings-style;

@import "../../Themes/_paper-dark.scss";
.theme-paper.scheme-dark { @include settings-style; }

@import "../../Themes/_glass-light.scss";
.theme-glass.scheme-light { @include settings-style; }

@import "../../Themes/_glass-dark.scss";
.theme-glass.scheme-dark { @include settings-style; }
```

A color that isn't a palette variable (a danger red, a warning yellow) is the same in every look. The palette files are generated: change a color in `tools/generate-theme.ps1`, run it, and commit what it writes. A Store app can't import the library's files, so it styles itself the same way with its own colors under those classes, or leaves its own look alone.

The desktop's own chrome (windows, taskbar, menus, the wallpaper) is in `NaviDesktop.razor.scss`, where Glass also gets its blur (`backdrop-filter-blur`) and its fill transparency from the `blur-N` and `clear-N` classes.
## Saving

Each `NaviComputer` saves what the player changed on it: installed apps and Store packages, granted permissions, accepted root, settings such as the display scale and the theme, and where each window was. Open windows and the apps running in the tray aren't saved: after a load, headless apps are not running until the player opens them. The save goes to the game's local data (`FileSystem.Data`), in `navi/saves/{slot}/{computer}.json`.

- **Windows open where they were.** Like a window manager, the computer remembers each window's position, size and whether it was maximized (`WindowPlacements`, one per window key) when it closes and when the computer saves, and opens it there next time. Apps don't do anything for it and can't set it. What the app's manifest no longer allows (resizing, maximizing) is dropped, and the desktop keeps the window inside the screen. Uninstalling an app forgets its windows. Only the host keeps placements; clients aren't sent them.
- **Automatic by default.** With `AutoSave` on, a computer loads its save when it starts, replacing what the scene set up. It saves a second after a change, and again when it is disabled.
- **One file per computer.** `SaveId` names the file. Left empty, it is the GameObject's id, which stays the same for an object saved in a scene. Set it on computers that are spawned at runtime.
- **Save slots.** A game with its own saves sets `NaviSaveData.Slot`, then calls `NaviSaveData.LoadAll( Scene )` when the player loads a save. Loading closes every window. `SaveAll`, and a computer's own `Save` and `Load`, are there for games that turn `AutoSave` off.
- **Not a security boundary.** The files are plain JSON, and any package's code in the game can write them. Loading still applies the computer's rules: root needs a system app, and a grant only counts for a permission the app declares.

## Multiplayer

The host runs every computer, and other clients draw what it sends. Set the computer's GameObject to **Network Mode: Object** in the scene. Scene objects default to Snapshot, which is sent to clients only once, as they join, so they never see the screen change. The host logs a warning when a computer isn't a network object. A computer made at runtime must be network-spawned by the game (`GameObject.NetworkSpawn()`).

- **One user at a time.** The first player to click a free computer takes the seat (`NaviComputer.UserId`) when they let go of the button. That click does nothing else: until a player sits, the screen ignores them, offline too. Only the user can click, drag windows, answer permission prompts and get pulled in to the screen. Everyone else sees an "In use by ..." badge, and their clicks are ignored. The seat frees itself when the user leaves the game, or after `SeatTimeout` seconds without them using or pointing at the screen (never while a prompt is open; it stays while they are pulled in to the screen). When the seat changes, open prompts are dismissed.
- **The desktop is the same everywhere.** Windows, positions, focus, installed apps, settings and prompts come from the host as a snapshot, in parts that are sent only when they change (`NetworkApps`, `NetworkState`, `NetworkWindows`, `NetworkSession`): moving a window sends only the windows. What the user does goes to the host as an RPC (`UserLaunch`, `UserClose`, `UserMoveWindow`...), which checks the caller is the user. On other clients every method that changes the computer (`Install`, `Launch`, `Close`...) does nothing: call them on the host. Saves are written and loaded on the host only.
- **Only nearby players get it.** The host sends a computer's state only to players within `NaviComputer.NetworkRange` world units (default 1500), and always to its user. A player who comes back in range gets the latest state. The computer turns off its GameObject's Always Transmit for this; set `NetworkRange` to 0 to send to everyone. Out of range, the engine turns the whole computer off for that player, so the host spawns a plane over the screen (`NaviScreenStandIn`, colored `OutOfRangeColor`, black by default) that they see instead.
- **Store apps follow the host.** A client downloads the Store apps the host has, so it can draw them.
- **Shared apps** (`Shared` in the manifest) look the same to everyone. The host's copy runs the app and publishes its state with `Navi.Publish`. Every other client's copy is a viewer (`Navi.IsViewer`): it draws `Navi.GetState<T>`, makes no calls, and runs no logic. Controls call `Navi.Act( this, "action", args )`, which reaches the host's copy as an action (`Navi.IsAction` in `OnEvent`) from whichever client the user is on. Apps that capture input read keys with `Navi.InputDown` / `Navi.InputPressed`, which give the user's keys on the host (`NaviInputCapture.ForwardedActions` lists the ones sent). Keep the state small: it is sent to everyone, up to `NaviComputer.MaxAppState` characters. See `docs/app-architecture.md`.
- **Other apps run locally.** Without `Shared`, each client runs its own copy, so players see different things in the window, which shows a "Client-side" mark. Such a copy on a client can work its own window, and read the computer, but changes nothing else.
- **What it trusts.** The host trusts the user's client that their clicks, and their answers to permission prompts, were real presses: the secure layer's real-mouse check runs on the user's machine. A modified client can fake both, for its own seat only. A client's copy never makes calls as an app; calls run only on the host's copy. A hostile host can make clients download any Store app, since they download what the host has installed. A client that lacks the manifests of the game's or libraries' apps (one that joins an editor session, say) takes them from the host, so a hostile host can also open any panel class in the client's code as an app; it only draws there, and can't change the client's computer.

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

- **Pre-packaged system apps** are part of the OS. They have root without asking, are always installed and can't be uninstalled. Packages and Settings are.
- **Pre-packaged means a trusted folder.** Only a manifest in one of `NaviAppRegistry.SystemAppFolders` counts: the library's own `navi/apps/`, plus the folders a game lists in `NaviComputer.SystemAppFolders` (default `navi/system/`, so a game's system apps go in `Assets/navi/system/`). A manifest downloaded through the Store never counts, even in one of those folders. Anything else is a normal app, so a system app from an unknown source has to ask for root like a Store one.
- **Ids can't be taken over.** A manifest can't choose its app id (see "App ids"), so a Store app can't pass for another app and inherit its root or permissions.
- **Store system apps** ask when they are installed. After the download, before anything is installed, the player sees "Install ... with full system access?". Cancel, and the app isn't installed. The Store can't tell before the download, because the manifest is inside the package.
- If a Store app becomes a system app in an update, it asks once before its next launch.
- Accepted apps are kept in `NaviComputer.RootApps`. Uninstalling forgets them.
- `Navi.HasRoot( this )` tells an app whether it has root.

## Sitting at the computer

Taking the seat (clicking the screen; see Multiplayer) pulls the player in, for every app. `NaviComputer.IsSeated` says whether the local player has it. Offline, the first click on the screen sits the player down.

- The camera flies to the computer until the screen fills the view (`NaviInputCapture.FlyTime`, `Fill`).
- The local player's `PlayerController` stops moving, looking and moving the camera. The app reads keys itself with `Navi.InputDown( this, "Forward" )` for W (or `Input.Down`, in an app that isn't shared). In multiplayer only the computer's user is pulled in.
- Once the screen fills the view, the mouse cursor is shown, and `WorldInput` aims with it, so the player can point and click anywhere on the computer (`FreeCursor`). Game code that hides the cursor every frame, like a crosshair, should skip that while `NaviInputCapture.Current` isn't null.
- An Escape press is swallowed so the game menu doesn't open, and stands the player up (`NaviComputer.UserLeave`): the camera flies back and the controls return.
- `Navi.HasInput( this )` is true while the app is in front and the player sits at the computer (its panel has the `navi-input` class). Pause when it isn't. Closing or minimizing the window doesn't stand the player up.
- Game code can check `NaviInputCapture.Current`, e.g. to hide a crosshair while it isn't null.

`NaviInputCapture` is added to the computer automatically by `NaviDesktop`. The camera move needs a `WorldPanel`; with a `ScreenPanel` only the controls are taken.

## Publish an app to the Store (players)

Players make apps in their own addon project and publish them to sbox.game. Other players install them from the Store tab in Packages, in any game that has the Navi library. In the Omnipresence repo, `Addons/navi_pacman/` is an example: a Pac-Man game.

**Don't install the Navi library in an app addon.** When an addon asset is published with code, s&box bundles every library in the addon project, and a bundled Navi would replace the host game's Navi when the app is installed. That's why apps use no Navi types: copy `Code/Navi/Client/Navi.cs` into the addon's `Code/` and change only its namespace line. The `.napp` asset type comes from the target game, which already has Navi.

1. Create an addon project: in the s&box launcher, New Project > Addon.
2. In Project Settings, set Target Game to a game that has the Navi library, e.g. `fractitude.omnipresence`. Restart the editor.
3. Copy the `Navi` client in, if the app calls the computer. Write the app as in "Make an app".
4. Press Play to test. Code changes hot-reload. Friends who join your editor session get the changes too.
5. Publish the `.napp` from the asset browser. The addon's code is published with it. Prefix the package ident with `napp_`, e.g. `fractitude.napp_pacman`.

How the Store works:

- The Store lists packages of type `napp`. These are shared by every game with the library.
- Install downloads the package with `Cloud.Load<NaviAppResource>( ident, withCode: true )`, which loads its code. The manifest names the panel class, and the app is then found like any other app. A system app asks for root before it is installed; other apps have their declared permissions reviewed on first launch.
- Installed Store packages are kept in `NaviComputer.InstalledPackages` and downloaded again when the game starts.
- A game can limit the Store, in its own code before its computers start (e.g. in a `GameObjectSystem`'s constructor), so the host and every client agree:
  - `NaviStore.AllowedOrgs`: the orgs whose apps are allowed. Empty allows every org. Official apps are always allowed and can't be turned off, so listing an official org (`fractitude`) allows only official apps. The Store asks sbox.game only for allowed orgs' apps (one search per org), and nothing else is downloaded: not on install, not from a save, not on a client whose host has it. An org is the first part of a package ident, which only its members can publish under.
  - `NaviStore.BlacklistApps`: package idents to block, official ones included. They still show in the Store, with a disabled Blacklisted button, and the host refuses to install or download them.
- Addon code must pass the s&box API whitelist, like all game code.
- Uninstalling a Store app keeps its code loaded until the game restarts.
- Each game has its own copy of the library's source. Keep `NaviAppResource`, the `navi.call` methods (`NaviSystemCalls`) and the permission ids stable, or published apps can break. The older `navi.close`, `navi.minimize` and `navi.maximize` events still work.
