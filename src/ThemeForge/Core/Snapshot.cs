using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ThemeForge.Core
{
    /// <summary>
    /// Capture/restore for colorable items.
    ///
    /// Fonts &amp; Colors has no undo stack and applying a theme silently overwrites every
    /// item. Taking a full snapshot before the first edit is the only safety net that exists,
    /// and the same serialisation doubles as the share/export format.
    /// </summary>
    internal static class Snapshot
    {
        public const string FileExtension = ".themeforge.json";

        public sealed class Record
        {
            public Guid Category;
            public string Item;
            public string Foreground; // null = inherit from theme
            public string Background;
            public bool Bold;
        }

        public static string Serialize(string name, IEnumerable<ItemViewModel> items)
        {
            return SerializeRecords(name, ToRecords(items));
        }

        /// <summary>Turns live rows into records. The record is the only shape that persists.</summary>
        public static IEnumerable<Record> ToRecords(IEnumerable<ItemViewModel> items)
        {
            foreach (var item in items)
            {
                yield return new Record
                {
                    Category = item.Category,
                    Item = item.StorageName,
                    Foreground = item.Colors.ForegroundInherited ? null : ColorMath.ToHex(item.Colors.ForegroundRgb),
                    Background = item.Colors.BackgroundInherited ? null : ColorMath.ToHex(item.Colors.BackgroundRgb),
                    Bold = item.Colors.Bold
                };
            }
        }

        /// <summary>
        /// The one writer. Export and the persistent store share it so a file written by
        /// either is readable by both — the store *is* an export that reloads itself.
        /// </summary>
        public static string SerializeRecords(string name, IEnumerable<Record> records)
        {
            var w = new Json.Writer();
            w.BeginObject();
            w.Prop("format", "themeforge/1");
            w.Prop("name", name ?? "Untitled");
            w.Prop("created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            w.BeginArray("items");
            foreach (var record in records)
            {
                w.BeginObject();
                w.Prop("category", record.Category.ToString("B"));
                w.Prop("item", record.Item);
                w.Prop("fg", record.Foreground);
                w.Prop("bg", record.Background);
                w.Prop("bold", record.Bold);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        public static List<Record> Deserialize(string text)
        {
            var root = Json.Parse(text);
            var list = new List<Record>();

            var items = root["items"];
            if (items == null || items.Array == null)
                throw new FormatException("Not an XoCrazy file: no 'items' array.");

            foreach (var node in items.Array)
            {
                var categoryText = node["category"] != null ? node["category"].AsString() : null;
                var itemName = node["item"] != null ? node["item"].AsString() : null;
                if (string.IsNullOrEmpty(categoryText) || string.IsNullOrEmpty(itemName))
                    continue;

                Guid category;
                if (!Guid.TryParse(categoryText, out category))
                    continue;

                list.Add(new Record
                {
                    Category = category,
                    Item = itemName,
                    Foreground = node["fg"] != null ? node["fg"].AsString() : null,
                    Background = node["bg"] != null ? node["bg"].AsString() : null,
                    Bold = node["bold"] != null && node["bold"].AsBool(false)
                });
            }
            return list;
        }

        public static void Save(string path, string name, IEnumerable<ItemViewModel> items)
        {
            File.WriteAllText(path, Serialize(name, items), new UTF8Encoding(false));
        }

        public static List<Record> Load(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        /// <summary>Turns a record back into the in-memory colour state for one row.</summary>
        public static ItemColors ToColors(Record record, ItemColors current)
        {
            var result = current != null ? current.Clone() : new ItemColors();

            uint rgb;
            if (record.Foreground == null)
            {
                result.ForegroundInherited = true;
            }
            else if (ColorMath.TryParseHex(record.Foreground, out rgb))
            {
                result.ForegroundRgb = rgb;
                result.ForegroundInherited = false;
            }

            if (record.Background == null)
            {
                result.BackgroundInherited = true;
            }
            else if (ColorMath.TryParseHex(record.Background, out rgb))
            {
                result.BackgroundRgb = rgb;
                result.BackgroundInherited = false;
            }

            result.Bold = record.Bold;
            return result;
        }
    }
}
