using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Framework.Logging;
using HermesProxy.World.Objects;

namespace HermesProxy.World;

/// <summary>
/// Item templates the legacy server sent, kept across restarts in <see cref="FileName"/>.
/// </summary>
/// <remarks>
/// The modern client caches the Item/ItemSparse hotfixes the proxy builds from a server template.
/// After a proxy restart those records are gone until the server is asked again, so an item the
/// client already knows renders from the baked DB2 instead. Replaying the saved templates at
/// startup rebuilds the hotfixes before any client connects. The file is append-only while
/// running and compacted to one line per item on replay.
/// </remarks>
public static class ItemTemplateCache
{
    private const string FileName = "ItemTemplateCache.jsonl";

    private static readonly object _lock = new();
    private static readonly Dictionary<uint, string> _known = new();

    public static void Remember(ItemTemplate item)
    {
        string json = JsonSerializer.Serialize(item, ItemTemplateCacheJsonContext.Default.ItemTemplate);
        lock (_lock)
        {
            if (_known.TryGetValue(item.Entry, out string? saved) && saved == json)
                return;
            _known[item.Entry] = json;
            try
            {
                File.AppendAllText(FileName, json + "\n");
            }
            catch (IOException ex)
            {
                Log.Print(LogType.Warn, $"Could not save item template {item.Entry} to {FileName}: {ex.Message}");
            }
        }
    }

    /// <returns>How many templates were replayed.</returns>
    public static int Replay()
    {
        if (!File.Exists(FileName))
            return 0;

        var latest = new Dictionary<uint, (ItemTemplate Item, string Json)>();
        foreach (string line in File.ReadLines(FileName))
        {
            if (line.Length == 0)
                continue;
            try
            {
                var item = JsonSerializer.Deserialize(line, ItemTemplateCacheJsonContext.Default.ItemTemplate);
                if (item != null)
                    latest[item.Entry] = (item, line);
            }
            catch (JsonException)
            {
                // A line cut short by a crash; the template comes back from the server.
            }
        }

        lock (_lock)
        {
            _known.Clear();
            var lines = new List<string>(latest.Count);
            foreach (var (entry, (_, json)) in latest)
            {
                _known[entry] = json;
                lines.Add(json);
            }
            try
            {
                File.WriteAllLines(FileName, lines);
            }
            catch (IOException ex)
            {
                Log.Print(LogType.Warn, $"Could not compact {FileName}: {ex.Message}");
            }
        }

        foreach (var (item, _) in latest.Values)
        {
            GameData.GenerateItemUpdateIfNeeded(item);
            GameData.GenerateItemSparseUpdateIfNeeded(item);
        }
        return latest.Count;
    }
}

[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(ItemTemplate))]
internal sealed partial class ItemTemplateCacheJsonContext : JsonSerializerContext;
