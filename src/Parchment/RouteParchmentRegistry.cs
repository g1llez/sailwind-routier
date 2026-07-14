using System.Collections.Generic;
using System.Text;

namespace Routier
{
  /// <summary>Persists parchment page text in GameState.modData (keyed by SaveablePrefab.instanceId).</summary>
  internal static class RouteParchmentRegistry
  {
    private const string Key = "Routier.Parchments";
    private static readonly Dictionary<int, string[]> Live = new Dictionary<int, string[]>();

    internal static void Register(int instanceId, string[] pages)
    {
      if (instanceId <= 0 || pages == null || pages.Length == 0)
        return;
      Live[instanceId] = pages;
      Flush();
    }

    internal static bool TryGet(int instanceId, out string[] pages)
    {
      return Live.TryGetValue(instanceId, out pages);
    }

    internal static void LoadFromGameState()
    {
      Live.Clear();
      if (GameState.modData == null || !GameState.modData.TryGetValue(Key, out var raw) || string.IsNullOrEmpty(raw))
        return;
      foreach (var entry in raw.Split('\n'))
      {
        if (string.IsNullOrWhiteSpace(entry))
          continue;
        var sep = entry.IndexOf('|');
        if (sep <= 0)
          continue;
        if (!int.TryParse(entry.Substring(0, sep), out var id))
          continue;
        var body = entry.Substring(sep + 1);
        var parts = body.Split(new[] { "|||" }, System.StringSplitOptions.None);
        Live[id] = parts;
      }
    }

    internal static void Flush()
    {
      if (GameState.modData == null)
        return;
      var sb = new StringBuilder();
      foreach (var kv in Live)
      {
        if (sb.Length > 0)
          sb.Append('\n');
        sb.Append(kv.Key).Append('|').Append(string.Join("|||", kv.Value));
      }
      GameState.modData[Key] = sb.ToString();
    }

    internal static void Clear()
    {
      Live.Clear();
      if (GameState.modData != null)
        GameState.modData.Remove(Key);
    }
  }
}
