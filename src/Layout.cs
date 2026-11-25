using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SomeOptimizations
{
    /// <summary>
    /// Every pixel value the mod draws with, in one flat list that the F8 editor walks through.
    /// The values are persisted by <see cref="ModState"/> in the mod's own side-car file, so a
    /// tuned layout survives restarts without touching the save.
    /// </summary>
    internal static class Layout
    {
        internal sealed class Item
        {
            internal string Key;
            internal string Label;
            internal float Min;
            internal float Max;
            internal float Step;
            internal float Default;
            internal float Value;

            internal float Get()
            {
                return Value;
            }

            internal void Set(float value)
            {
                Value = Mathf.Clamp(value, Min, Max);
            }
        }

        private static readonly List<Item> s_items = new List<Item>();
        private static readonly Dictionary<string, Item> s_byKey = new Dictionary<string, Item>();

        internal static event Action Changed;

        static Layout()
        {
            Add("hud.icon", "时钟下图标大小", 66f, 16f, 200f, 1f);
            Add("hud.spacing", "图标间距", 4f, 0f, 40f, 1f);
            Add("hud.gapY", "图标距时钟高度", 6f, -40f, 80f, 1f);
            Add("hud.offsetX", "图标横向偏移", -8f, -300f, 300f, 1f);
            Add("hud.heart.size", "爱心大小", 20f, 4f, 60f, 1f);
            Add("hud.heart.offX", "爱心横向偏移", -4f, -40f, 40f, 1f);
            Add("hud.heart.offY", "爱心纵向偏移", 5f, -40f, 40f, 1f);
            Add("hud.glow.scale", "高光范围", 1.8f, 0.5f, 4f, 0.05f);
            Add("hud.glow.alpha", "高光强度", 1f, 0f, 1f, 0.05f);
            Add("hud.border.width", "边框高光粗细", 3f, 1f, 12f, 1f);
            Add("hud.border.spread", "边框高光外扩", 2f, 0f, 16f, 1f);
            Add("cal.icon", "日历图标大小", 60f, 12f, 80f, 1f);
            Add("cal.offX", "日历图标横向偏移", 38f, -80f, 80f, 1f);
            Add("cal.offY", "日历图标纵向偏移", -40f, -80f, 80f, 1f);
            Add("cal.festival", "有节日时上抬距离", 49f, 0f, 120f, 1f);
            Add("cal.star.offX", "日历 +N 横向微调", 11f, -40f, 40f, 1f);
            Add("cal.star.offY", "日历 +N 纵向微调", -25f, -40f, 40f, 1f);
            Add("cal.star", "+N 字号", 20f, 8f, 32f, 1f);
            Add("cal.margin", "日历图标与格子边框的间距", 4f, 0f, 20f, 1f);
            Add("cal.rows.extra", "特殊事件行数额外加几条", 2f, -2f, 6f, 1f);
            Add("hud.plus.size", "+N 字号(时钟下方)", 20f, 8f, 32f, 1f);
            Add("hud.plus.offX", "+N 横向微调", 11f, -40f, 40f, 1f);
            Add("hud.plus.offY", "+N 纵向微调", 6f, -40f, 40f, 1f);
            Add("hud.portrait.scale", "时钟下方头像缩放(裁边后再缩放)", 1.2f, 0.5f, 2.5f, 0.05f);
            Add("hud.portrait.offX", "头像横向微调", 17f, -30f, 30f, 1f);
            Add("hud.portrait.offY", "头像纵向微调", 15f, -30f, 30f, 1f);
            Add("hud.icon.margin", "头像与背景板的最小间距", 2f, 0f, 12f, 1f);

            // Player birthday portrait. Kept separate from the NPC portrait values above: those were
            // tuned for calendar portraits packed with wide transparent margins, while the player
            // sprite is a tight 24x30 rig frame that fills the plate differently.
            // Defaults are the values the author settled on in the F8 editor (zoom 3.05, y -2).
            Add("hud.player.scale", "玩家头像缩放", 3.05f, 0.3f, 6f, 0.05f);
            Add("hud.player.offX", "玩家头像横向微调", 0f, -30f, 30f, 1f);
            Add("hud.player.offY", "玩家头像纵向微调", -2f, -30f, 30f, 1f);
            Add("hud.player.margin", "玩家头像与背景板的最小间距", 1f, 0f, 12f, 1f);
        }

        internal static List<Item> Items => s_items;

        private static void Add(string key, string label, float value, float min, float max, float step)
        {
            Item item = new Item
            {
                Key = key,
                Label = label,
                Min = min,
                Max = max,
                Step = step,
                Default = value,
                Value = value
            };

            s_items.Add(item);
            s_byKey[key] = item;
        }

        private static float Val(string key)
        {
            return s_byKey.TryGetValue(key, out Item item) ? item.Value : 0f;
        }

        internal static float HudIcon => Val("hud.icon");
        internal static float HudSpacing => Val("hud.spacing");
        internal static float HudGapY => Val("hud.gapY");
        internal static float HudOffsetX => Val("hud.offsetX");
        internal static float HudPlusSize => Val("hud.plus.size");
        internal static float HudPlusOffX => Val("hud.plus.offX");
        internal static float HudPlusOffY => Val("hud.plus.offY");
        internal static float HudHeartSize => Val("hud.heart.size");
        internal static float HudHeartOffX => Val("hud.heart.offX");
        internal static float HudHeartOffY => Val("hud.heart.offY");
        internal static float GlowScale => Val("hud.glow.scale");
        internal static float GlowAlpha => Val("hud.glow.alpha");
        internal static float CalIcon => Val("cal.icon");
        internal static float CalOffX => Val("cal.offX");
        internal static float CalOffY => Val("cal.offY");
        internal static float CalAlongFestival => Val("cal.festival");
        internal static float CalStarSize => Val("cal.star");
        internal static float CalStarOffX => Val("cal.star.offX");
        internal static float CalStarOffY => Val("cal.star.offY");
        internal static float CalMargin => Val("cal.margin");
        internal static float CalRowsExtra => Val("cal.rows.extra");
        internal static float HudPortraitScale => Val("hud.portrait.scale");
        internal static float HudPortraitOffX => Val("hud.portrait.offX");
        internal static float HudPortraitOffY => Val("hud.portrait.offY");
        internal static float HudIconMargin => Val("hud.icon.margin");
        internal static float PlayerPortraitScale => Val("hud.player.scale");
        internal static float PlayerPortraitOffX => Val("hud.player.offX");
        internal static float PlayerPortraitOffY => Val("hud.player.offY");
        internal static float PlayerIconMargin => Val("hud.player.margin");
        internal static float HudBorderWidth => Val("hud.border.width");
        internal static float HudBorderSpread => Val("hud.border.spread");

        /// <summary>Restores whatever the side-car file stored (called once the file is loaded).</summary>
        internal static void LoadSaved()
        {
            ModState.ApplyLayout(s_items);
        }

        // -------------------------------------------------------------------------------------
        // Per-crop nudges: every plant sprite has its own transparent margin and its own shape, so
        // a single global offset can never line them all up. Seed id -> extra pixel offset inside
        // the calendar cell, applied after the global offset and clamp.
        // -------------------------------------------------------------------------------------
        private static readonly Dictionary<string, Vector2> s_cropOffsets =
            new Dictionary<string, Vector2>(CropDefaults.Offsets);

        internal static Vector2 CropOffset(string seedId)
        {
            if (!string.IsNullOrEmpty(seedId) && s_cropOffsets.TryGetValue(seedId, out Vector2 v))
            {
                return v;
            }

            return Vector2.zero;
        }

        internal static bool HasCropOffset(string seedId)
        {
            return !string.IsNullOrEmpty(seedId) && s_cropOffsets.ContainsKey(seedId);
        }

        internal static void NudgeCrop(string seedId, float dx, float dy)
        {
            if (string.IsNullOrEmpty(seedId))
            {
                return;
            }

            Vector2 current = CropOffset(seedId);
            Vector2 next = new Vector2(
                Mathf.Clamp(current.x + dx, -120f, 120f),
                Mathf.Clamp(current.y + dy, -120f, 120f));
            s_cropOffsets[seedId] = next;
            RaiseChanged();
        }

        internal static void ResetCrop(string seedId)
        {
            if (!string.IsNullOrEmpty(seedId))
            {
                s_cropOffsets.Remove(seedId);
                RaiseChanged();
            }
        }

        internal static Dictionary<string, string> CaptureCropOffsets()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (KeyValuePair<string, Vector2> pair in s_cropOffsets)
            {
                if (pair.Value != Vector2.zero)
                {
                    result[pair.Key] = Format(pair.Value.x) + "," + Format(pair.Value.y);
                }
            }

            return result;
        }

        internal static void ApplyCropOffsets(Dictionary<string, string> stored)
        {
            s_cropOffsets.Clear();
            if (stored == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> pair in stored)
            {
                string[] parts = (pair.Value ?? string.Empty).Split(',');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    s_cropOffsets[pair.Key] = new Vector2(x, y);
                }
            }
        }

        internal static void SaveCurrent()
        {
            ModState.CaptureLayout(s_items);
        }

        internal static void ResetDefaults()
        {
            for (int i = 0; i < s_items.Count; i++)
            {
                s_items[i].Value = s_items[i].Default;
            }

            RaiseChanged();
        }

        internal static void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Layout.RaiseChanged");
            }
        }

        internal static string Format(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        internal static float Round(float value)
        {
            return Mathf.Round(value * 100f) / 100f;
        }
    }
}
