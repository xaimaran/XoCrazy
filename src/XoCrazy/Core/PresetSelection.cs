using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XoCrazy.Core
{
    /// <summary>
    /// The three independent things a palette can be asked to colour.
    ///
    /// These replace the old single <see cref="BackgroundMode"/> radio, which forced one preset
    /// to answer all three questions at once. They are separate questions: a palette you like
    /// for syntax is not necessarily the one you want deciding the colour of the gutter, and
    /// before this you could not say so.
    /// </summary>
    internal enum PresetSlot
    {
        /// <summary>Syntax colours — keyword, string, type, comment. No backgrounds.</summary>
        Foreground = 0,

        /// <summary>The code area's background, and the selection that sits on it.</summary>
        TextArea = 1,

        /// <summary>The surfaces around the code: gutter, breakpoint bar, margins.</summary>
        Editor = 2,
    }

    /// <summary>
    /// Which preset is assigned to each slot, and which slot the user was last editing.
    ///
    /// Persisted for one reason: the picker used to open on the first card in the list and
    /// preview it immediately, which meant opening the dialog to look at your options silently
    /// repainted your editor into Visual Studio Dark+ before you had touched anything. Reopening
    /// has to land on what you already chose, and land there without applying it.
    ///
    /// Stored beside the theme itself rather than in it: this is a record of intent — "Dracula
    /// is my syntax palette" — and it stays true across the individual colour edits that
    /// <see cref="ThemeStore"/> accumulates on top.
    /// </summary>
    internal static class PresetSelection
    {
        public static readonly string Path =
            System.IO.Path.Combine(ThemeStore.Directory, "selection.json");

        /// <summary>Preset name per slot. Null or absent means "None — leave it to VS".</summary>
        private static Dictionary<PresetSlot, string> _assigned;
        private static PresetSlot _active = PresetSlot.Foreground;
        private static bool _loaded;

        /// <summary>The slot the picker should open on.</summary>
        public static PresetSlot ActiveSlot
        {
            get { Load(); return _active; }
            set { Load(); _active = value; }
        }

        /// <summary>The preset assigned to a slot, or null for None.</summary>
        public static string NameFor(PresetSlot slot)
        {
            Load();
            string name;
            return _assigned.TryGetValue(slot, out name) ? name : null;
        }

        /// <summary>The preset assigned to a slot, resolved against the shipped list.</summary>
        public static ThemePreset PresetFor(PresetSlot slot)
        {
            var name = NameFor(slot);
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (var preset in ThemePresets.All)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }

            // A palette that was renamed or removed between versions. Treated as None rather
            // than resurrected as the first card, which is the behaviour being fixed.
            Diag.Log("PresetSelection: '" + name + "' is assigned to " + slot
                     + " but no longer exists; treating as None.");
            return null;
        }

        /// <summary>Assigns a preset to a slot. <paramref name="name"/> null means None.</summary>
        public static void Assign(PresetSlot slot, string name)
        {
            Load();
            if (string.IsNullOrEmpty(name))
                _assigned.Remove(slot);
            else
                _assigned[slot] = name;
        }

        public static void Save()
        {
            Load();
            try
            {
                if (!System.IO.Directory.Exists(ThemeStore.Directory))
                    System.IO.Directory.CreateDirectory(ThemeStore.Directory);

                var w = new Json.Writer();
                w.BeginObject();
                w.Prop("format", "xocrazy-selection/1");
                w.Prop("active", _active.ToString());
                w.Prop("foreground", NameFor(PresetSlot.Foreground));
                w.Prop("textArea", NameFor(PresetSlot.TextArea));
                w.Prop("editor", NameFor(PresetSlot.Editor));
                w.EndObject();

                File.WriteAllText(Path, w.ToString(), new UTF8Encoding(false));
                Diag.Log("PresetSelection saved: active=" + _active
                         + " fg=" + (NameFor(PresetSlot.Foreground) ?? "None")
                         + " text=" + (NameFor(PresetSlot.TextArea) ?? "None")
                         + " editor=" + (NameFor(PresetSlot.Editor) ?? "None"));
            }
            catch (Exception ex)
            {
                Diag.Log("PresetSelection save FAILED: " + ex.Message);
            }
        }

        /// <summary>Forgets every assignment. Part of the reset-to-VS-defaults path.</summary>
        public static void Clear()
        {
            Load();
            _assigned.Clear();
            _active = PresetSlot.Foreground;
            Save();
        }

        private static void Load()
        {
            if (_loaded)
                return;

            _loaded = true;
            _assigned = new Dictionary<PresetSlot, string>();

            try
            {
                // Falls back to the pre-rename folder for the same reason ThemeStore does: this
                // is a record of intent the user built by hand, and losing it to a rename would
                // reopen the picker on None with no explanation.
                var path = File.Exists(Path) ? Path : ThemeStore.LegacyPath(Path);
                if (path == null)
                {
                    Diag.Log("PresetSelection: nothing saved at " + Path + "; all slots start at None.");
                    return;
                }

                var root = Json.Parse(File.ReadAllText(path));

                Assign(PresetSlot.Foreground, root["foreground"] != null ? root["foreground"].AsString() : null);
                Assign(PresetSlot.TextArea, root["textArea"] != null ? root["textArea"].AsString() : null);
                Assign(PresetSlot.Editor, root["editor"] != null ? root["editor"].AsString() : null);

                var active = root["active"] != null ? root["active"].AsString() : null;
                if (!string.IsNullOrEmpty(active))
                {
                    try { _active = (PresetSlot)Enum.Parse(typeof(PresetSlot), active, true); }
                    catch { _active = PresetSlot.Foreground; }
                }

                Diag.Log("PresetSelection loaded: active=" + _active
                         + " fg=" + (NameFor(PresetSlot.Foreground) ?? "None")
                         + " text=" + (NameFor(PresetSlot.TextArea) ?? "None")
                         + " editor=" + (NameFor(PresetSlot.Editor) ?? "None"));
            }
            catch (Exception ex)
            {
                // A corrupt file means "no assignments", not a broken tool window.
                Diag.Log("PresetSelection load failed: " + ex.Message);
            }
        }
    }
}
