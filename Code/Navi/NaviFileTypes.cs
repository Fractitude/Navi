namespace Sandbox;

/// <summary>
/// What kind a file is by its name, for the icons the file explorer and the File Picker draw.
/// </summary>
public static class NaviFileTypes
{
	/// <summary>The extensions (lowercase, no dot) of code files, the kinds Code Edit opens.</summary>
	public static readonly string[] CodeExtensions = ["cs", "razor", "scss", "css", "json", "js", "lua", "xml", "html"];

	public static bool IsCode( string name )
	{
		var dot = name?.LastIndexOf( '.' ) ?? -1;
		return dot >= 0 && CodeExtensions.Contains( name[(dot + 1)..].ToLowerInvariant() );
	}
}
