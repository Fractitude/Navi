namespace Sandbox;

/// <summary>
/// Optional base class for a plugin that lives in the game's own code, where Navi's types are visible. A plugin from
/// the Store doesn't derive from it: an addon can't see Navi's types, so Navi finds a plugin's hooks by name instead
/// (see <see cref="NaviPluginManager"/>). The two are the same to Navi.
/// </summary>
public abstract class NaviPlugin
{
	/// <summary>
	/// The package ident the plugin was downloaded from, e.g. "fractitude.nplug_themes".
	/// </summary>
	public string Ident { get; internal set; }

	/// <summary>
	/// Called once, when the plugin is activated, with the API Navi offers and the hooks the plugin sets. See
	/// <see cref="NaviPluginManager"/>.
	/// </summary>
	public virtual void OnEnable( IDictionary<string, System.Delegate> api )
	{
	}

	/// <summary>
	/// Called every frame of the active scene.
	/// </summary>
	public virtual void OnUpdate()
	{
	}
}
