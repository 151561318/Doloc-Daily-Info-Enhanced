using System;
using System.Collections.Generic;
using DTMAPI.Abstractions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>
    /// In-game layout editor (F8 by default). It walks <see cref="Layout.Items"/>, lets the arrow keys
    /// move or resize the selected value and prints the numbers into the log, so a tuned layout can be
    /// baked into the shipped defaults. Keys go through DTMAPI's input helper first because the game
    /// runs on the Input System package (UnityEngine.Input throws there).
    /// </summary>
    internal static class EditorUi
    {
        private static IInputHelper s_input;
        private static bool s_enabled;
        private static int s_index;
        private static GameObject s_root;
        private static Text s_title;
        private static Text s_body;
        private static int s_lastShown = -1;
        private static string s_toast;
        private static float s_toastUntil;
        private static bool s_inputBlocked;
        private static bool s_previewAutoShown;

        /// <summary>Player-portrait mode: the panel only walks the <c>hud.player.*</c> values and the
        /// badge is force-shown, so the portrait can be tuned at real size.</summary>
        private static bool s_portraitMode;

        private static readonly List<Layout.Item> s_portraitItems = new List<Layout.Item>();

        /// <summary>0 = numeric list, <see cref="PreviewUi.PageCalendar"/> / <see cref="PreviewUi.PageHud"/>.</summary>
        private static int s_previewPage;

        internal static int PreviewPage
        {
            get => s_previewPage;
            set => s_previewPage = value;
        }

        internal static bool Enabled => s_enabled;

        internal static void Init(IInputHelper input)
        {
            s_input = input;
        }

        private static bool Pressed(string button)
        {
            if (string.IsNullOrEmpty(button))
            {
                return false;
            }

            try
            {
                if (s_input != null && s_input.WasPressed(button))
                {
                    return true;
                }
            }
            catch
            {
                // the DTMAPI binding may not cover this button; fall through to the raw keyboard
            }

            return RawPressed(button);
        }

        private static bool Held(string button)
        {
            if (string.IsNullOrEmpty(button))
            {
                return false;
            }

            try
            {
                if (s_input != null && s_input.IsDown(button))
                {
                    return true;
                }
            }
            catch
            {
                // see Pressed
            }

            return RawHeld(button);
        }

        /// <summary>The game runs on the Input System package, so UnityEngine.Input cannot be used.</summary>
        private static Key? ToKey(string button)
        {
            if (string.IsNullOrEmpty(button))
            {
                return null;
            }

            string name = button.Trim();
            switch (name.ToLowerInvariant())
            {
                case "up": case "uparrow": name = "UpArrow"; break;
                case "down": case "downarrow": name = "DownArrow"; break;
                case "left": case "leftarrow": name = "LeftArrow"; break;
                case "right": case "rightarrow": name = "RightArrow"; break;
                case "shift": case "leftshift": name = "LeftShift"; break;
                case "space": case "spacebar": name = "Space"; break;
                case "esc": case "escape": name = "Escape"; break;
                case "return": case "enter": name = "Enter"; break;
            }

            try
            {
                if (Enum.TryParse(name, true, out Key parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // fall through
            }

            return null;
        }

        private static bool RawPressed(string button)
        {
            try
            {
                Keyboard keyboard = Keyboard.current;
                Key? key = ToKey(button);
                return keyboard != null && key.HasValue && keyboard[key.Value].wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
        }

        private static bool RawHeld(string button)
        {
            try
            {
                Keyboard keyboard = Keyboard.current;
                Key? key = ToKey(button);
                return keyboard != null && key.HasValue && keyboard[key.Value].isPressed;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The game loads its config tables (and localization) through Addressables, which are not
        /// ready on the title screen of a cold start. Every UI prefab and every static text needs
        /// them, so the editor stays dormant until they exist.
        /// </summary>
        internal static bool ConfigsReady()
        {
            try
            {
                return DolocTown.Config.DolocConfig.StaticTexts != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void Update()
        {
            try
            {
                // Debug hook (no menu entry): lets me open a preview page without a keyboard.
                if (s_enabled && !s_previewAutoShown && ModConfig.PreviewOnStart > 0 && DolocAPI.uiSystem != null && GameTime.Ready)
                {
                    PreviewPage = ModConfig.PreviewOnStart;
                    if (PreviewUi.Show())
                    {
                        s_previewAutoShown = true;
                        if (ModConfig.PreviewOnStart >= 3)
                        {
                            // 3 = also decorate every cell with a portrait + festival banner, which
                            // is the crowded case the calibration pass exists for.
                            PreviewUi.SelectBirthday("kasia", 0);
                            PreviewUi.SelectFestival("节日角标-鲑鱼节", 0);
                        }

                        Refresh();
                    }
                }

                // Dedicated panel for the player's birthday portrait (F8): the sprite is a rig frame,
                // not a calendar portrait, so it needs its own scale/offset values - and it has to be
                // visible while tuning even when the saved birthday is another day.
                if (ModConfig.ShowLayoutEditor && Pressed(ModConfig.PlayerEditorKey))
                {
                    TogglePortraitEditor();
                }

                // The DTMAPI option is the master switch: with it off the key does nothing at all.
                if (ModConfig.ShowLayoutEditor && Pressed(ModConfig.EditorKey))
                {
                    if (s_enabled && s_portraitMode)
                    {
                        // Coming from the portrait panel, F7 returns to the full list rather than
                        // closing the overlay: same window, different value set.
                        s_portraitMode = false;
                        Reminders.PortraitPreview = false;
                        HudBadges.Invalidate();
                        s_index = 0;
                        s_enabled = Show();
                        SetInputBlocked(s_enabled);
                        Toast("完整布局编辑器");
                        Log.Info("layout editor on (full list)");
                    }
                    else
                    {
                        s_enabled = !s_enabled;
                        if (s_enabled)
                        {
                            s_enabled = Show();
                        }

                        if (!s_enabled)
                        {
                            Hide();
                        }

                        SetInputBlocked(s_enabled);
                        Log.Info("layout editor " + (s_enabled ? "on" : "off"));
                    }
                }

                if (s_enabled && !ModConfig.ShowLayoutEditor)
                {
                    s_enabled = false;
                    Hide();
                    SetInputBlocked(false);
                    Log.Info("layout editor closed (option turned off)");
                }

                if (Pressed(ModConfig.DemoKey))
                {
                    Reminders.DemoMode = !Reminders.DemoMode;
                    HudBadges.Invalidate();
                    Toast("示例图标：" + (Reminders.DemoMode ? "开" : "关"));
                    Log.Info("demo badges " + (Reminders.DemoMode ? "on" : "off"));
                }

                if (!s_enabled)
                {
                    return;
                }

                // Tab opens / closes the calendar calibration preview.
                KeepInputBlocked();

                if (Pressed("Tab"))
                {
                    if (PreviewUi.Active)
                    {
                        PreviewUi.Hide();
                        PreviewPage = 0;
                        Toast("数值编辑");
                        if (s_root != null)
                        {
                            s_root.SetActive(true);
                        }
                    }
                    else
                    {
                        PreviewPage = 1;
                        if (s_root != null)
                        {
                            s_root.SetActive(false);
                        }

                        PreviewUi.Show();
                    }

                    Refresh();
                    return;
                }

                if (PreviewUi.Active)
                {
                    PreviewUi.Tick();
                    if (!PreviewUi.Active)
                    {
                        PreviewPage = 0;
                        if (s_root != null)
                        {
                            s_root.SetActive(true);
                        }

                        Refresh();
                        return;
                    }

                    if (s_root != null && s_root.activeSelf)
                    {
                        s_root.SetActive(false);
                    }

                    HandlePreviewKeys();

                    if (Pressed(ModConfig.SaveKey))
                    {
                        Layout.SaveCurrent();
                        Toast("已保存布局与逐植物偏移");
                        DumpAll("saved");
                    }

                    return;
                }

                if (s_root != null && !s_root.activeSelf)
                {
                    s_root.SetActive(true);
                }

                List<Layout.Item> items = ActiveItems();
                if (items.Count == 0)
                {
                    return;
                }

                if (s_index >= items.Count)
                {
                    s_index = 0;
                }

                bool dirty = false;
                if (Pressed("DownArrow") || Pressed("Down"))
                {
                    s_index = (s_index + 1) % items.Count;
                    dirty = true;
                }

                if (Pressed("UpArrow") || Pressed("Up"))
                {
                    s_index = (s_index + items.Count - 1) % items.Count;
                    dirty = true;
                }

                float multiplier = Held("LeftShift") || Held("Shift") ? 10f : 1f;
                Layout.Item item = items[s_index];

                if (Pressed("RightArrow") || Pressed("Right"))
                {
                    item.Set(item.Value + item.Step * multiplier);
                    dirty = true;
                }

                if (Pressed("LeftArrow") || Pressed("Left"))
                {
                    item.Set(item.Value - item.Step * multiplier);
                    dirty = true;
                }

                if (dirty)
                {
                    Layout.RaiseChanged();
                    Log.Info("layout " + item.Key + " = " + Layout.Format(item.Value));
                }

                if (Pressed(ModConfig.SaveKey))
                {
                    Layout.SaveCurrent();
                    Toast("已保存 " + items.Count + " 项到 state.json");
                    Log.Info("layout saved");
                    DumpAll("saved");
                }

                if (Pressed(ModConfig.DumpKey))
                {
                    DumpAll("dump");
                    Toast("已把全部数值打印到日志");
                }

                Refresh();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "EditorUi.Update");
            }
        }

        /// <summary>
        /// Opens/closes the player-portrait panel. Opening it also turns on
        /// <see cref="Reminders.PortraitPreview"/> so the badge is on screen even when today is not
        /// the player's birthday; closing it drops the preview so the HUD goes back to real data.
        /// </summary>
        private static void TogglePortraitEditor()
        {
            if (s_enabled && s_portraitMode)
            {
                s_enabled = false;
                Hide();
                SetInputBlocked(false);
                Toast("玩家头像预览：关");
                Log.Info("portrait editor off");
                return;
            }

            s_portraitMode = true;
            Reminders.PortraitPreview = true;
            HudBadges.Invalidate();
            s_index = 0;

            s_enabled = Show();
            if (!s_enabled)
            {
                Reminders.PortraitPreview = false;
                HudBadges.Invalidate();
            }
            else
            {
                Toast("玩家生日头像预览：已显示");
                Log.Info("portrait editor on, sprite=" + PlayerPortrait.SpriteName);
            }

            SetInputBlocked(s_enabled);
        }

        /// <summary>Layout items the panel currently walks: everything, or just the player portrait.</summary>
        private static List<Layout.Item> ActiveItems()
        {
            if (!s_portraitMode)
            {
                return Layout.Items;
            }

            s_portraitItems.Clear();
            List<Layout.Item> all = Layout.Items;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Key != null && all[i].Key.StartsWith("hud.player.", StringComparison.Ordinal))
                {
                    s_portraitItems.Add(all[i]);
                }
            }

            return s_portraitItems;
        }

        /// <summary>
        /// Font used by the editor panel (the game's own event-slot text font); the preview overlay
        /// reuses it so both panels look identical.
        /// </summary>
        internal static Font PanelFont()
        {
            if (s_title != null && s_title.font != null)
            {
                return s_title.font;
            }

            return HudBadges.TipFont();
        }

        /// <summary>Maps the preview page's logical keys onto the two input back-ends.</summary>
        private static void HandlePreviewKeys()
        {
            if (Pressed("LeftArrow") || Pressed("Left")) { PreviewUi.HandleKey("left"); return; }
            if (Pressed("RightArrow") || Pressed("Right")) { PreviewUi.HandleKey("right"); return; }
            if (Pressed("UpArrow") || Pressed("Up")) { PreviewUi.HandleKey("up"); return; }
            if (Pressed("DownArrow") || Pressed("Down")) { PreviewUi.HandleKey("down"); return; }
            if (Pressed("PageUp")) { PreviewUi.HandleKey("pageup"); return; }
            if (Pressed("PageDown")) { PreviewUi.HandleKey("pagedown"); return; }
            if (Pressed("Space")) { PreviewUi.HandleKey("space"); return; }
            if (Pressed("A") || Pressed("a")) { PreviewUi.HandleKey("a"); return; }
            if (Pressed("D") || Pressed("d")) { PreviewUi.HandleKey("d"); return; }
            if (Pressed("W") || Pressed("w")) { PreviewUi.HandleKey("w"); return; }
            if (Pressed("S") || Pressed("s")) { PreviewUi.HandleKey("s"); return; }
            if (Pressed("R") || Pressed("r")) { PreviewUi.HandleKey("r"); return; }
            if (Pressed("C") || Pressed("c")) { PreviewUi.HandleKey("c"); return; }
            if (Pressed("B") || Pressed("b")) { PreviewUi.HandleKey("b"); return; }
            if (Pressed("N") || Pressed("n")) { PreviewUi.HandleKey("n"); }
        }

        /// <summary>Temporary probe: what UI can we reach while the title screen is up?</summary>
        internal static void DumpAllFromPreview()
        {
            DumpAll("preview");
            Toast("已把全部数值打印到日志");
        }

        /// <summary>
        /// While the panel is open the game's own input maps are disabled, otherwise the arrow keys
        /// would also walk the character around.
        /// </summary>
        private static void SetInputBlocked(bool blocked)
        {
            if (s_inputBlocked == blocked)
            {
                return;
            }

            try
            {
                if (DolocAPI.UserInput == null)
                {
                    return;
                }

                if (blocked)
                {
                    DolocAPI.UserInput.DisableAllInput(includeGlobal: true);
                }
                else
                {
                    DolocAPI.UserInput.ResumeCurrentInput();
                }

                s_inputBlocked = blocked;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "EditorUi.SetInputBlocked");
            }
        }

        /// <summary>
        /// The game re-enables its action maps whenever the input type changes (UI opens/closes, tool
        /// switch, ...), so blocking once is not enough: hold them down for as long as the panel is
        /// open, otherwise the arrow keys still walk the character around.
        /// </summary>
        private static void KeepInputBlocked()
        {
            if (!s_inputBlocked || DolocAPI.UserInput == null)
            {
                return;
            }

            try
            {
                DolocAPI.UserInput.DisableAllInput(includeGlobal: true);
            }
            catch
            {
                // Worst case the player can still move while the panel is open.
            }
        }

        private static void Toast(string text)
        {
            s_toast = text;
            s_toastUntil = Time.unscaledTime + 1.8f;
            s_lastShown = -1;
        }

        internal static void DumpAll(string tag)
        {
            List<Layout.Item> items = Layout.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Log.Info("cfg " + tag + " " + items[i].Key + " " + items[i].Label + " = " +
                         Layout.Format(items[i].Value));
            }

            Dictionary<string, string> crops = Layout.CaptureCropOffsets();
            foreach (KeyValuePair<string, string> pair in crops)
            {
                Log.Info("cfg " + tag + " crop:" + pair.Key + " = " + pair.Value);
            }
        }

        private static void Refresh()
        {
            if (s_root == null || s_title == null)
            {
                return;
            }

            bool toastActive = !string.IsNullOrEmpty(s_toast) && Time.unscaledTime < s_toastUntil;
            if (!toastActive && s_lastShown == s_index && Time.frameCount % 30 != 0)
            {
                return;
            }

            s_lastShown = s_index;
            List<Layout.Item> items = ActiveItems();
            if (items.Count == 0)
            {
                return;
            }

            if (s_index >= items.Count)
            {
                s_index = 0;
            }

            Layout.Item item = items[s_index];

            s_title.text = (s_portraitMode ? "玩家生日头像  " : "布局编辑器  ") +
                           (s_index + 1) + "/" + items.Count + "   " + item.Label;

            string closeKey = s_portraitMode ? ModConfig.PlayerEditorKey : ModConfig.EditorKey;
            s_body.text = item.Key + " = " + Layout.Format(item.Value) +
                          "   (范围 " + Layout.Format(item.Min) + "~" + Layout.Format(item.Max) + ")\n" +
                          (s_portraitMode ? "头像资源：" + PlayerPortrait.SpriteName + "（预览已强制显示）   " : string.Empty) +
                          "↑↓ 选择   ←→ 调整(Shift×10)   " + ModConfig.SaveKey + " 保存   " +
                          ModConfig.DemoKey + " 示例图标   " + ModConfig.DumpKey + " 打印全部   " +
                          closeKey + " 关闭";

            if (toastActive)
            {
                s_title.text = "✔ " + s_toast;
            }
        }

        private static bool Show()
        {
            try
            {
                if (s_root != null)
                {
                    s_root.SetActive(true);
                    Refresh();
                    return true;
                }

                RectTransform host = UiRoot.Overlay();
                if (host == null)
                {
                    return false;
                }

                // Clone the event-row prefab so the text keeps the game's own font, but park it in
                // the bottom-left corner where it does not cover the durability panel.
                GameObject prefab = DolocAPI.GetAsset<GameObject>(DolocGameAssets.UI_ELEMENT_EVENT_SLOT);
                if (prefab == null)
                {
                    return false;
                }

                s_root = UnityEngine.Object.Instantiate(prefab, host, false);
                s_root.name = "SomeOptEditor";
                s_root.SetActive(true);
                s_root.transform.SetAsLastSibling();

                RectTransform rect = s_root.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(0f, 0f);
                    rect.pivot = new Vector2(0f, 0f);
                    rect.anchoredPosition = new Vector2(24f, 170f);
                    rect.sizeDelta = new Vector2(820f, 132f);
                }

                Text[] texts = s_root.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (i == 0)
                    {
                        s_title = texts[i];
                        s_title.gameObject.SetActive(true);
                    }
                    else if (i == 1)
                    {
                        s_body = texts[i];
                        s_body.gameObject.SetActive(true);
                    }
                    else
                    {
                        texts[i].gameObject.SetActive(false);
                    }
                }

                if (s_title == null)
                {
                    Log.Warn("editor overlay: no text found in the event slot prefab");
                    s_root.SetActive(false);
                    return false;
                }

                if (s_body == null)
                {
                    s_body = s_title;
                }

                s_lastShown = -1;
                Refresh();
                Log.Info("editor overlay created");
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "EditorUi.Show");
                return false;
            }
        }

        private static void Hide()
        {
            PreviewUi.Hide();
            s_previewPage = 0;

            if (s_portraitMode)
            {
                s_portraitMode = false;
                Reminders.PortraitPreview = false;
                HudBadges.Invalidate();
                Log.Info("portrait preview off");
            }

            if (s_root != null)
            {
                s_root.SetActive(false);
            }
        }

    }
}
