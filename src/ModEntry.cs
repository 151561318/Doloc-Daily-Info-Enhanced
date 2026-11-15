using System;
using DTMAPI.Abstractions;
using HarmonyLib;

namespace SomeOptimizations
{
    /// <summary>
    /// DTMAPI entry point. Everything this mod does is additive: Harmony patches are installed at
    /// runtime, no game asset or save field is modified, and removing the mod restores the vanilla
    /// behaviour with no leftovers.
    /// </summary>
    public sealed class ModEntry : DtmMod
    {
        internal const string HarmonyId = "SomeOptimizations";
        internal const string Version = "1.9.4";
        internal static IMonitor Logger;

        private Harmony _harmony;

        public override void Entry(IDtmHelper helper)
        {
            Logger = helper.Monitor;
            try
            {
                ModConfig.Bind(helper);

                ModState.Init(helper.Config.GetConfigPath(Manifest));
                Layout.LoadSaved();
                Layout.Changed += HudBadges.Invalidate;
                EditorUi.Init(helper.Input);

                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                Tracking.Install(_harmony);
                ResinPriority.Install(_harmony);

                helper.Events.GameLoop.GameLaunched += OnGameLaunched;
                helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

                Log.Info("loaded v" + Version);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Entry failed");
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            Log.Info("game launched");
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            try
            {
                CropScan.Update();
                HudBadges.Update();
                CalendarInject.Tick();
                EditorUi.Update();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "UpdateTicked");
            }
        }
    }

    internal static class Log
    {
        internal static void Info(string message)
        {
            ModEntry.Logger?.Log("[SomeOptimizations] " + message, LogLevel.Info);
        }

        internal static void Warn(string message)
        {
            ModEntry.Logger?.Log("[SomeOptimizations] " + message, LogLevel.Warn);
        }

        internal static void Error(string message)
        {
            ModEntry.Logger?.Log("[SomeOptimizations] " + message, LogLevel.Error);
        }

        internal static void Exception(Exception ex, string where)
        {
            ModEntry.Logger?.LogException(ex, "[SomeOptimizations] " + where);
        }
    }
}

