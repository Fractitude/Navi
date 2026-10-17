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
	PackagesViewModel viewModel;

	/// <summary>The app's logic and its state. Made on first use: calls need the panel, which is `this`.</summary>
	PackagesViewModel ViewModel => viewModel ??= new( this );

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
| Its backing field in the panel | `viewModel` |
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
