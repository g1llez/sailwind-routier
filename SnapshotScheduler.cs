using System;
using UnityEngine;

namespace Routier
{
    internal sealed class SnapshotScheduler : MonoBehaviour
    {
        private int _intervalHours = 1;
        private int _lastCapturedSlot = -1;
        private float _startupDelay = 30f;
        private float _startupTimer;
        private bool _startupAligned;

        private bool _generationEnabled;
        private int _generationHour = 8;
        private GenerationConfig _genConfig;
        private int _lastGeneratedDay = -1;

        public void Configure(float intervalGameHours)
        {
            _intervalHours = Mathf.Max(1, Mathf.RoundToInt(intervalGameHours));
        }

        public void ConfigureGeneration(bool enabled, int hour, GenerationConfig config)
        {
            _generationEnabled = enabled;
            _generationHour = Mathf.Clamp(hour, 0, 23);
            _genConfig = config;
        }

        private void Update()
        {
            if (_startupTimer < _startupDelay)
            {
                _startupTimer += Time.unscaledDeltaTime;
                return;
            }

            if (!_startupAligned)
            {
                if (!Plugin.Instance.CanCapture() || Sun.sun == null)
                    return;
                _lastCapturedSlot = GetHourSlot(GameState.day, Sun.sun.globalTime);
                _startupAligned = true;
                return;
            }

            if (!Plugin.Instance.CanCapture() || Sun.sun == null)
                return;

            TryGenerateRoutes();

            var slot = GetHourSlot(GameState.day, Sun.sun.globalTime);
            if (slot <= _lastCapturedSlot)
                return;

            if (_intervalHours > 1)
            {
                var hour = Mathf.FloorToInt(Sun.sun.globalTime);
                if (hour % _intervalHours != 0)
                    return;
            }

            TryCaptureAndRecord(slot);
        }

        private static int GetHourSlot(int day, float globalTime)
        {
            var hour = Mathf.Clamp(Mathf.FloorToInt(globalTime), 0, 23);
            return day * 24 + hour;
        }

        private void TryGenerateRoutes()
        {
            if (!_generationEnabled)
                return;

            var hour = Mathf.FloorToInt(Sun.sun.globalTime);
            if (GameState.day == _lastGeneratedDay || hour < _generationHour)
                return;

            try
            {
                var snapshotId = MarketCollector.Capture();
                if (snapshotId <= 0)
                    return;
                RouteGenerator.Generate(snapshotId, _genConfig);
                _lastGeneratedDay = GameState.day;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Routier route generation failed: {ex}");
            }
        }

        private bool TryCaptureAndRecord(int slot)
        {
            try
            {
                var snapshotId = MarketCollector.Capture();
                if (snapshotId <= 0)
                    return false;

                _lastCapturedSlot = slot;
                var hour = slot % 24;
                Plugin.Log.LogInfo(
                    $"Routier snapshot #{snapshotId} (day {GameState.day}, {hour:00}:00)");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Routier snapshot failed: {ex}");
                return false;
            }
        }
    }
}
