using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Routier
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }
        internal static RoutierDatabase Database { get; private set; }
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private ConfigEntry<float> _intervalGameHours;
        private ConfigEntry<string> _databasePath;

        private ConfigEntry<bool> _routesEnabled;
        private ConfigEntry<int> _routesHour;
        private ConfigEntry<int> _routesLocalCount;
        private ConfigEntry<int> _routesRegionalCount;
        private ConfigEntry<float> _routesRoiFloor;
        private ConfigEntry<int> _routesBudgetMin;
        private ConfigEntry<int> _routesBudgetMax;
        private ConfigEntry<int> _routesHopsMin;
        private ConfigEntry<int> _routesHopsMax;
        private ConfigEntry<int> _routesSamples;

        private SnapshotScheduler _scheduler;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _intervalGameHours = Config.Bind(
                "Capture",
                "IntervalGameHours",
                1f,
                "In-game hours between market snapshots while time is advancing.");

            _databasePath = Config.Bind(
                "Storage",
                "DatabasePath",
                "",
                "Optional full path to routier.db. Empty = BepInEx/plugins/Routier/data/routier.db");

            _routesEnabled = Config.Bind("Routes", "Enabled", true,
                "Generate trade routes once per in-game day and log them.");
            _routesHour = Config.Bind("Routes", "GenerationHour", 8,
                "In-game hour (0-23) at which the daily routes are generated.");
            _routesLocalCount = Config.Bind("Routes", "LocalCount", 4,
                "Number of same-region routes offered per hub port.");
            _routesRegionalCount = Config.Bind("Routes", "RegionalCount", 2,
                "Number of cross-region routes offered per hub port.");
            _routesRoiFloor = Config.Bind("Routes", "RoiFloor", 0.20f,
                "Minimum return on invested capital for a route to be offered.");
            _routesBudgetMin = Config.Bind("Routes", "BudgetMin", 2000,
                "Lower bound of the random capital budget used to plan a route.");
            _routesBudgetMax = Config.Bind("Routes", "BudgetMax", 30000,
                "Upper bound of the random capital budget used to plan a route.");
            _routesHopsMin = Config.Bind("Routes", "HopsMin", 3,
                "Minimum number of ports in a route (including the hub).");
            _routesHopsMax = Config.Bind("Routes", "HopsMax", 5,
                "Maximum number of ports in a route (including the hub).");
            _routesSamples = Config.Bind("Routes", "SamplesPerList", 24,
                "Random candidate routes evaluated per list; the most profitable are kept.");

            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly);

                var dbPath = ResolveDatabasePath(_databasePath.Value);
                Database = new RoutierDatabase(dbPath);
                Database.Initialize();

                _scheduler = gameObject.AddComponent<SnapshotScheduler>();
                _scheduler.Configure(_intervalGameHours.Value);
                _scheduler.ConfigureGeneration(
                    _routesEnabled.Value,
                    _routesHour.Value,
                    new GenerationConfig
                    {
                        LocalCount = _routesLocalCount.Value,
                        RegionalCount = _routesRegionalCount.Value,
                        RoiFloor = _routesRoiFloor.Value,
                        BudgetMin = _routesBudgetMin.Value,
                        BudgetMax = _routesBudgetMax.Value,
                        HopsMin = _routesHopsMin.Value,
                        HopsMax = _routesHopsMax.Value,
                        SamplesPerList = _routesSamples.Value,
                    });

                gameObject.AddComponent<HubKioskInstaller>();
                RouteParchmentRegistry.LoadFromGameState();

                Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} — database: {dbPath}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Routier failed to initialize SQLite: {ex}");
            }
        }

        internal static string ResolveDatabasePath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath.Trim());

            var pluginDir = Path.Combine(Paths.PluginPath, PluginInfo.Name, "data");
            Directory.CreateDirectory(pluginDir);
            return Path.Combine(pluginDir, "routier.db");
        }

        internal bool CanCapture()
        {
            if (Database == null)
                return false;
            if (!GameState.playing)
                return false;
            if (GameState.currentlyLoading)
                return false;
            if (Time.timeScale <= 0f)
                return false;
            if (Port.ports == null)
                return false;
            return true;
        }
    }
}
