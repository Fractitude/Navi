namespace Sandbox;

/// <summary>
/// The classes the desktop puts on its root panel, and on the secure layer's, for the computer's look (the theme, color
/// scheme, blur and transparency from <see cref="Navi"/>). A style sheet themes itself by selecting on them:
/// <c>.theme-glass.scheme-dark Settings .sidebar { … }</c>. Apps from a Store addon can do the same, since their panels are
/// inside the desktop's.
/// </summary>
public static class NaviThemeClasses
{
	/// <summary>
	/// <c>theme-paper</c> or <c>theme-glass</c>.
	/// </summary>
	public static string Of( Navi.Theme theme ) => theme == Navi.Theme.Glass ? "theme-glass" : "theme-paper";

	/// <summary>
	/// <c>scheme-light</c> or <c>scheme-dark</c>.
	/// </summary>
	public static string Of( Navi.ColorScheme scheme ) => scheme == Navi.ColorScheme.Dark ? "scheme-dark" : "scheme-light";
}
