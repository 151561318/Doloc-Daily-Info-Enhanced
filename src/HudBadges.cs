using System;
using System.Collections.Generic;
using DolocTown.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>
    /// The row of small icons hanging under the top bar clock. Icons are plain uGUI objects the
    /// mod creates at runtime; nothing is written into the game's prefabs or save data.
    /// </summary>
    internal static class HudBadges
    {
        private sealed class Slot
        {
            internal RectTransform Root;
            internal Image Background;
            internal RectTransform IconClip;
            internal RectMask2D IconMask;
            internal Image Icon;
            internal Text Plus;
            internal Image Glow;
            internal Image Heart;
            internal Image BorderTop;
            internal Image BorderBottom;
            internal Image BorderLeft;
            internal Image BorderRight;
            internal HoverProxy Hover;
        }

        private static readonly List<Slot> s_slots = new List<Slot>();
        private static readonly List<Badge> s_badges = new List<Badge>();
        private static RectTransform s_row;
        private static TimeTip s_tip;
        private static float s_nextScan;
        private static bool s_logged;
        private static Text s_label;
        private static bool s_heartLogged;
        private static System.Reflection.FieldInfo s_textField;

        internal static void Update()
        {
            if (!GameTime.Ready)
            {
                Hide();
                return;
            }

            float now = Time.unscaledTime;
            if (now >= s_nextScan)
            {
                s_nextScan = now + 1f;
                try
                {
                    Refresh();
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "HudBadges.Refresh");
                }
            }

            Animate();
        }

        internal static void Hide()
        {
            if (s_row != null && s_row.gameObject.activeSelf)
            {
                s_row.gameObject.SetActive(false);
            }
        }

        /// <summary>Tears the row down so the next refresh rebuilds it with the current layout values.</summary>
        internal static void Invalidate()
        {
            if (s_row != null)
            {
                UnityEngine.Object.Destroy(s_row.gameObject);
                s_row = null;
            }

            s_slots.Clear();
        }

        private static void Refresh()
        {
            if (s_row == null || s_tip == null)
            {
                if (!TryAttach())
                {
                    return;
                }
            }

            Reminders.Build(s_badges);
            Diagnostics.Report(s_badges, s_tip, s_row);

            if (s_badges.Count == 0)
            {
                Hide();
                return;
            }

            if (!s_row.gameObject.activeSelf)
            {
                s_row.gameObject.SetActive(true);
            }

            EnsureSlots(s_badges.Count);

            for (int i = 0; i < s_slots.Count; i++)
            {
                Slot slot = s_slots[i];
                bool used = i < s_badges.Count;
                if (slot.Root.gameObject.activeSelf != used)
                {
                    slot.Root.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                Apply(slot, s_badges[i], i);
            }
        }

        /// <summary>
        /// Picks the HUD clock: the title screen owns a second TimeTip whose label is a placeholder,
        /// and that instance survives into the loaded game, so the live one has to be chosen by
        /// looking at what its label actually says.
        /// </summary>
        private static TimeTip ResolveTip()
        {
            try
            {
                BasicTipGroup group = DolocAPI.uiSystem != null ? DolocAPI.uiSystem.basicTip : null;
                TimeTip live = group != null ? group.TimeTip : null;
                if (IsUsable(live))
                {
                    return live;
                }
            }
            catch
            {
                // fall through to a scene search
            }

            if (IsUsable(s_tip))
            {
                return s_tip;
            }

            try
            {
                TimeTip[] tips = UnityEngine.Object.FindObjectsOfType<TimeTip>();
                for (int i = 0; i < tips.Length; i++)
                {
                    if (IsUsable(tips[i]))
                    {
                        return tips[i];
                    }
                }
            }
            catch
            {
                // nothing else to try
            }

            return null;
        }

        private static bool IsUsable(TimeTip tip)
        {
            if (tip == null || tip.rectTransform == null || tip.rectTransform.parent == null)
            {
                return false;
            }

            if (!tip.gameObject.activeInHierarchy)
            {
                return false;
            }

            Text label = TipText(tip);
            return label != null && !string.IsNullOrEmpty(label.text) && label.text.IndexOf(':') >= 0;
        }

        private static Text TipText(TimeTip tip)
        {
            if (s_textField == null)
            {
                s_textField = AccessTools.Field(typeof(TimeTip), "text");
            }

            return s_textField != null ? s_textField.GetValue(tip) as Text : null;
        }

        private static bool TryAttach()
        {
            TimeTip tip = ResolveTip();

            if (tip == null || tip.rectTransform == null || tip.rectTransform.parent == null)
            {
                return false;
            }

            if (!ReferenceEquals(tip, s_tip))
            {
                s_tip = tip;
                s_label = TipText(tip);
                s_row = null;
            }

            RectTransform tipRect = tip.rectTransform;
            RectTransform parent = tipRect.parent as RectTransform;
            if (parent == null)
            {
                return false;
            }

            if (s_row == null)
            {
                s_row = UiFactory.Create(parent, "SomeOptBadges", null, new Color(0f, 0f, 0f, 0f));
                s_row.anchorMin = tipRect.anchorMin;
                s_row.anchorMax = tipRect.anchorMax;
                s_row.pivot = new Vector2(0f, 1f);
                s_row.sizeDelta = new Vector2(0f, Layout.HudIcon);
                LayoutElement layout = s_row.gameObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;

                if (!s_logged)
                {
                    s_logged = true;
                    Log.Info("badge row attached under " + tipRect.parent.name);
                }
            }

            s_row.SetParent(parent, false);
            s_row.SetAsLastSibling();
            return true;
        }

        private static void EnsureSlots(int count)
        {
            while (s_slots.Count < count)
            {
                s_slots.Add(CreateSlot(s_row));
            }
        }

        private static Slot CreateSlot(RectTransform parent)
        {
            Slot slot = new Slot();
            slot.Root = UiFactory.Create(parent, "SomeOptBadge", null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Size(slot.Root, Layout.HudIcon, Layout.HudIcon);

            slot.Glow = UiFactory.Create(slot.Root, "Glow", ProcSprites.Glow, new Color(1f, 0.86f, 0.35f, 0f)).GetComponent<Image>();
            slot.Background = UiFactory.Create(slot.Root, "Bg", ProcSprites.Plate, BadgeBackgrounds.Resolve(ModConfig.BadgeBackground)).GetComponent<Image>();
            slot.Background.raycastTarget = true;

            // Clipping container for the portrait. Only the player's badge turns it on: that art is a
            // full body the user can zoom past the plate, and anything outside the plate must not
            // spill over its neighbours. NPC portraits keep the unclipped look they were tuned for.
            slot.IconClip = UiFactory.Create(slot.Root, "IconClip", null, new Color(0f, 0f, 0f, 0f));
            slot.IconMask = slot.IconClip.gameObject.AddComponent<RectMask2D>();
            slot.IconMask.enabled = false;

            slot.Icon = UiFactory.Create(slot.IconClip, "Icon", null, Color.white).GetComponent<Image>();

            Font font = s_tip != null ? TipFont() : Resources.GetBuiltinResource<Font>("Arial.ttf");
            slot.Plus = UiFactory.CreateText(slot.Root, "Plus", font, Mathf.RoundToInt(Layout.HudPlusSize),
                new Color(1f, 0.9f, 0.25f, 1f));
            slot.Heart = UiFactory.Create(slot.Root, "Heart", LoadHeart(), new Color(1f, 1f, 1f, 1f)).GetComponent<Image>();

            Color border = new Color(1f, 0.84f, 0.2f, 0f);
            slot.BorderTop = UiFactory.Create(slot.Root, "BorderTop", ProcSprites.Square, border).GetComponent<Image>();
            slot.BorderBottom = UiFactory.Create(slot.Root, "BorderBottom", ProcSprites.Square, border).GetComponent<Image>();
            slot.BorderLeft = UiFactory.Create(slot.Root, "BorderLeft", ProcSprites.Square, border).GetComponent<Image>();
            slot.BorderRight = UiFactory.Create(slot.Root, "BorderRight", ProcSprites.Square, border).GetComponent<Image>();
            slot.BorderTop.preserveAspect = false;
            slot.BorderBottom.preserveAspect = false;
            slot.BorderLeft.preserveAspect = false;
            slot.BorderRight.preserveAspect = false;

            slot.Hover = slot.Background.gameObject.AddComponent<HoverProxy>();
            return slot;
        }

        /// <summary>The clock's own font; the calendar reuses it so both "+N" markers look identical.</summary>
        internal static Font TipFont()
        {
            if (s_label != null && s_label.font != null)
            {
                return s_label.font;
            }

            if (s_tip != null)
            {
                Text label = TipText(s_tip);
                if (label != null && label.font != null)
                {
                    return label.font;
                }
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 offset)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
        }

        private static Sprite LoadHeart()
        {
            try
            {
                Sprite heart = ModSprites.Heart;
                if (heart != null)
                {
                    return heart;
                }

                heart = DolocAPI.LoadSprite("ui_icon_emolove");
                if (heart != null)
                {
                    return heart;
                }

                return ProcSprites.Heart;
            }
            catch
            {
                return ProcSprites.Heart;
            }
        }

        private static void Apply(Slot slot, Badge badge, int index)
        {
            float size = Layout.HudIcon;

            UiFactory.Size(slot.Root, size, size);
            slot.Root.anchorMin = new Vector2(0f, 1f);
            slot.Root.anchorMax = new Vector2(0f, 1f);
            slot.Root.pivot = new Vector2(0f, 1f);
            slot.Root.anchoredPosition = new Vector2(index * (size + Layout.HudSpacing), 0f);

            UiFactory.Size(slot.Glow.rectTransform, size * Layout.GlowScale, size * Layout.GlowScale);
            UiFactory.Size(slot.Background.rectTransform, size, size);

            // The portrait lives inside this square; with the mask on, zoomed artwork is cut at the
            // plate edge instead of bleeding over the next badge.
            UiFactory.Size(slot.IconClip, size, size);
            slot.IconClip.anchoredPosition = Vector2.zero;
            if (slot.IconMask.enabled != badge.Player)
            {
                slot.IconMask.enabled = badge.Player;
            }

            // Border highlight: four thin bars hugging the plate, visible even in broad daylight.
            float thickness = Layout.HudBorderWidth;
            float spread = Layout.HudBorderSpread;
            float side = size + spread * 2f;
            UiFactory.Size(slot.BorderTop.rectTransform, side, thickness);
            UiFactory.Size(slot.BorderBottom.rectTransform, side, thickness);
            UiFactory.Size(slot.BorderLeft.rectTransform, thickness, side);
            UiFactory.Size(slot.BorderRight.rectTransform, thickness, side);

            Place(slot.BorderTop.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, -spread - thickness * 0.5f));
            Place(slot.BorderBottom.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, spread + thickness * 0.5f));
            Place(slot.BorderLeft.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(-spread - thickness * 0.5f, 0f));
            Place(slot.BorderRight.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(spread + thickness * 0.5f, 0f));

            // "+N" on the plate's top-right corner, 2 units inside the border.
            UiFactory.Size(slot.Plus.rectTransform, Layout.HudPlusSize * 2f, Layout.HudPlusSize * 1.6f);
            Place(slot.Plus.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-2f + Layout.HudPlusOffX, -2f + Layout.HudPlusOffY));
            slot.Plus.fontSize = Mathf.RoundToInt(Layout.HudPlusSize);

            UiFactory.Size(slot.Heart.rectTransform, Layout.HudHeartSize, Layout.HudHeartSize);
            Place(slot.Heart.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(Layout.HudHeartOffX, Layout.HudHeartOffY));

            slot.Background.color = BadgeBackgrounds.Resolve(ModConfig.BadgeBackground);

            UiFactory.SetSprite(slot.Icon, badge.Icon);
            slot.Icon.rectTransform.localScale = Vector3.one;

            // Portraits are packed with transparent margins; fit the visible pixels into the plate.
            if (badge.Icon != null && (badge.Kind == BadgeKind.Birthday || badge.Kind == BadgeKind.Merchant ||
                                       badge.Kind == BadgeKind.Pike))
            {
                // The player's badge uses a rig frame and therefore its own fit values, tuned in the
                // dedicated F8 editor; the NPC portraits keep the calendar-calibrated ones.
                float margin = badge.Player ? Layout.PlayerIconMargin : Layout.HudIconMargin;
                float scale = badge.Player ? Layout.PlayerPortraitScale : Layout.HudPortraitScale;
                float offX = badge.Player ? Layout.PlayerPortraitOffX : Layout.HudPortraitOffX;
                float offY = badge.Player ? Layout.PlayerPortraitOffY : Layout.HudPortraitOffY;

                // Scale about the plate centre so shrinking stays centred and growing keeps it
                // growing until it fills (and can overhang) the plate. The player portrait may zoom
                // far past the plate (clipped by IconMask); NPC portraits keep the old 1.8x ceiling.
                float maxBox = Mathf.Max(6f, size - margin * 2f);
                float ceiling = badge.Player ? size * 6f : size * 1.8f;
                float box = Mathf.Clamp(maxBox * scale, 6f, ceiling);
                IconFit.Fit(slot.Icon, box, IconFit.Align.Center);

                // IconFit centred the ink; the user offset is added on top of that.
                slot.Icon.rectTransform.anchoredPosition += new Vector2(offX, offY);
            }
            else
            {
                UiFactory.Size(slot.Icon.rectTransform, size - Mathf.Max(3f, size * 0.08f),
                    size - Mathf.Max(3f, size * 0.08f));
                slot.Icon.rectTransform.anchoredPosition = Vector2.zero;
            }

            if (slot.BorderTop.gameObject.activeSelf != badge.Glow)
            {
                slot.BorderTop.gameObject.SetActive(badge.Glow);
                slot.BorderBottom.gameObject.SetActive(badge.Glow);
                slot.BorderLeft.gameObject.SetActive(badge.Glow);
                slot.BorderRight.gameObject.SetActive(badge.Glow);
            }

            if (slot.Plus != null)
            {
                if (badge.ExtraKinds > 0)
                {
                    slot.Plus.text = "+" + badge.ExtraKinds;
                    slot.Plus.gameObject.SetActive(true);
                }
                else
                {
                    slot.Plus.gameObject.SetActive(false);
                }
            }

            if (slot.Heart.gameObject.activeSelf != badge.Heart)
            {
                slot.Heart.gameObject.SetActive(badge.Heart);
            }

            if (badge.Heart && Diagnostics.Enabled == false && !s_heartLogged)
            {
                s_heartLogged = true;
                Log.Info("heart slot=" + index + " kind=" + badge.Kind + " sprite=" +
                         (slot.Heart.sprite == null ? "null" : slot.Heart.sprite.name) + " size=" +
                         Layout.Format(Layout.HudHeartSize));
            }

            if (slot.Glow.gameObject.activeSelf != badge.Glow)
            {
                slot.Glow.gameObject.SetActive(badge.Glow);
            }

            if (slot.Hover.Text != badge.Hover)
            {
                slot.Hover.Text = badge.Hover;
            }
        }

        private static void Animate()
        {
            if (s_row == null || !s_row.gameObject.activeSelf)
            {
                return;
            }

            FollowClock();

            float pulse = Layout.GlowAlpha * (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.2f));
            for (int i = 0; i < s_slots.Count; i++)
            {
                Slot slot = s_slots[i];
                if (!slot.Root.gameObject.activeSelf)
                {
                    continue;
                }

                if (slot.Glow.gameObject.activeSelf)
                {
                    Color g = slot.Glow.color;
                    g.a = pulse * 0.35f;
                    slot.Glow.color = g;
                }

                if (slot.BorderTop.gameObject.activeSelf)
                {
                    Color b = slot.BorderTop.color;
                    b.a = pulse;
                    slot.BorderTop.color = b;
                    slot.BorderBottom.color = b;
                    slot.BorderLeft.color = b;
                    slot.BorderRight.color = b;
                }
            }
        }

        /// <summary>Keeps the row glued to the bottom edge of the clock, whatever the layout does.</summary>
        private static void FollowClock()
        {
            if (s_tip == null || s_tip.rectTransform == null)
            {
                return;
            }

            RectTransform tip = s_tip.rectTransform;
            RectTransform target = LabelRect(tip);
            Vector3 left = target == tip
                ? target.TransformPoint(new Vector3(target.rect.xMin, target.rect.yMin, 0f))
                : LabelLeft(target);

            // Y comes from the clock bar itself: the label rect has no height, so its own bottom
            // would sit on the vertical centre of the string.
            Vector3 barBottom = tip.TransformPoint(new Vector3(0f, tip.rect.yMin, 0f));
            s_row.position = new Vector3(left.x + Layout.HudOffsetX, barBottom.y - Layout.HudGapY, barBottom.z);
        }

        /// <summary>Left edge of the drawn clock string, in world space.</summary>
        private static Vector3 LabelLeft(RectTransform rect)
        {
            Text text = rect.GetComponent<Text>();
            if (text == null)
            {
                return rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y, 0f));
            }

            Vector3 leftEdge = rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y, 0f));
            Vector3 rightEdge = rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.center.y, 0f));
            float width = text.preferredWidth;

            switch (text.alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return leftEdge;

                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return width > 1f ? rightEdge - rect.TransformVector(new Vector3(width, 0f, 0f)) : rightEdge;

                default:
                    if (width <= 1f)
                    {
                        return rect.TransformPoint(rect.rect.center);
                    }

                    Vector3 span = rect.TransformVector(new Vector3(width, 0f, 0f));
                    return rect.TransformPoint(rect.rect.center) - span * 0.5f;
            }
        }

        /// <summary>
        /// The clock tip's rect is zero sized; the visible "13:05 星期二 ..." string lives on the
        /// tip's own label, so that label is what the row is centred on.
        /// </summary>
        private static RectTransform LabelRect(RectTransform tip)
        {
            if (s_label == null || s_label.rectTransform == null)
            {
                s_label = TipText(s_tip);
            }

            return s_label != null && s_label.rectTransform != null ? s_label.rectTransform : tip;
        }

        /// <summary>Horizontal centre of the drawn clock string, in world space.</summary>
        private static Vector3 LabelCentre(RectTransform rect)
        {
            Vector3 centre = rect.TransformPoint(rect.rect.center);
            Text text = rect.GetComponent<Text>();
            if (text == null)
            {
                return centre;
            }

            // Centred alignments (the clock uses MiddleCenter) put the string on the rect centre no
            // matter how wide the rect happens to be, including a zero-width overflow label.
            switch (text.alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    break;
                default:
                    return centre;
            }

            float width = text.preferredWidth;
            if (width <= 1f)
            {
                return centre;
            }

            Vector3 span = rect.TransformVector(new Vector3(width, 0f, 0f));
            Vector3 left = rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y, 0f));
            Vector3 right = rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.center.y, 0f));

            switch (text.alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return left + span * 0.5f;
                default:
                    return right - span * 0.5f;
            }
        }
    }

    /// <summary>Vanilla hover box for the badges that carry a description.</summary>
    internal sealed class HoverProxy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            try
            {
                ((RectTransform)transform).HoverTextSmall(Text);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "HoverProxy");
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try
            {
                ((RectTransform)transform).HideHoverBox();
            }
            catch
            {
                // nothing to hide
            }
        }
    }
}
