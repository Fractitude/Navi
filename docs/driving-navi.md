# Driving Navi from the command line

How to check a Navi change without clicking through it by hand: compile, enter play mode, open apps, press their
buttons and keys, read what they would draw, and take screenshots. It all goes through `sbox-cli`, the engine's bridge
to a running s&box editor. Everything here is what was used to build and test the file system and the Files app.

## Setup

- **The editor must be open** on the game project (e.g. Omnipresence), with Project Settings > AI > Enable MCP
  Communication on. Navi has no editor of its own: its code is compiled as part of the game that has it.
- **`sbox-cli`** is in the engine install: `<engine>/bin/managed/sbox-cli.exe`, where `<engine>` is the
  `FACEPUNCH_ENGINE` user variable or `C:\Program Files (x86)\Steam\steamapps\common\sbox`. Run it **from the game
  project's folder**: it picks the editor that has that project open. Run from `Libraries/navi/` it looks for an
  editor with `navi.sbproj` open and fails.
- `sbox-cli --skill sbox-cli` prints the engine's own guide to it.

```bash
C="/c/Program Files (x86)/Steam/steamapps/common/sbox/bin/managed/sbox-cli"
cd "/c/Users/you/Projects/S&box/omnipresence"
"$C" --call editor_status
```

Each `--call <tool> [json]` is one connection and one tool. Find tools with `--call search_tools '{"query":"ui"}'`;
it returns each tool's input schema. Pass long input as a file, not inline JSON: Windows PowerShell strips quotes,
and inline C# needs every quote escaped.

## Compile

Saving a `.cs`, `.razor` or `.scss` file makes the editor compile and hot-reload. Wait for it, then read the errors:

```bash
for i in $(seq 1 40); do s=$("$C" --call editor_status); echo "$s" | grep -q '"IsCompiling": false' && break; sleep 1; done
echo "$s" | grep LastCompile
"$C" --call compile_status    # every compiler's diagnostics: look at the "fractitude.navi" one
```

Give it a few seconds after saving before you poll, or you read the previous compile. A `.napp` asset is compiled
with `"$C" --call asset_compile '{"path":"navi/apps/files.napp"}'`.

## Play mode

```bash
"$C" --call play_start
"$C" --call play_stop
```

**Restart play mode after a code change.** A hot reload doesn't give a panel that already exists the new members (a
view model reads as null), so an app window from before the change proves nothing. Stop, start, and open the app again.

## Run C# in the game: `run_csharp`

`run_csharp` compiles a method body against the game's code and runs it on the main thread, in the running scene.
`await` works, and what it returns is printed. Write the script to a file and pass its absolute path:

```bash
"$C" --call run_csharp '{"path":"C:/path/to/script.cs"}'
```

### Sit down and open an app

```csharp
var computer = Game.ActiveScene.GetAllComponents<NaviComputer>().First();
computer.UserSit();                                   // takes the seat offline, like the first click on the screen
computer.Launch( NaviAppRegistry.BuiltInFolder + "files.napp" );
await Task.Delay( 2500 );                             // the camera flies in and the window opens
return computer.IsSeated;
```

Seating the player matters: the camera flies to the screen (needed for screenshots), and the app in front gets the
player's keys (`InputCapture`), which shortcuts need.

### Reach an app's view model

The app's panel is in the scene's panels. Its `ViewModel` is private, so read it by reflection:

```csharp
var panel = Game.ActiveScene.GetAllComponents<PanelComponent>()
	.SelectMany( x => x.Panel.Descendants )
	.OfType<NaviApps.Files>()
	.First();

var vm = (NaviApps.FilesViewModel)panel.GetType()
	.GetProperty( "ViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )
	.GetValue( panel );
```

### Press its buttons

Every control is an action (`Navi.Act`), so a script does what a click does by calling the view model's `Act` with the
same name and arguments the panel sends. The names are constants on the view model (`FilesViewModel.TrashAction`...):

```csharp
vm.Act( "open-path", ["/Documents"] );
vm.Act( "select", ["/Documents/a.txt", ""] );        // "toggle" is a Ctrl+click, "range" a Shift+click
vm.Act( "open-menu", ["", "300", "200"] );            // right-click on the empty list, at x 300, y 200
vm.Act( "choose-menu", ["new-file"] );
await Task.Delay( 500 );                              // calls are answered a frame or so later
```

Then read what the panel would draw from `vm.UiState`: it is the whole truth of the panel, so it makes a good test.

```csharp
return new { vm.UiState.Path, vm.UiState.Status, Rows = vm.UiState.Rows.Select( r => r.Name + (r.Selected ? " *" : "") ) };
```

### Press its keys

Keyboard shortcuts reach a shared app through the same RPC the player's client uses, which checks the window is the
one in front. So the player must be seated and the app focused:

```csharp
var id = NaviAppRegistry.BuiltInFolder + "files.napp";
computer.UserShortcut( id, "", "ctrl+a" );            // window id "" is the main window
computer.UserShortcut( id, "", "delete" );
```

This tests what the app does with the keys, not the reading of the keyboard (`NaviInputCapture.ForwardShortcuts`):
press real keys in the game view for that. The engine's key names are not the obvious ones (`DEL`, `UPARROW`, `PGDN`,
`CTRL`); `Sandbox.Engine.KeyTranslation.CodeToString` turns a `NativeEngine.ButtonCode` into one, by reflection.

### Call the computer as an app

To test permissions, make a `NaviProcess` for an app without root and run calls as it. Calculator has no permissions:

```csharp
var app = NaviAppRegistry.Find( NaviAppRegistry.BuiltInFolder + "calculator.napp" );
var process = new NaviProcess { App = app, IsRoot = false };
object Call( string m, params object[] a ) => NaviFileCalls.Run( computer, process, m, a );

try { Call( "files.list", "/" ); return "BAD: no error"; }
catch ( System.Exception e ) { return e.Message; }     // Missing permission: navi.files.access
```

## Screenshots

```bash
"$C" --call camera_screenshot '{}'                    # prints the path of a PNG of the main camera
```

Seat the player first, so the camera is in front of the screen. `ui_screenshot` doesn't work on a computer: it crops
to a panel's screen rectangle, and a world panel has none ("that rect is off screen").

`ui_panel_dump` shows the panel tree with sizes and computed styles, which finds layout bugs faster than a picture.
Rects are in the panel's render pixels, not CSS pixels (the Files window, 460px wide in its stylesheet, measured 920):

```bash
"$C" --call ui_panel_dump '{"panel":"Files .place","depth":1}'
"$C" --call ui_panel_dump '{"panel":"Files","depth":0,"styles":true,"styleFilter":"width"}'
```

## Console commands

Navi's own commands run in the editor's console, or from a script with `ConsoleSystem.Run( "navi_fs_ls", "/" )`.
They print to the console; read it with `"$C" --call read_console`.

| Command | Does |
|---|---|
| `navi_fs_ls [path]`, `navi_fs_cat path` | List a folder, print a text file |
| `navi_fs_write path text`, `navi_fs_mkdir path`, `navi_fs_rm path`, `navi_fs_mv from to` | Change files |
| `navi_fs_ln target path`, `navi_fs_alias target path` | Make a symlink, an alias |
| `navi_fs_drives`, `navi_fs_usb [label]`, `navi_fs_eject path` | List drives, put in a new 8 MB one, take one out |
| `navi_plugins`, `navi_plugin_install ident`, `navi_plugin_remove ident` | Plugins |

The `navi_fs_*` commands work on the host only. Each takes a computer's `SaveKey` as its last argument; without it they
use the computer the player sits at, else the first one.

## Leave things as you found them

Play mode saves like a real game: computers and drives are written to the game's local data
(`navi/saves/{slot}/...`). So a test that makes files should make its own folder and delete it at the end:

```csharp
var files = computer.Files;
if ( files.TryResolve( "/Documents/mytest", false ) is { } old ) old.Drive.Delete( old.Node );
files.CreateFolders( "/Documents/mytest" );
// ... the test ...
if ( files.TryResolve( "/Documents/mytest", false ) is { } done ) done.Drive.Delete( done.Node );
```

Tests that build their own `NaviFileSystem` (`new NaviFileSystem( new NaviDrive( "test", "Test", 4096 ) )`) touch no
save at all; use that for the file system's rules, and the real computer only for what needs it (apps, permissions).
`navi_fs_usb` makes a new drive each time, and each is saved under its own id when play stops.
