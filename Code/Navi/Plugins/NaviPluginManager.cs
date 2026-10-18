using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Installs and runs plugins. Only the host installs: it downloads a plugin from sbox.game, saves the list in its local
/// data (<c>navi/plugins.json</c>) and loads that list again whenever it hosts. It tells every client (and each one that
/// joins later) which plugins are running, and they download and activate the same ones. A plugin can't be unloaded:
/// removing one only stops it loading next time.
/// </summary>
/// <remarks>
/// The library loads the plugin and calls it, so the plugin needs no Navi types (an addon can't see them). A plugin is a
/// plain class with a public parameterless constructor. After creating it, Navi calls these by name, whatever their
/// visibility, and skips the ones it lacks:
/// <list type="bullet">
/// <item><c>OnEnable()</c> or <c>OnEnable( IDictionary&lt;string, System.Delegate&gt; api )</c>, once. <c>api</c> holds what Navi
/// offers under keys that start with "navi." (<see cref="CreateApi"/>). The plugin adds its own hooks to it under "hook." keys.</item>
/// <item><c>OnUpdate()</c>, every frame of the active scene.</item>
/// </list>
/// Everything that crosses between the two sides is a .NET or engine type: delegates, strings, <c>Panel</c>, <c>Vector2</c>.
/// Navi reads the plugins' hooks with <see cref="Hooks{T}"/> where it can be extended.
/// </remarks>
public static class NaviPluginManager
{
	/// <summary>
	/// Package type of published plugins. It is the file extension of <see cref="NaviPluginResource"/>.
	/// </summary>
	public const string PackageType = "nplug";

	/// <summary>
	/// Version of what Navi offers plugins (<see cref="CreateApi"/>) and of the hooks it reads (<see cref="Hooks{T}"/>). It
	/// goes up when a key changes in a way that breaks a plugin.
	/// </summary>
	public const int ApiVersion = 1;

	const string SavePath = "navi/plugins.json";

	static readonly Dictionary<string, NaviRunningPlugin> running = new( System.StringComparer.OrdinalIgnoreCase );
	static readonly HashSet<string> activating = new( System.StringComparer.OrdinalIgnoreCase );

	static List<string> installed = [];
	static bool savedLoaded;

	/// <summary>
	/// Can this machine install plugins: it hosts the game, or the game isn't networked. A client can't.
	/// </summary>
	public static bool CanManage => !Networking.IsActive || Networking.IsHost;

	/// <summary>
	/// The plugins running now, on the host and on clients.
	/// </summary>
	public static IReadOnlyCollection<NaviRunningPlugin> Running => running.Values;

	/// <summary>
	/// The host's saved plugins, by package ident. They load when the host starts a game.
	/// </summary>
	public static IReadOnlyList<string> Installed => installed;

	/// <summary>
	/// Idents of the plugins running now, for the host to send to clients.
	/// </summary>
	internal static string[] RunningIdents => running.Keys.ToArray();

	/// <summary>
	/// Download a plugin, start it, and save it to the host's list so it loads next time. Returns the plugin, or null if
	/// this machine isn't the host or the package isn't a working plugin.
	/// </summary>
	public static async Task<NaviRunningPlugin> InstallAsync( string packageIdent )
	{
		if ( !CanManage )
		{
			Log.Warning( "NaviPluginManager: only the host installs plugins" );
			return null;
		}

		var plugin = await ActivateAsync( packageIdent );
		if ( plugin is null )
			return null;

		if ( !installed.Contains( plugin.Ident, System.StringComparer.OrdinalIgnoreCase ) )
		{
			installed.Add( plugin.Ident );
			Save();
		}

		NaviPluginSystem.Current?.Announce();
		return plugin;
	}

	/// <summary>
	/// Take a plugin off the host's list so it doesn't load next time. A running plugin can't be unloaded, so it keeps
	/// running until the game ends.
	/// </summary>
	public static bool Remove( string packageIdent )
	{
		if ( !CanManage || NaviStore.NormalizeIdent( packageIdent ) is not { } ident )
			return false;

		if ( installed.RemoveAll( x => string.Equals( x, ident, System.StringComparison.OrdinalIgnoreCase ) ) == 0 )
			return false;

		Save();
		return true;
	}

	/// <summary>
	/// Start every plugin on the host's saved list that isn't running yet.
	/// </summary>
	internal static async Task ActivateSavedAsync()
	{
		if ( !savedLoaded )
		{
			savedLoaded = true;
			installed = Read();
		}

		foreach ( var ident in installed.ToArray() )
			await ActivateAsync( ident );

		NaviPluginSystem.Current?.Announce();
	}

	/// <summary>
	/// Download a plugin's package and start it, unless it is running already. Returns the plugin, or null if the package
	/// isn't a working plugin.
	/// </summary>
	internal static async Task<NaviRunningPlugin> ActivateAsync( string packageIdent )
	{
		if ( NaviStore.NormalizeIdent( packageIdent ) is not { } ident )
		{
			Log.Warning( $"NaviPluginManager: '{packageIdent}' is not a package ident" );
			return null;
		}

		if ( running.TryGetValue( ident, out var existing ) )
			return existing;

		// A second request for a plugin that is still downloading would start it twice.
		if ( !activating.Add( ident ) )
			return null;

		try
		{
			var resource = await Cloud.Load<NaviPluginResource>( ident, withCode: true );
			if ( resource is null )
			{
				Log.Warning( $"NaviPluginManager: '{ident}' is not a Navi plugin" );
				return null;
			}

			return Start( ident, resource );
		}
		finally
		{
			activating.Remove( ident );
		}
	}

	/// <summary>
	/// Create the plugin a manifest names and call its <c>OnEnable</c>. Null if its class isn't loaded.
	/// </summary>
	internal static NaviRunningPlugin Start( string ident, NaviPluginResource resource )
	{
		if ( resource.FindPluginType() is not { } type )
		{
			Log.Warning( $"NaviPluginManager: '{ident}' has no plugin class '{resource.PluginType}' in its code" );
			return null;
		}

		var instance = type.Create<object>();
		if ( instance is null )
		{
			Log.Warning( $"NaviPluginManager: couldn't create '{resource.PluginType}' of '{ident}'" );
			return null;
		}

		if ( instance is NaviPlugin plugin )
			plugin.Ident = ident;

		var started = new NaviRunningPlugin( ident, resource, instance, type );
		running[ident] = started;

		try
		{
			started.Enable();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviPluginManager: '{ident}' failed to enable: {e.Message}" );
		}

		Log.Info( $"NaviPluginManager: '{ident}' is running" );
		return started;
	}

	/// <summary>
	/// The class with this full name (or, failing that, class name) that a plugin can be: a concrete class. Null if there
	/// is none loaded.
	/// </summary>
	internal static TypeDescription FindPluginType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		var types = TypeLibrary.GetTypes().Where( x => x.IsClass && !x.IsAbstract && !x.IsGenericType ).ToList();
		return types.FirstOrDefault( x => x.FullName == name ) ?? types.FirstOrDefault( x => x.ClassName == name );
	}

	/// <summary>
	/// What Navi offers a plugin, by key, in the dictionary it gets in <c>OnEnable</c>. Every delegate uses .NET or engine
	/// types only, because the plugin can't see Navi's.
	/// </summary>
	internal static Dictionary<string, System.Delegate> CreateApi() => new()
	{
		["navi.version"] = (System.Func<int>)(() => ApiVersion),
		["navi.computers"] = (System.Func<IReadOnlyList<Component>>)(() =>
			Game.ActiveScene?.GetAllComponents<NaviComputer>().Cast<Component>().ToList() ?? []),
	};

	/// <summary>
	/// The hook every running plugin registered under this key, in the order the plugins started. A plugin registers one
	/// by putting a delegate under the key in the dictionary <c>OnEnable</c> gets. Navi calls this where it can be extended.
	/// A plugin whose delegate isn't a <typeparamref name="T"/> is skipped.
	/// </summary>
	public static IEnumerable<T> Hooks<T>( string key ) where T : System.Delegate =>
		running.Values
			.Select( x => x.Api.TryGetValue( key, out var hook ) ? hook as T : null )
			.Where( x => x is not null );

	/// <summary>
	/// One page of published plugins matching <paramref name="text"/>.
	/// </summary>
	public static async Task<List<Package>> SearchAsync( string text = "", int skip = 0, int pageSize = NaviStore.PageSize )
	{
		var result = await Package.FindAsync( $"type:{PackageType} {text}".Trim(), pageSize, skip );
		return result?.Packages?.Where( x => x.TypeName == PackageType ).ToList() ?? [];
	}

	internal static void Update()
	{
		foreach ( var plugin in running.Values.ToArray() )
			plugin.Update();
	}

	static List<string> Read()
	{
		if ( FileSystem.Data is not { } data || !data.FileExists( SavePath ) )
			return [];

		try
		{
			return data.ReadJson<List<string>>( SavePath )?
				.Select( NaviStore.NormalizeIdent )
				.Where( x => x is not null )
				.Distinct()
				.ToList() ?? [];
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviPluginManager: couldn't read the plugin list: {e.Message}" );
			return [];
		}
	}

	static void Save()
	{
		if ( FileSystem.Data is not { } data )
			return;

		try
		{
			data.CreateDirectory( "navi" );
			data.WriteJson( SavePath, installed );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviPluginManager: couldn't save the plugin list: {e.Message}" );
		}
	}

	/// <summary>
	/// Install a plugin by its package ident. Host only.
	/// </summary>
	[ConCmd( "navi_plugin_install" )]
	public static async void InstallCommand( string packageIdent ) => await InstallAsync( packageIdent );

	/// <summary>
	/// Take a plugin off the list. It stops loading next time. Host only.
	/// </summary>
	[ConCmd( "navi_plugin_remove" )]
	public static void RemoveCommand( string packageIdent )
	{
		if ( !Remove( packageIdent ) )
			Log.Info( $"NaviPluginManager: '{packageIdent}' isn't on the list, or this machine isn't the host" );
	}

	/// <summary>
	/// Log the saved and the running plugins.
	/// </summary>
	[ConCmd( "navi_plugins" )]
	public static void ListCommand()
	{
		Log.Info( $"Navi plugins saved: {(installed.Count == 0 ? "none" : string.Join( ", ", installed ))}" );
		Log.Info( $"Navi plugins running: {(running.Count == 0 ? "none" : string.Join( ", ", running.Keys ))}" );
	}
}

/// <summary>
/// A plugin that is running: the instance of its class, and the dictionary it shares with Navi.
/// </summary>
public sealed class NaviRunningPlugin
{
	public string Ident { get; }
	public NaviPluginResource Resource { get; }

	/// <summary>
	/// The instance of the class the manifest names. It can be any class, so it is only an object.
	/// </summary>
	public object Instance { get; }

	/// <summary>
	/// What Navi offered under "navi." keys, and the hooks the plugin set under "hook." keys.
	/// </summary>
	public IDictionary<string, System.Delegate> Api { get; } = NaviPluginManager.CreateApi();

	readonly MethodDescription onEnable;
	readonly MethodDescription onUpdate;
	bool updateFailed;

	internal NaviRunningPlugin( string ident, NaviPluginResource resource, object instance, TypeDescription type )
	{
		Ident = ident;
		Resource = resource;
		Instance = instance;

		// Looked up by name, whatever the visibility: the plugin's class is Navi's only link to it. The lookup is by
		// method list, because GetMethod( name ) doesn't find all of them.
		onEnable = type.Methods.FirstOrDefault( x => x.Name == "OnEnable" && !x.IsStatic && x.Parameters.Length <= 1 );
		onUpdate = type.Methods.FirstOrDefault( x => x.Name == "OnUpdate" && !x.IsStatic && x.Parameters.Length == 0 );
	}

	internal void Enable()
	{
		if ( onEnable is null )
			return;

		onEnable.Invoke( Instance, onEnable.Parameters.Length == 1 ? [Api] : [] );
	}

	internal void Update()
	{
		// A plugin that throws every frame would flood the log, so one failure stops its updates.
		if ( onUpdate is null || updateFailed )
			return;

		try
		{
			onUpdate.Invoke( Instance );
		}
		catch ( System.Exception e )
		{
			updateFailed = true;
			Log.Warning( e, $"NaviPluginManager: '{Ident}' failed in OnUpdate and won't update again: {e.Message}" );
		}
	}
}

/// <summary>
/// Present in every scene. Runs the plugins' updates, loads the host's saved plugins, and tells clients which plugins
/// are running so they download them too. It needs no setup in the game.
/// </summary>
public sealed class NaviPluginSystem : GameObjectSystem<NaviPluginSystem>, Component.INetworkListener
{
	public NaviPluginSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Update, "NaviPlugins" );

		if ( NaviPluginManager.CanManage )
			_ = NaviPluginManager.ActivateSavedAsync();
	}

	void Update()
	{
		// Every scene has a system. Only the active one drives the plugins, so they update once a frame.
		if ( Scene == Game.ActiveScene )
			NaviPluginManager.Update();
	}

	/// <summary>
	/// A client has loaded in: tell it which plugins to run.
	/// </summary>
	void Component.INetworkListener.OnActive( Connection connection )
	{
		using ( Rpc.FilterInclude( connection ) )
			ActivateOnClient( NaviPluginManager.RunningIdents );
	}

	/// <summary>
	/// Tell every client which plugins are running, after the host installs one.
	/// </summary>
	internal void Announce()
	{
		if ( Networking.IsActive && Networking.IsHost )
			ActivateOnClient( NaviPluginManager.RunningIdents );
	}

	[Rpc.Broadcast]
	void ActivateOnClient( string[] idents )
	{
		// Only the host says which plugins run. The host already runs them, so this is for clients.
		if ( Rpc.Caller != Connection.Host || NaviPluginManager.CanManage )
			return;

		foreach ( var ident in idents ?? [] )
			_ = NaviPluginManager.ActivateAsync( ident );
	}
}
