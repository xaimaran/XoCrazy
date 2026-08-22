using System;

namespace XoCrazy
{
	internal static class PackageGuids
	{
		public const string PackageString = "7d3f1a20-9c44-4e7b-9f1e-2b6a8c5d3e01";
		public const string ToolWindowString = "7d3f1a20-9c44-4e7b-9f1e-2b6a8c5d3e02";
		public const string CommandSetString = "7d3f1a20-9c44-4e7b-9f1e-2b6a8c5d3e03";
		public const int CmdIdOpenToolWindow = 0x0100;
		public const int CmdIdTargetCaret = 0x0101;
		public const int CmdIdOpenToolWindowTools = 0x0102;
		public static readonly Guid CommandSet = new Guid(CommandSetString);
	}
	/// <summary>
	/// Font and Color category GUIDs. These are the containers the shell groups
	/// colorable items into; each one is a separate <c>OpenCategory</c> scope.
	/// </summary>
	internal static class FontColorCategories
	{
		/// <summary>Text Editor — every Roslyn classification lives here.</summary>
		public static readonly Guid TextEditor = new Guid("A27B4E24-A735-4D1D-B8E7-9716E1E3D8E0");

		/// <summary>Editor Tooltip (quick info, parameter help).</summary>
		public static readonly Guid EditorTooltip = new Guid("1F987C00-E7C0-4284-8340-A81A3B9C2D24");

		/// <summary>Output / command windows.</summary>
		public static readonly Guid OutputWindow = new Guid("9973EFDF-317D-431C-8BC1-5E88CBFD4F7F");
	}
}