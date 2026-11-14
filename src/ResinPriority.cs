using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using DolocTown;
using HarmonyLib;

namespace SomeOptimizations
{
    /// <summary>
    /// The resin collector and a neighbouring plant often overlap. An empty collector has nothing
    /// to give, so it yields the interaction to whatever else is under the cursor; a collector
    /// that has actually collected resin keeps the vanilla behaviour.
    /// </summary>
    internal static class ResinPriority
    {
        private static readonly ConditionalWeakTable<Equipment, Wrapper> s_cache =
            new ConditionalWeakTable<Equipment, Wrapper>();

        internal static void Install(Harmony harmony)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Equipment), "Interactable");
            if (getter == null)
            {
                Log.Warn("Equipment.Interactable getter not found; resin priority tweak disabled.");
                return;
            }

            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(ResinPriority), nameof(Postfix)));
        }

        private static void Postfix(Equipment __instance, ref IInteractable __result)
        {
            try
            {
                if (__result == null || ModConfig.RestoreResinCollectorPriority)
                {
                    return;
                }

                if (!(__instance is ResinCollector collector) || collector.IsGatherable)
                {
                    return;
                }

                if (!s_cache.TryGetValue(__instance, out Wrapper wrapper))
                {
                    wrapper = new Wrapper();
                    s_cache.Add(__instance, wrapper);
                }

                wrapper.Original = __result;
                __result = wrapper;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "ResinPriority");
            }
        }

        /// <summary>
        /// Reference stable shim: the interaction manager remembers the object it touched, so one
        /// instance has to be handed out per collector for its whole life.
        /// </summary>
        private sealed class Wrapper : IInteractable
        {
            internal IInteractable Original;

            public int Priority => -1;

            public int InteractableLayer => Original != null ? Original.InteractableLayer : 0;

            public bool OnlyTouch => Original != null && Original.OnlyTouch;

            public bool CanInteractContinues => Original != null && Original.CanInteractContinues;

            public void OnTouch()
            {
                Original?.OnTouch();
            }

            public void OnDisTouch()
            {
                Original?.OnDisTouch();
            }

            public void OnInteract()
            {
                Original?.OnInteract();
            }
        }
    }
}
