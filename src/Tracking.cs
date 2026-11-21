using System;
using System.Reflection;
using DolocTown;
using HarmonyLib;

namespace SomeOptimizations
{
    /// <summary>
    /// "Did the player already do this today?" bookkeeping. The game keeps no per-day gift or
    /// talk record, so the mod remembers it in its own side-car file (see <see cref="ModState"/>):
    /// nothing is written to the save.
    /// </summary>
    internal static class Tracking
    {
        internal static bool GiftedToday(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && ModState.GiftDay(npcId) == GameTime.Today;
        }

        internal static bool TalkedToday(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && ModState.TalkDay(npcId) == GameTime.Today;
        }

        internal static void MarkGift(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || !GameTime.Ready)
            {
                return;
            }

            ModState.SetGift(npcId, GameTime.Today);
        }

        internal static void MarkTalk(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || !GameTime.Ready)
            {
                return;
            }

            ModState.SetTalk(npcId, GameTime.Today);
        }

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(LikingManager), "ReceiveGift"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(Tracking), nameof(ReceiveGiftPostfix))));

            harmony.Patch(
                AccessTools.Method(typeof(DialogueState), "OnExit"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(Tracking), nameof(DialogueExitPrefix))));

            // Talking counts the moment the conversation starts, not when it ends. The merchant
            // shop is opened from inside the dialogue and the state is torn down without going
            // through OnExit while the NPC reference is still set, which is why picking "进入商店"
            // and walking out used to leave the reminder hanging.
            MethodBase enter = AccessTools.Method(typeof(DialogueState), "DialogueWithNpc");
            if (enter != null)
            {
                harmony.Patch(enter,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(Tracking), nameof(DialogueEnterPostfix))));
            }
            else
            {
                Log.Warn("DialogueState.DialogueWithNpc not found; talk is only marked on dialogue exit.");
            }

            // Belt and braces: any shop that belongs to Licca also counts as having met her, even if
            // a future version opens it without the dialogue path above.
            MethodBase openStore = AccessTools.Method(typeof(DolocAPI), "OpenStore");
            if (openStore != null)
            {
                harmony.Patch(openStore,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(Tracking), nameof(OpenStorePostfix))));
            }
        }

        private static void ReceiveGiftPostfix(string __0)
        {
            try
            {
                MarkGift(__0);
            }
            catch
            {
                // bookkeeping only
            }
        }

        private static void DialogueEnterPostfix(string __0)
        {
            try
            {
                MarkTalk(__0);
            }
            catch
            {
                // bookkeeping only
            }
        }

        private static void OpenStorePostfix(string __0)
        {
            try
            {
                if (!string.IsNullOrEmpty(__0) && __0.IndexOf("licca", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MarkTalk("licca");
                }
            }
            catch
            {
                // bookkeeping only
            }
        }

        /// <summary>
        /// <c>DialogueState.OnExit</c> clears the partner reference, so the name has to be read
        /// before the original body runs.
        /// </summary>
        private static void DialogueExitPrefix(DialogueState __instance)
        {
            try
            {
                IDialogueEntity entity = __instance.interactedNpc;
                if (entity != null)
                {
                    MarkTalk(entity.EntityId);
                }
            }
            catch
            {
                // bookkeeping only
            }
        }
    }
}
