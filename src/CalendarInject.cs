using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DolocTown;
using DolocTown.Config;
using DolocTown.UI;
using HarmonyLib;
using RedSaw;
using UnityEngine;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>Extra crop rows inside the calendar panel and the per-day crop icon on the grid.</summary>
    internal static class CalendarInject
    {
        private const string IconName = "SomeOptCropIcon";
        private const string StarName = "SomeOptCropStar";
        internal const string RefBoxName = "SomeOptRefBox";

        /// <summary>The per-cell festival banner image (used by the preview to fake a festival).</summary>
        internal static Image FestivalOf(DateSlot slot)
        {
            return slot == null || FestivalField == null ? null : FestivalField.GetValue(slot) as Image;
        }

        private static readonly FieldInfo FestivalField = AccessTools.Field(typeof(DateSlot), "festival");
        private static readonly FieldInfo PoolField = AccessTools.Field(typeof(CalendarViewer), "_slotPool");
        private static readonly FieldInfo EventRootField = AccessTools.Field(typeof(CalendarViewer), "eventRoot");
        private static readonly ConditionalWeakTable<DateSlot, SlotVisual> Visuals =
            new ConditionalWeakTable<DateSlot, SlotVisual>();

        private static readonly List<DateSlot> LiveSlots = new List<DateSlot>();
        private static float s_cellHalf;

        static CalendarInject()
        {
            Layout.Changed += RefreshLayout;
        }

        /// <summary>Re-applies the tuned sizes/offsets while the calendar is already open.</summary>
        internal static void RefreshLayout()
        {
            try
            {
                for (int i = 0; i < LiveSlots.Count; i++)
                {
                    DateSlot slot = LiveSlots[i];
                    if (slot == null)
                    {
                        continue;
                    }

                    if (Visuals.TryGetValue(slot, out SlotVisual visual) && visual != null && visual.Icon != null)
                    {
                        ApplyIconLayout(slot, visual);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CalendarInject.RefreshLayout");
            }
        }

        private static int s_slotLogs;
        private static int s_rowLogs;
        private static float s_nextTick;
        private static float s_lastRender;

        /// <summary>
        /// While the calendar is on screen, re-apply the icon layout a couple of times per second.
        /// The grid recycles its slots, and decorations/festival images can become active only after
        /// the first render pass, so a one-shot placement is not enough.
        /// </summary>
        internal static void Tick()
        {
            if (LiveSlots.Count == 0 || Time.unscaledTime < s_nextTick)
            {
                return;
            }

            // Only chase layout for a few seconds after the grid was drawn: that is the window in
            // which recycled slots and festival decorations finish activating.
            if (Time.unscaledTime - s_lastRender > 3f)
            {
                return;
            }

            s_nextTick = Time.unscaledTime + 0.5f;
            RefreshLayout();
        }

        private sealed class SlotVisual
        {
            internal Image Icon;
            internal Text Plus;
            internal Image RefBox;
            internal string SeedId;
        }

        internal static void RenderSlot(DateSlot slot, DateData data)
        {
            try
            {
                s_lastRender = Time.unscaledTime;

                if (slot == null || data.time <= 0)
                {
                    return;
                }

                SlotVisual visual = GetVisual(slot);
                if (visual != null)
                {
                    PruneDuplicates(slot.rectTransform, visual.Icon);
                }

                if (visual == null)
                {
                    if (Diagnostics.Enabled && s_slotLogs < 12)
                    {
                        s_slotLogs++;
                        Log.Warn("diag slot day=" + data.time + " visual=NULL festivalField=" +
                                 (FestivalField == null ? "missing" : (FestivalField.GetValue(slot) == null ? "null-value" : "ok")));
                    }

                    return;
                }

                if (!LiveSlots.Contains(slot))
                {
                    LiveSlots.Add(slot);
                }

                // In the preview page the grid always shows the fake crops, whose seeds rotate so every
                // cell can be nudged individually.
                List<CropBatch> batches = CropScan.BatchesOfDay(GameTime.DayOfMonthToAbsDay(data.time));

                if (batches == null || batches.Count == 0)
                {
                    visual.Icon.gameObject.SetActive(false);
                    if (visual.Plus != null)
                    {
                        visual.Plus.gameObject.SetActive(false);
                    }

                    if (visual.RefBox != null)
                    {
                        visual.RefBox.gameObject.SetActive(false);
                    }

                    Transform legacy = visual.Icon.transform.Find(RefBoxName);
                    if (legacy != null)
                    {
                        legacy.gameObject.SetActive(false);
                    }

                    return;
                }

                visual.Icon.gameObject.SetActive(true);
                visual.Icon.sprite = batches[0].Icon;
                visual.Icon.enabled = batches[0].Icon != null;
                visual.SeedId = batches[0].SeedId;

                ApplyIconLayout(slot, visual);

                if (Diagnostics.Enabled && s_slotLogs < 6)
                {
                    s_slotLogs++;
                    RectTransform ir = visual.Icon.rectTransform;
                    Log.Info("diag slot day=" + data.time + " abs=" + GameTime.DayOfMonthToAbsDay(data.time) +
                             " batches=" + batches.Count + " seed=" + batches[0].SeedId +
                             " sprite=" + (batches[0].Icon != null ? batches[0].Icon.name : "NULL") +
                             " iconActive=" + visual.Icon.gameObject.activeInHierarchy +
                             " size=" + ir.rect.size + " pos=" + ir.anchoredPosition +
                             " parentActive=" + (ir.parent != null && ir.parent.gameObject.activeInHierarchy));
                    DumpTree(slot.rectTransform, 0, 3);
                }

                HashSet<string> seeds = new HashSet<string>();
                for (int i = 0; i < batches.Count; i++)
                {
                    seeds.Add(batches[i].SeedId);
                }

                int extraKinds = seeds.Count - 1;
                if (visual.Plus != null)
                {
                    if (extraKinds > 0)
                    {
                        visual.Plus.text = "+" + extraKinds;
                        visual.Plus.gameObject.SetActive(true);
                    }
                    else
                    {
                        visual.Plus.gameObject.SetActive(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CalendarInject.RenderSlot");
            }
        }

        /// <summary>
        /// Places the crop icon in the cell's lower right corner; when the day also shows a festival
        /// decoration the crop icon is lifted above it instead of covering it.
        /// </summary>
        private static void ApplyIconLayout(DateSlot slot, SlotVisual visual)
        {
            Image festival = FestivalField?.GetValue(slot) as Image;

            // activeSelf, not activeInHierarchy: on the first render pass the grid slots are still
            // inactive, which made the festival lift miss and the crop icon cover the decoration.
            bool hasFestival = festival != null && festival.gameObject.activeSelf && festival.enabled &&
                               festival.sprite != null;

            PlaceCropIcon(slot.rectTransform, visual.Icon, visual.Plus, visual.SeedId, hasFestival,
                CellHalf(slot));

            if (ModConfig.DemoBadges && visual.Icon.gameObject.activeSelf)
            {
                if (visual.RefBox == null)
                {
                    Transform existingBox = slot.rectTransform.Find(RefBoxName);
                    if (existingBox != null)
                    {
                        visual.RefBox = existingBox.GetComponent<Image>();
                    }

                    if (visual.RefBox == null)
                    {
                        visual.RefBox = UiFactory.Create(slot.rectTransform, RefBoxName, ProcSprites.Frame,
                            new Color(1f, 0.85f, 0.2f, 0.8f)).GetComponent<Image>();
                        visual.RefBox.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                        visual.RefBox.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        visual.RefBox.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                        visual.RefBox.transform.SetAsLastSibling();
                    }
                }

                visual.RefBox.gameObject.SetActive(true);
                UiFactory.Size(visual.RefBox.rectTransform, Layout.CalIcon, Layout.CalIcon);
                visual.RefBox.rectTransform.anchoredPosition = visual.Icon.rectTransform.anchoredPosition;
            }
            else if (visual.RefBox != null)
            {
                visual.RefBox.gameObject.SetActive(false);
            }

            // The NPC portrait keeps priority: draw it above the crop icon when both share a cell.
            Transform npc = slot.rectTransform.Find("npc_icon");
            if (npc != null && npc.GetSiblingIndex() != slot.rectTransform.childCount - 1)
            {
                npc.SetAsLastSibling();
            }
        }

        /// <summary>
        /// Shared placement used by the real calendar slot and the F8 preview grid, so what is tuned
        /// in the preview is exactly what the calendar draws.
        /// </summary>
        internal static void PlaceCropIcon(RectTransform parent, Image icon, Text plus, string seedId,
            bool hasFestival, float cellHalf)
        {
            if (icon == null || parent == null)
            {
                return;
            }

            float lift = hasFestival ? Layout.CalAlongFestival : 0f;

            // Plant sprites are packed with transparent margins; fit the visible pixels so every
            // crop looks the same size in the grid.
            if (icon.sprite != null)
            {
                IconFit.Fit(icon, Layout.CalIcon, IconFit.Align.BottomRight);
            }

            RectTransform iconRect = icon.rectTransform;
            Vector2 pos = new Vector2(Layout.CalOffX, Layout.CalOffY + lift);

            // Keep a small gap to the cell border: the grid cell is 142x142 centred on the slot
            // origin, so clamp the visible (ink) box inside it. IconFit scales the sprite so its
            // ink is exactly CalIcon wide, so that is the size to clamp with, not the padded rect.
            float margin = Layout.CalMargin;
            float halfW = Layout.CalIcon * 0.5f;
            float maxX = Mathf.Max(0f, cellHalf - halfW - margin);
            float maxY = maxX;
            pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
            pos.y = Mathf.Clamp(pos.y, -maxY, maxY);

            // IconFit already moved the rect so the ink lands in its box; that offset has to be
            // kept, otherwise the sprite sits wherever the padded rect was placed. The per-crop
            // nudge is added after the clamp so a single stubborn sprite can be pushed anywhere.
            Vector2 nudge = Layout.CropOffset(seedId);
            iconRect.anchoredPosition += pos + nudge;
            pos = iconRect.anchoredPosition;

            // "+N" sits on the top-right corner of the icon's effective (ink) box, 2 units away.
            if (plus != null)
            {
                RectTransform plusRect = plus.rectTransform;
                plusRect.anchorMin = new Vector2(0.5f, 0.5f);
                plusRect.anchorMax = new Vector2(0.5f, 0.5f);
                plusRect.pivot = new Vector2(0f, 0f);
                plusRect.sizeDelta = new Vector2(Layout.CalStarSize * 2f, Layout.CalStarSize * 1.6f);
                float inkHalf = Layout.CalIcon * 0.5f;
                Vector2 plusPos = new Vector2(inkHalf + 2f, inkHalf + 2f);

                // Keep the label inside the cell: the pivot is the bottom-left corner, so the box
                // grows up-right and must be clamped by its own size, in cell space (the label is
                // a child of the icon, which is itself offset inside the cell).
                float labelW = plusRect.sizeDelta.x;
                float labelH = plusRect.sizeDelta.y;
                float limit = cellHalf - Layout.CalMargin;
                plusPos.x = Mathf.Min(plusPos.x, limit - labelW - pos.x);
                plusPos.y = Mathf.Min(plusPos.y, limit - labelH - pos.y);
                plusPos.x = Mathf.Max(plusPos.x, -limit - pos.x);
                plusPos.y = Mathf.Max(plusPos.y, -limit - pos.y);

                // The user offsets are applied after the clamp, otherwise they would be swallowed.
                plusRect.anchoredPosition = new Vector2(
                    plusPos.x + Layout.CalStarOffX,
                    plusPos.y + Layout.CalStarOffY);
                plus.fontSize = Mathf.RoundToInt(Layout.CalStarSize);
            }
        }

        /// <summary>Half the side of the visible date cell (142 px by default).</summary>
        private static float CellHalf(DateSlot slot)
        {
            if (s_cellHalf > 0f)
            {
                return s_cellHalf;
            }

            RectTransform slotRect = slot.rectTransform;
            Transform border = slotRect.Find("border");
            if (border is RectTransform br && br.rect.width > 1f)
            {
                s_cellHalf = br.rect.width * 0.5f;
                return s_cellHalf;
            }

            for (int i = 0; i < slotRect.childCount; i++)
            {
                if (slotRect.GetChild(i) is RectTransform child && child.rect.width > 100f)
                {
                    s_cellHalf = child.rect.width * 0.5f;
                    return s_cellHalf;
                }
            }

            return 71f;
        }

        internal static void AppendRows(CalendarViewer viewer, DateEventData data)
        {
            try
            {
                if (viewer == null || data.time <= 0)
                {
                    return;
                }

                ObjectPool<DolocTown.UI.DateEventInfo> pool =
                    PoolField?.GetValue(viewer) as ObjectPool<DolocTown.UI.DateEventInfo>;
                if (pool == null)
                {
                    return;
                }

                List<CropBatch> batches = CropScan.BatchesOfDay(GameTime.DayOfMonthToAbsDay(data.time));
                if (batches == null || batches.Count == 0)
                {
                    return;
                }

                // When vanilla has nothing to show it renders a single "（无）" hint row; that row is
                // reused for the first crop line so the panel does not read "none" above real news.
                int start = pool.ActiveCount;
                if (start == 1 && data.eventsTitle != null && data.eventsTitle.Length == 1 &&
                    data.eventsTitle[0] == DolocConfig.StaticTexts.CalendarPanelEmptyHint)
                {
                    start = 0;
                }

                // The event list has no scroll view, so a long day would push the memo block off the
                // panel. Ask the live layout how many extra lines actually fit instead of guessing.
                int fit = RowsThatFit(viewer, pool, start) + Mathf.RoundToInt(Layout.CalRowsExtra);
                fit = Mathf.Clamp(fit, 0, 9);
                int shown = Mathf.Min(batches.Count, fit);
                int extra = batches.Count - shown;
                if (extra > 0 && shown > 0)
                {
                    // The summary line needs a slot of its own.
                    shown--;
                    extra = batches.Count - shown;
                }

                int rows = shown + (extra > 0 ? 1 : 0);
                if (rows <= 0)
                {
                    return;
                }

                pool.CheckCount(start + rows);

                if (Diagnostics.Enabled && s_rowLogs < 4)
                {
                    s_rowLogs++;
                    Log.Info("diag rows day=" + data.time + " vanilla=" + start + " crop=" + batches.Count +
                             " shown=" + shown + " active=" + pool.ActiveCount);
                }

                for (int i = 0; i < shown; i++)
                {
                    pool[start + i].Render(Title(batches[i]), string.Empty);
                }

                if (extra > 0)
                {
                    pool[start + shown].Render("…另有 " + extra + " 批作物在今日成熟", string.Empty);
                }

                // The pool recycles slots in its own order, so the on-screen order can differ from
                // the index order; force the hierarchy to match what was just rendered.
                for (int i = 0; i < pool.ActiveCount; i++)
                {
                    DolocTown.UI.DateEventInfo slot = pool[i];
                    if (slot != null)
                    {
                        slot.transform.SetSiblingIndex(i);
                    }
                }

                viewer.RebuildLayout();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CalendarInject.AppendRows");
            }
        }

        /// <summary>
        /// How many crop lines still fit under the vanilla rows without pushing the memo block.
        /// Falls back to a conservative constant when the panel has not been laid out yet.
        /// </summary>
        private static int RowsThatFit(CalendarViewer viewer, ObjectPool<DolocTown.UI.DateEventInfo> pool,
            int vanillaCount)
        {
            const int Fallback = 4;
            try
            {
                RectTransform root = EventRootField?.GetValue(viewer) as RectTransform;
                if (root == null)
                {
                    return Fallback;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(root);

                float available = root.rect.height;
                if (available <= 1f)
                {
                    return Fallback;
                }

                float spacing = 0f;
                VerticalLayoutGroup group = root.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    spacing = group.spacing;
                }

                float used = 0f;
                float lastHeight = 0f;
                int active = 0;
                for (int i = 0; i < pool.ActiveCount; i++)
                {
                    DolocTown.UI.DateEventInfo slot = pool[i];
                    if (slot == null || !slot.gameObject.activeSelf)
                    {
                        continue;
                    }

                    float h = slot.transform is RectTransform r ? r.rect.height : 0f;
                    if (h <= 1f)
                    {
                        continue;
                    }

                    used += h;
                    lastHeight = h;
                    active++;
                }

                if (active > 0)
                {
                    used += spacing * (active - 1);
                }

                if (lastHeight <= 1f)
                {
                    return Fallback;
                }

                float room = available - used;
                int fit = Mathf.FloorToInt(room / (lastHeight + spacing));

                if (Diagnostics.Enabled && s_rowLogs < 6)
                {
                    s_rowLogs++;
                    Log.Info("diag fit avail=" + available.ToString("0.#") + " used=" + used.ToString("0.#") +
                             " row=" + lastHeight.ToString("0.#") + " spacing=" + spacing.ToString("0.#") +
                             " vanilla=" + vanillaCount + " fit=" + fit);
                }

                return Mathf.Clamp(fit, 0, 8);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CalendarInject.RowsThatFit");
                return Fallback;
            }
        }

        private static string Title(CropBatch batch)
        {
            if (batch.RipeCarry)
            {
                return batch.Name + "*" + batch.Count + "已成熟待收割";
            }

            if (batch.Ripe >= batch.Count)
            {
                return batch.Name + "*" + batch.Count + "已成熟待收割";
            }

            string first = GameTime.FormatMinute(CropScan.MinuteOfDay(batch), ModConfig.ShowExactTime);
            string last = GameTime.FormatMinute(batch.LastExact - batch.AbsDay * GameTime.MinutesPerDay,
                ModConfig.ShowExactTime);
            string when = first == last ? "（" + first + "）" : "（" + first + "~" + last + "）";
            return batch.Name + "*" + batch.Count + when + "成熟";
        }

        /// <summary>Diagnostics only: prints the slot hierarchy so icon anchoring can be solved once.</summary>
        private static void DumpTree(RectTransform rect, int depth, int maxDepth)
        {
            Log.Info("diag tree " + new string(' ', depth) + rect.name + " rect=" + rect.rect +
                     " anchor=" + rect.anchorMin + " pivot=" + rect.pivot +
                     " pos=" + rect.anchoredPosition + " active=" + rect.gameObject.activeSelf);

            if (depth >= maxDepth)
            {
                return;
            }

            for (int i = 0; i < rect.childCount; i++)
            {
                RectTransform child = rect.GetChild(i) as RectTransform;
                if (child != null)
                {
                    DumpTree(child, depth + 1, maxDepth);
                }
            }
        }

        /// <summary>
        /// Slots are pooled and re-rendered a lot; if a clone ever slips through the name lookup we
        /// used to end up with two crop icons in one cell. Kill anything that is not the tracked one.
        /// </summary>
        private static void PruneDuplicates(RectTransform slotRect, Image keep)
        {
            if (slotRect == null || keep == null)
            {
                return;
            }

            for (int i = slotRect.childCount - 1; i >= 0; i--)
            {
                Transform child = slotRect.GetChild(i);
                if (child == null || child.name != IconName)
                {
                    continue;
                }

                Image image = child.GetComponent<Image>();
                if (image != null && image != keep)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static SlotVisual GetVisual(DateSlot slot)
        {
            if (Visuals.TryGetValue(slot, out SlotVisual cached) && cached.Icon != null)
            {
                return cached;
            }

            Image festival = FestivalField?.GetValue(slot) as Image;
            if (festival == null)
            {
                return null;
            }

            // The festival image lives inside a container that vanilla only enables on festival
            // days, so the crop icon must hang off the always-enabled day slot itself.
            RectTransform slotRect = slot.rectTransform;
            Transform existing = slotRect.Find(IconName);
            Image icon;
            if (existing != null)
            {
                icon = existing.GetComponent<Image>();
                if (icon == null)
                {
                    return null;
                }
            }
            else
            {
                GameObject clone = UnityEngine.Object.Instantiate(festival.gameObject, slotRect);
                clone.name = IconName;
                clone.SetActive(true);
                icon = clone.GetComponent<Image>();
                if (icon == null)
                {
                    return null;
                }

                RectTransform iconRect = icon.rectTransform;

                // The slot rect itself is zero sized (children are anchored to its origin), so the
                // crop icon is placed with plain offsets in the cell's lower right, just above the
                // vanilla festival decoration.
                iconRect.SetParent(slotRect, false);
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(Layout.CalIcon, Layout.CalIcon);
                iconRect.anchoredPosition = new Vector2(Layout.CalOffX, Layout.CalOffY);
                iconRect.SetAsLastSibling();
            }

            SlotVisual visual = new SlotVisual { Icon = icon };

            Transform existingPlus = icon.transform.Find(StarName);
            if (existingPlus != null)
            {
                visual.Plus = existingPlus.GetComponent<Text>();
            }

            if (visual.Plus == null)
            {
                // The marker is a "+N" label (N = extra crop kinds) instead of the old star, which
                // read like a festival/decoration badge.
                Text time = slotRect.Find("time") != null ? slotRect.Find("time").GetComponent<Text>() : null;
                Font font = HudBadges.TipFont();
                visual.Plus = UiFactory.CreateText(icon.transform, StarName, font,
                    Mathf.RoundToInt(Layout.CalStarSize), new Color(1f, 0.9f, 0.25f, 1f));
                visual.Plus.transform.SetAsLastSibling();
            }

            // The demo reference box is created lazily by ApplyIconLayout: only slots that actually
            // draw a crop may own one, otherwise recycled slots leave empty frames behind.
            Transform stray = icon.transform.Find(RefBoxName);
            if (stray != null)
            {
                UnityEngine.Object.Destroy(stray.gameObject);
            }

            Visuals.AddOrUpdate(slot, visual);
            return visual;
        }

        /// <summary>Helper that turns the absolute day counter back into a shown month/day label.</summary>
        private struct DateInfoHolder
        {
            internal int Month;
            internal int Day;
            internal string Season;

            internal static DateInfoHolder From(int absDay)
            {
                DateInfoHolder holder = default;
                try
                {
                    int month = GameTime.Now.Month;
                    int day = GameTime.Now.Day + (absDay - GameTime.Today);
                    int monthDays = DolocAPI.GlobalParameter.Month2Day;
                    while (day > monthDays)
                    {
                        day -= monthDays;
                        month++;
                    }

                    while (day < 1)
                    {
                        month--;
                        day += monthDays;
                    }

                    int yearMonths = DolocAPI.GlobalParameter.Year2Month;
                    while (month > yearMonths)
                    {
                        month -= yearMonths;
                    }

                    while (month < 1)
                    {
                        month += yearMonths;
                    }

                    holder.Month = month;
                    holder.Day = day;
                    holder.Season = DolocConfig.Tables.TbSeason.GetDefaultByMonth(month).SeasonTitle;
                }
                catch
                {
                    holder.Month = 0;
                    holder.Day = 0;
                    holder.Season = string.Empty;
                }

                return holder;
            }
        }
    }

    [HarmonyPatch(typeof(DateSlot), nameof(DateSlot.Render))]
    internal static class PatchDateSlotRender
    {
        private static void Postfix(DateSlot __instance, DateData data)
        {
            CalendarInject.RenderSlot(__instance, data);
        }
    }

    [HarmonyPatch(typeof(CalendarViewer), "Render")]
    internal static class PatchCalendarViewerRender
    {
        private static void Postfix(CalendarViewer __instance, DateEventData data)
        {
            CalendarInject.AppendRows(__instance, data);
        }
    }
}
