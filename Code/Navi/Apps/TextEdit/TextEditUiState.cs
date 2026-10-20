namespace NaviApps;

/// <summary>
/// The bar under the toolbar where a path is typed to save as a file. None while it is closed.
/// </summary>
public enum TextEditBar
{
	None,
	SaveAs,
}

/// <summary>
/// Everything the Text Edit panel draws. Replaced whole by <see cref="TextEditViewModel"/> on every change.
/// </summary>
public sealed record TextEditUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static TextEditUiState Empty { get; } = new();

	/// <summary>Goes up whenever anything else here changes, except <see cref="Text"/>. The panel's BuildHash has it.</summary>
	public int Revision { get; init; }

	/// <summary>
	/// The document. Typing replaces it without bumping <see cref="Revision"/>; see <see cref="TextEditViewModel.Edit"/>.
	/// </summary>
	public string Text { get; init; } = "";

	/// <summary>
	/// Goes up whenever the text is replaced rather than typed: a new document, a file opened, an edit that was refused.
	/// The text field then takes <see cref="Text"/> even while it has the caret, and edits made before are dropped.
	/// </summary>
	public int TextSession { get; init; }

	/// <summary>The file's name, or "Untitled" for a document never saved.</summary>
	public string Name { get; init; } = "Untitled";

	/// <summary>Where the file is, or null for a document never saved.</summary>
	public string Path { get; init; }

	/// <summary>The text differs from what was opened or saved last.</summary>
	public bool Dirty { get; init; }

	/// <summary>The bottom line: where the file is, and whether it is saved.</summary>
	public string Status { get; init; } = "";

	/// <summary>What went wrong last, until the player dismisses it.</summary>
	public string Notice { get; init; }

	public TextEditBar Bar { get; init; }

	/// <summary>
	/// What the path field holds. Typing replaces it without bumping <see cref="Revision"/>, like <see cref="Text"/>.
	/// </summary>
	public string BarText { get; init; } = "";

	/// <summary>Counts bars opened, so the panel focuses the field once for each.</summary>
	public int BarSession { get; init; }

	/// <summary>A question that covers the window until it is answered, such as whether to discard changes; else null.</summary>
	public string ConfirmText { get; init; }

	/// <summary>The button that answers yes, e.g. "Discard".</summary>
	public string ConfirmLabel { get; init; } = "";

	/// <summary>A second answer between yes and Cancel, e.g. "Don't save", or null for none.</summary>
	public string ConfirmOtherLabel { get; init; }

	public bool Confirming => ConfirmText is not null;
	public bool BarOpen => Bar != TextEditBar.None;
}
