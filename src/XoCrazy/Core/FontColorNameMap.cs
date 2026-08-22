using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;

namespace XoCrazy.Core
{
    /// <summary>
    /// Classification name -&gt; Fonts and Colors display item name.
    ///
    /// These are not the same string, and assuming they were is why every single
    /// <c>SetItem</c> in the log failed. The editor's format definitions are exported with a
    /// <c>[Name]</c> the maps are keyed by (<c>class name</c>, <c>method name</c>) and a
    /// separate <c>DisplayName</c> the shell registers the colorable item under
    /// (<c>User Types</c>, <c>Method Name</c>). <c>IVsFontAndColorStorage.GetItem</c> wants the
    /// second one and answers REGDB_E_KEYMISSING for the first.
    ///
    /// There is no API that maps one to the other; the pair only exists on the MEF export, so
    /// the table is built by walking the composition once. Definitions are constructed to read
    /// <c>DisplayName</c>, which is why this is deferred until a write actually needs it.
    /// </summary>
    internal static class FontColorNameMap
    {
        private static Dictionary<string, string> _map;

        /// <summary>
        /// The name to hand <c>GetItem</c>/<c>SetItem</c> for a classification, or the input
        /// unchanged when the composition knows no different name for it.
        /// </summary>
        public static string ToStorageName(IServiceProvider services, string formatName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(formatName))
                return formatName;

            var map = Build(services);
            string displayName;
            return map.TryGetValue(formatName, out displayName) ? displayName : formatName;
        }

        /// <summary>
        /// Every editor format definition name in the composition. This is the only
        /// enumeration of format-map keys that exists — <c>IEditorFormatMap</c> itself has no
        /// listing API, which is why the margins had to be named by hand before.
        /// </summary>
        public static IEnumerable<string> AllFormatNames(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Build(services).Keys;
        }

        private static Dictionary<string, string> Build(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_map != null)
                return _map;

            _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var components = services.GetService(typeof(SComponentModel)) as IComponentModel;
                if (components == null)
                {
                    Diag.Log("NameMap: no component model; storage writes will use raw names.");
                    return _map;
                }

                // IDictionary<string, object> as the metadata view rather than a typed
                // interface: the typed metadata contract for format definitions has moved
                // between editor versions, and a missing member there is a composition
                // exception at runtime, not a compile error.
                var exports = components.DefaultExportProvider
                    .GetExports<EditorFormatDefinition, IDictionary<string, object>>();

                foreach (var export in exports)
                {
                    object nameValue;
                    if (!export.Metadata.TryGetValue("Name", out nameValue))
                        continue;

                    var name = nameValue as string;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string displayName = null;
                    try
                    {
                        // Constructing the definition is the only way to read DisplayName;
                        // one that throws must not cost us the rest of the table.
                        var definition = export.Value;
                        if (definition != null)
                            displayName = definition.DisplayName;
                    }
                    catch (Exception ex)
                    {
                        Diag.Log("NameMap: '" + name + "' would not construct: " + ex.Message);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(displayName) && !_map.ContainsKey(name))
                        _map[name] = displayName;
                }

                Diag.Log("NameMap built: " + _map.Count + " format definition(s) with a display name.");

                // The authoritative list of format-map keys this Visual Studio has. Logged
                // because every "which name paints that surface" question is answered here and
                // nowhere else — there is no documentation of it that survives a VS update.
                foreach (var name in _map.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    Diag.Log("NameMap  key '" + name + "' -> display '" + _map[name] + "'");
            }
            catch (Exception ex)
            {
                Diag.Log("NameMap build FAILED: " + ex.Message);
            }
            return _map;
        }
    }
}
