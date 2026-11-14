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
