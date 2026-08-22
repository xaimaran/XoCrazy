using System;
using System.Collections.Generic;

namespace ThemeForge.Core
{
    internal sealed class CatalogEntry
    {
        public string StorageName;   // exact name the shell stores it under
        public string DisplayName;   // what a human calls it
        public string Group;
        public string Hint;          // sample token, shown in the row preview

        public CatalogEntry(string group, string storageName, string displayName, string hint)
        {
            Group = group;
            StorageName = storageName;
            DisplayName = displayName;
            Hint = hint;
        }
    }

    /// <summary>
    /// The short list.
    ///
    /// The Fonts &amp; Colors page enumerates every registered display item — several hundred
    /// rows, alphabetised, most of which nobody has ever changed on purpose. These are the
    /// ones that actually decide what C# looks like. Everything else is still reachable via
    /// the "Show all items" toggle, which enumerates the shell directly.
    /// </summary>
    internal static class ClassificationCatalog
    {
        public const string GroupSyntax = "Syntax";
        public const string GroupTypes = "Types";
        public const string GroupMembers = "Members";
        public const string GroupSurface = "Editor surface";
        public const string GroupDiagnostics = "Diagnostics";

        public static readonly CatalogEntry[] Curated =
        {
            // ---- Syntax: the tokens the lexer produces -------------------------------
            new CatalogEntry(GroupSyntax, "keyword",                    "Keyword",                 "static"),
            new CatalogEntry(GroupSyntax, "keyword - control",          "Keyword — control flow",  "if"),
            new CatalogEntry(GroupSyntax, "string",                     "String",                  "\"text\""),
            new CatalogEntry(GroupSyntax, "string - verbatim",          "String — verbatim",       "@\"C:\\\""),
            new CatalogEntry(GroupSyntax, "string - escape character",  "String — escape",         "\\n"),
            new CatalogEntry(GroupSyntax, "number",                     "Number",                  "42"),
            new CatalogEntry(GroupSyntax, "comment",                    "Comment",                 "// note"),
            new CatalogEntry(GroupSyntax, "operator",                   "Operator",                "=>"),
            new CatalogEntry(GroupSyntax, "operator - overloaded",      "Operator — overloaded",   "+"),
            new CatalogEntry(GroupSyntax, "punctuation",                "Punctuation",             "{ ; }"),
            new CatalogEntry(GroupSyntax, "identifier",                 "Identifier",              "name"),
            new CatalogEntry(GroupSyntax, "preprocessor keyword",       "Preprocessor",            "#region"),
            new CatalogEntry(GroupSyntax, "preprocessor text",          "Preprocessor — text",     "Parameters"),
            new CatalogEntry(GroupSyntax, "Plain Text",                 "Plain text",              "text"),

            // ---- Types ---------------------------------------------------------------
            new CatalogEntry(GroupTypes, "class name",                  "Class",                   "Program"),
            new CatalogEntry(GroupTypes, "record class name",           "Record",                  "Point"),
            new CatalogEntry(GroupTypes, "struct name",                 "Struct",                  "Vector3"),
            new CatalogEntry(GroupTypes, "record struct name",          "Record struct",           "Pair"),
            new CatalogEntry(GroupTypes, "interface name",              "Interface",               "IList"),
            new CatalogEntry(GroupTypes, "enum name",                   "Enum",                    "DayOfWeek"),
            new CatalogEntry(GroupTypes, "delegate name",               "Delegate",                "Action"),
            new CatalogEntry(GroupTypes, "type parameter name",         "Type parameter",          "T"),
            new CatalogEntry(GroupTypes, "namespace name",              "Namespace",               "System.IO"),

            // ---- Members and locals ---------------------------------------------------
            new CatalogEntry(GroupMembers, "method name",               "Method",                  "Run()"),
            new CatalogEntry(GroupMembers, "extension method name",     "Extension method",        "Where()"),
            new CatalogEntry(GroupMembers, "property name",             "Property",                "Length"),
            new CatalogEntry(GroupMembers, "field name",                "Field",                   "_count"),
            new CatalogEntry(GroupMembers, "constant name",             "Constant",                "MaxValue"),
            new CatalogEntry(GroupMembers, "local name",                "Local",                   "index"),
            new CatalogEntry(GroupMembers, "parameter name",            "Parameter",               "value"),
            new CatalogEntry(GroupMembers, "event name",                "Event",                   "Changed"),
            new CatalogEntry(GroupMembers, "label name",                "Label",                   "retry:"),

            // ---- The surface you stare at all day -------------------------------------
            new CatalogEntry(GroupSurface, "Selected Text",             "Selection",               "selected"),
            new CatalogEntry(GroupSurface, "Inactive Selected Text",    "Selection — inactive",    "selected"),
            new CatalogEntry(GroupSurface, "Line Number",               "Line numbers",            "42"),
            new CatalogEntry(GroupSurface, "Selected Line Number",      "Line number — current",   "42"),
            new CatalogEntry(GroupSurface, "Indicator Margin",          "Indicator margin",        " "),
            new CatalogEntry(GroupSurface, "brace matching",            "Brace matching",          "{ }"),
            new CatalogEntry(GroupSurface, "MarkerFormatDefinition/HighlightedReference", "Reference highlight", "name"),
            new CatalogEntry(GroupSurface, "MarkerFormatDefinition/HighlightedWrittenReference", "Write highlight", "name"),
            // The box a collapsed region is drawn as. In VS 2026 this is 'outlining.chevron.*'
            // — the composition walk lists it as "Collapsed Text Indicator (Collapsed)" — and
            // 'Collapsible Text (Collapsed)' is a vestige: it has no format definition left, only
            // a 'Collapsible Text (Collapsed) {LegacyMarker}' classification that nothing paints
            // from and that a dump catches sitting at black. Both are written, because the legacy
            // name is still the live one on VS 2022.
            new CatalogEntry(GroupSurface, "outlining.chevron.collapsed", "Collapsed region",      "..."),
            new CatalogEntry(GroupSurface, "outlining.chevron.expanded", "Collapsed region — open", "..."),
            new CatalogEntry(GroupSurface, "Collapsible Text (Collapsed)", "Collapsed region (legacy)", "..."),
            new CatalogEntry(GroupSurface, "outlining.square",          "Outlining glyph",         "[-]"),
            new CatalogEntry(GroupSurface, "outlining.collapsehintadornment", "Collapsed region box", "..."),
            new CatalogEntry(GroupSurface, "outlining.verticalrule",    "Outlining guide line",    "|"),

            // ---- Diagnostics ----------------------------------------------------------
            new CatalogEntry(GroupDiagnostics, "Syntax Error",          "Syntax error squiggle",   "err"),
            new CatalogEntry(GroupDiagnostics, "Compiler Error",        "Compiler error",          "CS0103"),
            new CatalogEntry(GroupDiagnostics, "Warning",               "Warning squiggle",        "warn"),
            new CatalogEntry(GroupDiagnostics, "Other Error",           "Other error",             "err"),
            new CatalogEntry(GroupDiagnostics, "excluded code",         "Excluded code",           "#if false"),
        };

        private static Dictionary<string, CatalogEntry> _byStorageName;

        public static CatalogEntry Find(string storageName)
        {
            if (_byStorageName == null)
            {
                _byStorageName = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in Curated)
                    _byStorageName[e.StorageName] = e;
            }
            CatalogEntry hit;
            return _byStorageName.TryGetValue(storageName ?? string.Empty, out hit) ? hit : null;
        }

        /// <summary>
        /// Maps a MEF classification type name onto a catalog row. Roslyn's classification
        /// names and the Fonts &amp; Colors storage names are the same string in almost every
        /// case, which is what makes caret targeting a one-line lookup.
        /// </summary>
        public static string ToStorageName(string classificationTypeName)
        {
            if (string.IsNullOrEmpty(classificationTypeName))
                return null;
            if (Find(classificationTypeName) != null)
                return classificationTypeName;

            // A handful of display items capitalise differently than the classification type.
            switch (classificationTypeName.ToLowerInvariant())
            {
                case "text": return "Plain Text";
                case "whitespace": return "Plain Text";
                default: return classificationTypeName;
            }
        }
    }
}
