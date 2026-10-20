namespace Sandbox;

/// <summary>
/// An entry of a <see cref="NaviMenu"/>. <see cref="Keys"/> is its keyboard shortcut, shown at the right, or empty. A
/// <see cref="Separator"/> is a line before the item. A <see cref="Danger"/> item shows red when pointed at (Quit).
/// </summary>
public sealed record NaviMenuItem( string Id, string Label, string Icon = "", string Keys = "", bool Enabled = true,
	bool Separator = false, bool Danger = false );
