using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XoCrazy.Core
{
    /// <summary>
    /// XoCrazy's own persistence, and the reason edits now survive a restart.
    ///
    /// The original design assumed <c>IVsFontAndColorStorage.SetItem</c> was the place edits
    /// live. The trace says otherwise: every write in the log came back SETITEM FAILED, 305 of
    /// them, 0 successes. Two independent reasons, and only one of them is fixable:
    ///
    ///   1. The storage item name is the Fonts and Colors *display* name, not the
    ///      classification name — <c>class name</c> is stored as <c>User Types</c>. That
    ///      mismatch is what <see cref="FontColorNameMap"/> repairs.
    ///   2. A format definition exported with <c>UserVisible=false</c> has no Fonts and Colors
    ///      entry at all. There is nothing to write to, at any name.
    ///
    /// Case 2 cannot be fixed inside the shell's storage, so persistence cannot depend on it.
    /// This file is the durable copy: every applied edit is recorded here, and
    /// <see cref="ThemeApplier"/> pushes it back into the editor format maps on the next start.
    /// Storage writes still happen where they can, so the colours also show up in the shell's
    /// own Fonts and Colors page — but nothing depends on them succeeding.
    /// </summary>
    internal static class ThemeStore
    {
        public static readonly string Directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XoCrazy");

        /// <summary>
        /// Where the theme lived before the rename. Read only as a fallback, and never written:
        /// renaming the folder without this would silently orphan a saved theme and a slot
        /// selection that took real work to arrive at, and present it to the user as "the
        /// extension forgot everything".
        /// </summary>
        private static readonly string LegacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThemeForge");

        /// <summary>The live theme. Written on every apply, read on every VS start.</summary>
        public static readonly string CurrentPath = Path.Combine(Directory, "current.xocrazy.json");

        /// <summary>
        /// The file name used before the rename. A saved theme can be sitting under either name
        /// in either folder — the folder was renamed in one release and the file in the next —
        /// so all four combinations are read, and only <see cref="CurrentPath"/> is written.
        /// </summary>
        private const string LegacyCurrentName = "current.themeforge.json";

        private static Dictionary<string, Snapshot.Record> _records;
        private static bool _dirty;

        private static string Key(Guid category, string item)
        {
            return category.ToString("N") + "|" + (item ?? string.Empty).ToLowerInvariant();
        }

        private static Dictionary<string, Snapshot.Record> Records()
        {
            if (_records != null)
                return _records;

            _records = new Dictionary<string, Snapshot.Record>(StringComparer.Ordinal);
            try
            {
                var path = File.Exists(CurrentPath) ? CurrentPath : LegacyPath(CurrentPath);
                if (path != null)
                {
                    foreach (var record in Snapshot.Deserialize(File.ReadAllText(path)))
                    {
                        // Themes saved before "Plain Text" was recognised as a covering layer
                        // carry a background for it, and ThemeApplier re-asserts that at every
                        // start — so the selection stays hidden until the user happens to apply
                        // a palette again. Dropping the channel on load repairs it once, on the
                        // next start, without touching anything else the theme says.
                        if (string.Equals(record.Item, "Plain Text", StringComparison.OrdinalIgnoreCase)
                            && record.Background != null)
                        {
                            Diag.Log("ThemeStore: dropping the stored 'Plain Text' background ("
                                     + record.Background + "); it paints over the selection.");
                            record.Background = null;
                        }

                        _records[Key(record.Category, record.Item)] = record;
                    }
                    Diag.Log("ThemeStore loaded " + _records.Count + " override(s) from " + path);
                }
                else
                {
                    Diag.Log("ThemeStore: no saved theme at " + CurrentPath);
                }
            }
            catch (Exception ex)
            {
                // A corrupt file must not take the package down with it. Start empty; the
                // next apply overwrites it.
                Diag.Log("ThemeStore load failed: " + ex.Message);
            }
            return _records;
        }

        /// <summary>
        /// The pre-rename twin of a path under <see cref="Directory"/>, if a file is actually
        /// sitting there. Null when there is nothing to fall back to. The first save writes to
        /// the new folder under the new name, so this answers once and then stops mattering.
        /// </summary>
        public static string LegacyPath(string current)
        {
            try
            {
                var name = Path.GetFileName(current);
                var candidates = name == Path.GetFileName(CurrentPath)
                    ? new[]
                      {
                          Path.Combine(Directory, LegacyCurrentName),
                          Path.Combine(LegacyDirectory, name),
                          Path.Combine(LegacyDirectory, LegacyCurrentName)
                      }
                    : new[] { Path.Combine(LegacyDirectory, name) };

                foreach (var legacy in candidates)
                {
                    if (!File.Exists(legacy))
                        continue;

                    Diag.Log("ThemeStore: reading " + legacy + " — pre-rename location.");
                    return legacy;
                }

                return null;
            }
            catch (Exception ex)
            {
                Diag.Log("ThemeStore legacy lookup failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Every stored override, in no particular order.</summary>
        public static IEnumerable<Snapshot.Record> All()
        {
            return Records().Values;
        }

        /// <summary>
        /// True when a colour sitting in the editor's format map for <paramref name="item"/> is
        /// one we wrote in an earlier session rather than the theme's own.
        ///
        /// This is what stops <see cref="PristineStore"/> from adopting our own colour as the
        /// baseline on installs that were already running before it existed: storage writes are
        /// persistent, so the map a fresh process starts with can already be holding our value,
        /// and the only thing that can tell the two apart is the record of what we wrote.
        ///
        /// Matched on the item name alone. The category is not part of the question — the
        /// format maps are not keyed by it, and the editor category is the only one written.
        /// </summary>
        public static bool WroteChannel(string item, bool isForeground, uint rgb)
        {
            if (string.IsNullOrEmpty(item))
                return false;

            var hex = ColorMath.ToHex(rgb);
            foreach (var record in Records().Values)
            {
                if (!string.Equals(record.Item, item, StringComparison.OrdinalIgnoreCase))
                    continue;

                var stored = isForeground ? record.Foreground : record.Background;
                if (string.Equals(stored, hex, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static int Count { get { return Records().Count; } }

        /// <summary>Records one applied edit. Does not write to disk — call <see cref="Save"/>.</summary>
        public static void Record(ItemViewModel item)
        {
            if (item == null) return;

            var record = new Snapshot.Record
            {
                Category = item.Category,
                Item = item.StorageName,
                Foreground = item.Colors.ForegroundInherited ? null : ColorMath.ToHex(item.Colors.ForegroundRgb),
                Background = item.Colors.BackgroundInherited ? null : ColorMath.ToHex(item.Colors.BackgroundRgb),
                ForegroundCleared = item.Colors.ForegroundCleared,
                BackgroundCleared = item.Colors.BackgroundCleared,
                Bold = item.Colors.Bold
            };

            // Fully inherited and not bold means "the user handed it all back to the theme".
            // Keeping such a row would re-assert the theme default on every start, which is
            // harmless until the user switches themes and wonders why the old one bleeds in.
            //
            // A cleared channel is not that. It is an instruction that has to be re-asserted on
            // every start, because the map the editor rebuilds at startup does not know about it
            // — drop the row and the colour is back after a restart.
            if (Empty(record))
                Records().Remove(Key(record.Category, record.Item));
            else
                Records()[Key(record.Category, record.Item)] = record;

            _dirty = true;
        }

        /// <summary>True when a record says nothing worth re-asserting at the next start.</summary>
        private static bool Empty(Snapshot.Record record)
        {
            return record.Foreground == null
                && record.Background == null
                && !record.ForegroundCleared
                && !record.BackgroundCleared
                && !record.Bold;
        }

        /// <summary>Upserts a batch — a preset or an import — without touching anything else.</summary>
        public static void RecordRange(IEnumerable<Snapshot.Record> records)
        {
            var map = Records();
            foreach (var record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.Item))
                    continue;

                if (Empty(record))
                    map.Remove(Key(record.Category, record.Item));
                else
                    map[Key(record.Category, record.Item)] = record;
            }
            _dirty = true;
        }

        public static void Save()
        {
            if (!_dirty) return;
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    System.IO.Directory.CreateDirectory(Directory);

                File.WriteAllText(
                    CurrentPath,
                    Snapshot.SerializeRecords("current", Records().Values),
                    new UTF8Encoding(false));

                _dirty = false;
                Diag.Log("ThemeStore saved " + Records().Count + " override(s).");
            }
            catch (Exception ex)
            {
                Diag.Log("ThemeStore save FAILED: " + ex.Message);
            }
        }

        /// <summary>Drops every override, so the next start shows the VS theme untouched.</summary>
        public static void Clear()
        {
            Records().Clear();
            _dirty = true;
            Save();
        }

        /// <summary>Replaces the whole store, used when a preset or an import is applied.</summary>
        public static void ReplaceAll(IEnumerable<Snapshot.Record> records)
        {
            var map = Records();
            map.Clear();
            foreach (var record in records)
            {
                if (Empty(record))
                    continue;
                map[Key(record.Category, record.Item)] = record;
            }
            _dirty = true;
            Save();
        }
    }
}
