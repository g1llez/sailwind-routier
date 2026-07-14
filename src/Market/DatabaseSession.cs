using System;
using System.IO;
using BepInEx;

namespace Routier
{
  /// <summary>
  /// Opens one SQLite file per Sailwind save slot ({plugin}/data/routier_slot{N}.db).
  /// Swaps on load / new game; no migration from legacy routier.db.
  /// </summary>
  internal static class DatabaseSession
  {
    private static string _dataDirectory;
    private static string _pathOverride;
    private static int? _activeSlot;
    private static RoutierDatabase _database;
    private static bool _skipNewGameHook;

    internal static RoutierDatabase Active => _database;

    internal static string DataDirectory => _dataDirectory ?? string.Empty;

    internal static void Configure(string configuredPath)
    {
      if (!string.IsNullOrWhiteSpace(configuredPath))
      {
        var full = Path.GetFullPath(configuredPath.Trim());
        if (full.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
          _pathOverride = full;
        else
          _dataDirectory = full;
      }

      if (string.IsNullOrEmpty(_dataDirectory))
      {
        _dataDirectory = Path.Combine(Paths.PluginPath, PluginInfo.Name, "data");
      }

      Directory.CreateDirectory(_dataDirectory);
    }

    internal static string ResolvePathForSlot(int slot)
    {
      if (!string.IsNullOrEmpty(_pathOverride))
        return _pathOverride;

      slot = ClampSlot(slot);
      return Path.Combine(_dataDirectory, $"routier_slot{slot}.db");
    }

    internal static void EnsureForCurrentSlot()
    {
      if (!GameState.playing)
        return;

      OpenForSlot(SaveSlots.currentSlot, fresh: false);
    }

    internal static void OnSaveLoaded()
    {
      _skipNewGameHook = true;
      OpenForSlot(SaveSlots.currentSlot, fresh: false);
      RouteGenerator.ReloadRunSeed();
      RouteParchmentRegistry.LoadFromGameState();
      DailyRouteCatalog.Reset();
      SnapshotScheduler.ResetForSave();
      Plugin.Log.LogInfo(
        $"Routier database for save slot {SaveSlots.currentSlot}: {ResolvePathForSlot(SaveSlots.currentSlot)}");
    }

    internal static void OnNewGameStarted()
    {
      if (_skipNewGameHook)
      {
        _skipNewGameHook = false;
        return;
      }

      var slot = SaveSlots.currentSlot;
      ClearRoutierModData();
      if (UsesManagedSlotDatabase)
      {
        DeleteDatabaseFiles(ResolvePathForSlot(slot));
      }
      else
      {
        Plugin.Log.LogWarning(
          "Routier kept the configured database override on new game; "
          + "only managed routier_slotN.db files are deleted automatically.");
      }
      OpenForSlot(slot, fresh: true);
      RouteGenerator.ReloadRunSeed();
      RouteParchmentRegistry.Clear();
      DailyRouteCatalog.Reset();
      SnapshotScheduler.ResetForSave();
      Plugin.Log.LogInfo(
        $"Routier new game on slot {slot} — fresh database at {ResolvePathForSlot(slot)}");
    }

    private static void OpenForSlot(int slot, bool fresh)
    {
      slot = ClampSlot(slot);
      if (!fresh && _activeSlot == slot && _database != null)
        return;

      _database = null;
      _activeSlot = slot;

      var path = ResolvePathForSlot(slot);
      if (!File.Exists(path) || (fresh && UsesManagedSlotDatabase))
      {
        DeleteDatabaseFiles(path);
      }

      _database = new RoutierDatabase(path);
      _database.Initialize();
    }

    private static void ClearRoutierModData()
    {
      if (GameState.modData == null)
        return;

      var keys = new System.Collections.Generic.List<string>();
      foreach (var key in GameState.modData.Keys)
      {
        if (key.StartsWith("Routier.", StringComparison.Ordinal))
          keys.Add(key);
      }

      foreach (var key in keys)
        GameState.modData.Remove(key);
    }

    private static void DeleteDatabaseFiles(string dbPath)
    {
      foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
      {
        if (!File.Exists(path))
          continue;
        try
        {
          File.Delete(path);
        }
        catch (Exception ex)
        {
          Plugin.Log.LogWarning($"Routier could not delete {path}: {ex.Message}");
        }
      }
    }

    private static bool UsesManagedSlotDatabase => string.IsNullOrEmpty(_pathOverride);

    private static int ClampSlot(int slot)
    {
      if (slot < 0)
        return 0;
      if (slot > 5)
        return 5;
      return slot;
    }
  }
}
