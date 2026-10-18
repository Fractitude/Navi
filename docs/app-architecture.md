# App architecture: view model, UI state, stateless panel

Every Navi app is the same three files. It's the pattern Packages is built on, and the one a
new app should be started from:

| File | Holds | Doesn't hold |
|---|---|---|
| `<App>UiState.cs` | Records of exactly what the panel draws: rows already filtered, buttons already decided, text already formatted, plus a `Revision` | Rules, calls, async, anything that can fail |
| `<App>ViewModel.cs` | All of it: the calls to the computer, polling, paging, filters and their wording, permission handling, the async guards | Any read of the panel's tree, any panel state |
| `<App>.razor` | The markup, one lazy `ViewModel`, one `UiState` shortcut, and `BuildHash()` | App state of any kind |

Why bother: an s&box panel with the app in its `@code` block can only be exercised by clicking it,
every field is another possible reason for a redraw to be missed, and the markup ends up mixing
what the list *shows* with what it *means*. Split this way, the rules run without a panel at all —
a console probe can select a filter, search the Store, install something and print the state that
came out — and the panel becomes a function of one value.

## The three parts

### `PackagesUiState` — what is drawn

A `sealed record` with `init` properties, replaced whole on every change:

```csharp
public sealed record PackagesUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	public bool ShowStore { get; init; }
	public IReadOnlyList<PackageRow> StoreRows { get; init; } = [];
	public int StoreTotal { get; init; }

	public int StoreShown => StoreRows.Count;
	public bool HasMore => !Refreshing && StoreShown < StoreTotal;
}
```

- **Rows are records too** (`PackageRow`, `AppRow`), as is anything the markup loops over or prints
  a pair of (`PackageFact( Icon, Key, Value )`, `FilterItem`). Positional records for fixed little
  shapes, `init` properties for the ones that will grow.
- **Enums instead of strings the markup compares.** `PackageAction.Installing` rather than
  `entry.Action == "installing"`, so the razor has no spelling to get wrong.
- **Computed flags live here** (`HasMore`, `AppListEmpty`) when the markup would otherwise do
  arithmetic. They're properties, not duplicated fields, so they can't go stale.
- **Formatted strings live here** (`Rating` reading "82% positive (14)", `Size` reading "2 KB"),
  because the panel prints values, it doesn't work them out.
- **No engine objects.** A `Package` never reaches the state; what the markup needs from one is
  copied into the row. The state is then pure data — the same thing could be serialized, logged or
  asserted in a test.
- **`Revision` is the only thing the panel hashes.** Anything drawn from the state must bump it.

### `PackagesViewModel` — what happens

A `sealed class`, not a panel:

```csharp
public sealed class PackagesViewModel
{
	readonly Panel app;                 // what every call to the computer is made with

	public PackagesViewModel( Panel app ) { ... }

	public PackagesUiState UiState { get; private set; }
	public int Revision => UiState.Revision;

	public void OpenStore() { ... }     // one method per control
	public void ChooseFilter( string filter ) { ... }

	void Update( System.Func<PackagesUiState, PackagesUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };
}
```

- **One method per control.** `OpenStore`, `SubmitSearch`, `LoadMore`, `ToggleDetails`,
  `ChooseFilter`, `InstallApp`, `UninstallApp`… Public and named after the thing the player does,
  not after the field it flips. The panel calls them; nothing else does.
- **One writer.** `Update` is the only place `UiState` is assigned, and the only place `Revision`
  moves. A change that can't forget the redraw can't exist.
- **Raw data is private and separate.** What the computer said (`apps`, `storePackages`,
  `installedPackages`, `knownVersion`) lives in fields; the rows are built from them by
  `BuildStoreRows` / `BuildAppRows` whenever either side changes. Don't draw from a field, and
  don't cache a row as if it were data.
- **The guards are here, not in the UI.** `if ( refreshing ) return;` at the top of
  `FetchStoreAsync` is the rule that one page is fetched at a time; the disabled button is only its
  reflection.
- **The panel argument is the only panel-shaped thing it knows.** It is used for `Navi.*` calls and
  for nothing else — no `Children`, no classes, no styles, no `Tick` of a panel.

### `Packages.razor` — what it looks like

```razor
@code
{
	/// <summary>The app's logic and its state. Made on first use: calls need the panel, which is `this`.</summary>
	PackagesViewModel ViewModel => field ??= new(this);

	/// <summary>What is drawn below. Read only.</summary>
	PackagesUiState UiState => ViewModel.UiState;

	public override void Tick()
	{
		base.Tick();

		FitScrollbarGutter();

		ViewModel.Tick();
	}

	/// <summary>The engine rebuilds this panel when the hash changes, and the revision is what changes.</summary>
	protected override int BuildHash() => ViewModel.Revision;
}
```

The markup reads `UiState` and calls `ViewModel`:

```razor
<div class="tab @(UiState.ShowStore ? "active" : "")" @onclick=@(() => ViewModel.OpenStore())>Store</div>

@foreach ( var entry in UiState.StoreRows )
{
	<div class="button install" @onclick=@(() => ViewModel.InstallPackage( entry.Ident ))>Install</div>
}
```

Naming, so this file and the next one read the same:

| Thing | Name |
|---|---|
| The view model object | `ViewModel` |
| Its backing field in the panel | none — `field` (C# 14) declares it |
| The state, in the panel | `UiState` (property, `=> ViewModel.UiState`) |
| The state, in the view model | `UiState` (property, `private set`) |
| The lambda parameter in `Update` | `uiState` — `Update( uiState => uiState with { … } )` |
| The updater | `Update` |
| The redraw counter | `Revision` |
| One method per control | verb-first: `OpenStore`, `ChooseFilter`, `InstallPackage` |

## Where a rule goes

| A change to… | Goes in | Why |
|---|---|---|
| What a click does, which call it makes | ViewModel | It's the app, and it has to work without a panel |
| Which rows exist, in which order, with which button | ViewModel (a `Build*` method) | Filtering is policy, not drawing |
| Wording: empty states, error notices, "2 KB" | ViewModel, into the state | One place for strings means one place to fix them |
| A flag the markup needs (`HasMore`, `Selected`) | UiState, computed | The markup doesn't do arithmetic |
| `@foreach`, `@if`, `@switch` over an enum | razor | Drawing is not deciding |
| A class toggled from the rendered panel tree | razor | View plumbing, see below |

## The one thing that stays in the panel

`FitScrollbarGutter` in Packages walks the rendered children every frame and toggles a `scrolling`
class, because the engine draws its scrollbar over the content and `scrollbar-gutter` reserves room
even when nothing scrolls. That's a correction to the panel's own tree, it holds no state, and it
means nothing to the view model — so it stays in the panel, with a comment saying why. Anything
like it (measuring a rendered box, nudging a class on a child) belongs there too, and only there.

## Bindings: the one edge that isn't a method call

The engine's widgets are stateful by design: `TextEntry` owns its text, its caret, its undo history
and its IME composition. A panel can't be more stateless than the tree it builds — what it can do is
own nothing itself and hand every write to the view model. A `Value:bind` is where that gets blurred,
because Razor then wants a settable property on the panel, and a setter is exactly the kind of member
someone later "simplifies":

```csharp
string SearchText => ViewModel.SearchText;   // CS0200: the binding assigns to it
```

So a field that reports edits is wired as a method, like every other control:

```razor
<TextEntry Value=@( UiState.SearchText )
           OnTextEdited=@SearchEdited
           onsubmit=@( () => ViewModel.SubmitSearch() )/>
```

```csharp
void SearchEdited( string text ) => ViewModel.SetSearchText( text );
```

`Value` is one-way, and the edit comes back through `TextEntry.OnTextEdited` — as a method group,
because a lambda there doesn't compile ("the delegate type could not be inferred") and the panel's own
method supplies the type. `SetSearchText` replaces the state without going through `Update`, so typing
doesn't bump `Revision` and the list doesn't rebuild under the caret.

What this depends on: the panel pushes `Value` on every rebuild, so `SearchText` has to live in the
state and be replaced on every edit. Then the pushed value equals what the field already holds and the
widget keeps the caret where it was (checking this is how to catch a state that moved out).

## Async, threads and `Revision`

The **data path is async**: `Navi.*` calls return `Task`s that the computer answers a frame or so
later, and a Store refresh holds its running state for a moment so an instant answer still reads as
work. The **state path is synchronous**: every change is one assignment of a new record in
`Update`, and the panel reads `UiState` on the main thread when it rebuilds.

That means no locks and no notification: there is exactly one writer, and writes only start from a
click handler, from a continuation of an `await` the panel began, or from `Tick`'s polling. Keep it
that way — no `Task.Run`, no `ConfigureAwait( false )` around anything that touches `UiState`. The
swap itself is a single reference assignment, so a stray thread wouldn't corrupt the state, but two
threads would still decide the order; marshal back to the main thread first.

## A `Changed` event, only when it earns its place

Nothing in Packages declares one. The panel doesn't need it: it redraws when `Revision` changes,
which the engine checks anyway, so a push would only arrive between two frames that already compare
the hash. StateFlow-style observation is the wrong tool for the render path in s&box.

It earns its place when something **outside** the panel needs to react to the app's state — game
code watching what the player installed, a second view, telemetry, a test harness. Then:

```csharp
/// <summary>
/// Raised after every UiState replacement, with the state that is now current. A notification, not a
/// queue: a listener that is slow misses the states in between, exactly like StateFlow.
/// </summary>
public event System.Action<PackagesUiState> Changed;

void Update( System.Func<PackagesUiState, PackagesUiState> change )
{
	UiState = change( UiState ) with { Revision = UiState.Revision + 1 };
	Changed?.Invoke( UiState );
}
```

Rules for it, if it's added:

- **Subscribe after reading `UiState` once**, so a listener starts from the current value rather
  than waiting for the next change. That's the `StateFlow` `.value` half.
- **The event fires on whatever thread called `Update`** — today the main thread. A listener that
  hops must marshal for itself.
- **Never use it to drive the panel.** `BuildHash` and `Revision` do that; a panel that also
  subscribes has two sources of truth about what it is drawing.
- **Don't fire it from `Update` above if nothing ever subscribes** — an event with no listener is
  one null check per change, which is fine, but a stream of states into a channel that nobody reads
  is not. If conflation is genuinely wanted, a bounded channel with `DropOldest` is the shape:

  ```csharp
  readonly Channel<PackagesUiState> channel = Channel.CreateBounded<PackagesUiState>(
  	new BoundedChannelOptions( 1 ) { FullMode = BoundedChannelFullMode.DropOldest } );

  public IAsyncEnumerable<PackagesUiState> States => channel.Reader.ReadAllAsync();
  ```

  Compile it before relying on it: the app has to pass the s&box whitelist, and
  `System.Threading.Channels` is the kind of namespace that might not.

## Starting an app this way

1. Write the state first: what does the panel print, row by row? That's `<App>UiState.cs`, with
   sibling records for the rows and enums for anything the markup would compare as a string.
2. Write the view model with the app's calls and its private data, then the `Build*` methods that
   turn data into rows. Add `Update` and make it the only setter.
3. Write the panel last: it's markup, `ViewModel`, `UiState`, `BuildHash`, and a `Tick` if the app
   polls.
4. Compile, then close and reopen the app's window — a hot reload does not give a panel instance
   that already exists the new members (`ViewModel` will read as null), so an old window proves
   nothing. From `run_csharp`, find the panel and print `ViewModel.UiState` to see what it would
   draw.

## Sharing an app in multiplayer

With `Shared` in its `.napp`, the same panel class runs on every client, in one of two roles. The split above is
what makes it work: `UiState` is pure data, so the host can send it, and the panel draws one value, so it can draw the
host's.

- **The host's copy** (offline, the only one) owns the view model. Its `Tick` runs the view model and calls
  `Navi.Publish( this, ViewModel.UiState )`. Publishing the same object again sends nothing, which is why `Update`
  replaces the record instead of changing it.
- **A viewer's copy** (`Navi.IsViewer( this )`) has no view model and makes no calls: `NaviAppHost` refuses them.
  It draws `Navi.GetState<TUiState>( this )`, or an `Empty` state until the first one arrives. The class is set
  before the panel's first tick, so a viewer never creates a view model by accident.
- **Controls are actions.** `@onclick=@( () => Navi.Act( this, CalculatorViewModel.PressAction, key.Label ) )`.
  The host's copy gets it in `OnEvent` (`Navi.IsAction( e, out var action, out var args )`) and the view model's
  `Act` turns it back into a method call, one case per control. Arguments are strings; parse them there, and ignore
  what doesn't parse, as if the player had clicked nothing. Keep action names as constants on the view model.
- **The state is sent to every player.** Keep it small (Pac-Man packs its dots into one string), leave out what
  never changes (Calculator's keypad is a static property), and remember that viewers get it a tick or so late
  (Pac-Man's board glides its actors to where the state puts them).
- **Text fields send every edit as an action**, so others see the text being typed. If the state holds the text
  without bumping `Revision` (so typing doesn't redraw lists), put the text in `BuildHash` too, except while the
  field has focus: there the player is typing, and the host's text is a keystroke behind. Packages' search field
  does this.

```csharp
CalculatorUiState UiState => Navi.IsViewer( this ) ? Navi.GetState<CalculatorUiState>( this ) ?? CalculatorUiState.Empty : ViewModel.UiState;

public override void Tick()
{
	base.Tick();

	if ( !Navi.IsViewer( this ) )
		Navi.Publish( this, ViewModel.UiState );
}

protected override void OnEvent( PanelEvent e )
{
	if ( Navi.IsAction( e, out var action, out var args ) )
	{
		ViewModel.Act( action, args );
		e.StopPropagation();
		return;
	}

	base.OnEvent( e );
}
```

An action is what the player could have clicked, not data to trust: the host checks only that it came from the
computer's user. Whatever the action does still goes through the computer's permissions, as the click always did.

## Several windows, and headless apps

Each window is a separate panel, so a window has its own `ViewModel` and `UiState` like any app. An app with more than one window (`Windows` in its manifest) or a service (`Headless`) has to decide where the state that the windows have in common lives. Two rules:

- **One owner for state they share.** Put it in a plain class the app's code owns (a static or a singleton held by the service panel's view model), and let each window's view model read it. Don't pass it through Navi, and don't have one window reach into another's panel: the panels are separate instances and a viewer's copy has nothing of the host's.
- **The service is the app, the windows are views.** For a headless app, the service panel's view model does the work (timers, downloads, the tray menu) and the window panels only draw its state and call its methods. A window can then be closed and reopened without losing anything, which is the point of headless.

The service's view model is made like any other: `new( this )` on first use. It makes the tray menu with `Navi.SetTray` and reacts to the player's tray clicks, which arrive in the panel's `OnEvent` as `Navi.IsTrayClick`, as a method call on the view model, one case per menu item, in the same way a shared app turns an action into a call.

```csharp
public void TrayClicked( string item )
{
	switch ( item )
	{
		case Navi.TrayPrimary:
		case "settings": _ = Navi.OpenWindow( app, "settings" ); break;
		case "sync": StartSync(); break;
	}
}
```

Tray items change what the app shows, so keep them like any other state: build the list from `UiState` and call `Navi.SetTray` when it changes, not every tick.

## If you come from Kotlin

- `record` ≈ `data class`: `equals`/`hashCode`/`toString` by value, `with` ≈ `copy( x = … )`. A
  positional record also generates a deconstruct, like `componentN`.
- One trap: a record's equality over `IReadOnlyList`/arrays is by **reference**, while Kotlin's
  `List.equals` is structural. Don't compare states to detect change — read `Revision`.
- `with` and `copy()` are both shallow: list members are shared by the copy. Always build a new
  list when one changes, as `Build*` does.
- States are `sealed`, mirroring a data class being final.
- `Task<T>` ≈ `Deferred`, `async`/`await` ≈ `suspend`, `IAsyncEnumerable<T>` + `await foreach` ≈
  `Flow` + `collect`. There is no BCL `StateFlow`; the `Changed` event is the cheap version, and a
  conflation channel is the faithful one. For drawing, neither is needed — the panel's hash is
  the observation.
