using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XoCrazy.Core
{
    /// <summary>
    /// What Visual Studio's own theme held for a channel before XoCrazy ever painted it,
    /// persisted across restarts.
    ///
    /// <see cref="EditorFormatBridge"/> used to snapshot this from the live format map on first
    /// touch, which is only honest inside the process that did the first touch. It is not
    /// honest afterwards: <see cref="FontColorStore"/> persists the colour into Fonts and
    /// Colors, so the next Visual Studio start builds its format maps *from our value*, and the
    /// first apply of that session — <see cref="ThemeApplier"/> at startup, before any tool
    /// window exists — snapshots our own colour as "pristine". From then on "inherit" and the
    /// picker's Clear button restore the colour they were asked to remove, which is the
    /// transparent-does-nothing bug.
    ///
    /// So the snapshot has to outlive the process, and it has to be taken once: first capture
    /// wins, every later one is ignored.
    ///
    /// One guard covers installs that were already poisoned before this file existed. A live
    /// value that matches what <see cref="ThemeStore"/> has on record for the same item is a
    /// value we wrote in an earlier session, not the theme's, and it is captured as "unset" —
    /// the only truthful answer available once the original is gone, and the one that makes
    /// Clear actually clear.
    /// </summary>
    internal static class PristineStore
    {
        public const string Foreground = "fg";
        public const string Background = "bg";

        private sealed class Entry
        {
            public bool ForegroundCaptured;
            public bool ForegroundSet;
            public uint ForegroundRgb;
            public bool BackgroundCaptured;
            public bool BackgroundSet;
            public uint BackgroundRgb;
        }

        public static readonly string Path =
            System.IO.Path.Combine(ThemeStore.Directory, "pristine.xocrazy.json");

        private static Dictionary<string, Entry> _entries;
        private static bool _dirty;

        private static Dictionary<string, Entry> Entries()
        {
            if (_entries != null)
                return _entries;

            _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = File.Exists(Path) ? Path : ThemeStore.LegacyPath(Path);
                if (path == null)
                {
                    Diag.Log("PristineStore: nothing saved at " + Path
                             + "; this session's first touch of each channel becomes the baseline.");
                    return _entries;
                }

                var root = Json.Parse(File.ReadAllText(path));
                var items = root["items"];
                if (items == null || items.Array == null)
                    return _entries;

                foreach (var node in items.Array)
                {
                    var key = node["key"] != null ? node["key"].AsString() : null;
                    if (string.IsNullOrEmpty(key))
                        continue;

                    var entry = new Entry();
                    Read(node, "fg", ref entry.ForegroundCaptured, ref entry.ForegroundSet, ref entry.ForegroundRgb);
                    Read(node, "bg", ref entry.BackgroundCaptured, ref entry.BackgroundSet, ref entry.BackgroundRgb);
                    _entries[key] = entry;
                }

                Diag.Log("PristineStore loaded " + _entries.Count + " baseline(s) from " + path);
            }
            catch (Exception ex)
            {
                // A corrupt baseline must not take the package down. Starting empty means the
                // next paint re-captures, which is no worse than the behaviour before this file.
                Diag.Log("PristineStore load failed: " + ex.Message);
            }
            return _entries;
        }

        private static void Read(Json.Node node, string channel, ref bool captured, ref bool set, ref uint rgb)
        {
            var value = node[channel];
            if (value == null)
                return;

            captured = true;

            // A captured-but-unpainted channel is written as null, and that is the whole point
            // of the file: "Visual Studio painted nothing here" is a different answer from
            // "Visual Studio painted black", and only the first one makes Clear see-through.
            var hex = value.AsString();
            uint parsed;
            if (hex != null && ColorMath.TryParseHex(hex, out parsed))
            {
                set = true;
                rgb = parsed;
            }
        }

        /// <summary>True once this channel has a baseline — which also means we have painted it.</summary>
        public static bool Has(string key, string channel)
        {
            if (string.IsNullOrEmpty(key)) return false;

            Entry entry;
            if (!Entries().TryGetValue(key, out entry))
                return false;

            return channel == Foreground ? entry.ForegroundCaptured : entry.BackgroundCaptured;
        }

        /// <summary>
        /// The baseline for a channel. <paramref name="wasSet"/> false means Visual Studio had
        /// no colour there at all, so restoring it means removing the entry rather than writing
        /// one.
        /// </summary>
        public static bool TryGet(string key, string channel, out bool wasSet, out uint rgb)
        {
            wasSet = false;
            rgb = 0;

            Entry entry;
            if (string.IsNullOrEmpty(key) || !Entries().TryGetValue(key, out entry))
                return false;

            if (channel == Foreground)
            {
                if (!entry.ForegroundCaptured) return false;
                wasSet = entry.ForegroundSet;
                rgb = entry.ForegroundRgb;
                return true;
            }

            if (!entry.BackgroundCaptured) return false;
            wasSet = entry.BackgroundSet;
            rgb = entry.BackgroundRgb;
            return true;
        }

        /// <summary>
        /// Records the baseline for a channel, once. Later calls are ignored — by the time they
        /// happen the map is holding our colour and has nothing left to tell us.
        /// </summary>
        public static void Capture(string key, string channel, bool set, uint rgb)
        {
            if (string.IsNullOrEmpty(key) || Has(key, channel))
                return;

            Entry entry;
            if (!Entries().TryGetValue(key, out entry))
            {
                entry = new Entry();
                Entries()[key] = entry;
            }

            if (channel == Foreground)
            {
                entry.ForegroundCaptured = true;
                entry.ForegroundSet = set;
                entry.ForegroundRgb = rgb;
            }
            else
            {
                entry.BackgroundCaptured = true;
                entry.BackgroundSet = set;
                entry.BackgroundRgb = rgb;
            }

            _dirty = true;
            Diag.Log("  pristine baseline '" + key + "/" + channel + "' = "
                     + (set ? ColorMath.ToHex(rgb) : "<unpainted>"));
        }

        public static void Save()
        {
            if (!_dirty) return;
            try
            {
                if (!System.IO.Directory.Exists(ThemeStore.Directory))
                    System.IO.Directory.CreateDirectory(ThemeStore.Directory);

                var w = new Json.Writer();
                w.BeginObject();
                w.Prop("format", "xocrazy-pristine/1");
                w.BeginArray("items");
                foreach (var pair in Entries())
                {
                    var entry = pair.Value;
                    w.BeginObject();
                    w.Prop("key", pair.Key);
                    if (entry.ForegroundCaptured)
                        w.Prop("fg", entry.ForegroundSet ? ColorMath.ToHex(entry.ForegroundRgb) : null);
                    if (entry.BackgroundCaptured)
                        w.Prop("bg", entry.BackgroundSet ? ColorMath.ToHex(entry.BackgroundRgb) : null);
                    w.EndObject();
                }
                w.EndArray();
                w.EndObject();

                File.WriteAllText(Path, w.ToString(), new UTF8Encoding(false));
                _dirty = false;
            }
            catch (Exception ex)
            {
                Diag.Log("PristineStore save FAILED: " + ex.Message);
            }
        }

        /// <summary>
        /// Drops every baseline. Part of the reset-to-VS-defaults path: once the overrides are
        /// gone the map is Visual Studio's again, so the next paint should re-measure it.
        /// </summary>
        public static void Clear()
        {
            Entries().Clear();
            _dirty = true;
            Save();
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception ex) { Diag.Log("PristineStore delete failed: " + ex.Message); }
        }
    }
}
