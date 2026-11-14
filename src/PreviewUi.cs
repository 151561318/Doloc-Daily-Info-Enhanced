using System;
using System.Collections.Generic;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Plant;
using DolocTown.Config.Calendar;
using DolocTown.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>
    /// The calibration preview: a self drawn 7x4 grid of every seed, one page at a time, each cell
    /// carrying the same crop icon + "+N" the real calendar uses. It is deliberately independent from
    /// the game's calendar panel: no config tables are touched, nothing shares slots with the vanilla
    /// calendar, and it never fights with it for input.
    /// </summary>
    internal static class PreviewUi
    {
        internal const int PageCalendar = 1;
        internal const int PageHud = 2;

        private const int Cols = 7;
        private const int Rows = 4;
        private const int PerPage = Cols * Rows;
        private const float CellSize = 142f;
        private const float CellGap = 6f;
        private const float GridWidth = Cols * CellSize + (Cols - 1) * CellGap;
        private const float GridHeight = Rows * CellSize + (Rows - 1) * CellGap;

        internal static bool Active { get; private set; }
        internal static int Day { get; private set; } = 1;
        internal static string BirthdayNpc { get; private set; }
        internal static string FestivalIcon { get; private set; }

        private static RectTransform s_root;
        private static RectTransform s_grid;
        private static Text s_title;
        private static Text s_hint;

        private static int s_page;
        private static int s_selected;
        private static int s_scenario;

        private static readonly List<SeedInfo> s_seeds = new List<SeedInfo>();
        private static readonly List<string> s_npcs = new List<string>();
        private static readonly List<string> s_festivals = new List<string>();
        private static readonly Dictionary<string, Sprite> s_icons = new Dictionary<string, Sprite>();
        private static readonly List<GameObject> s_cells = new List<GameObject>();
        private static readonly List<Image> s_frames = new List<Image>();

        private static readonly string[] Scenarios = { "无", "生日", "节日", "生日+节日" };

        private static int PageCount => Mathf.Max(1, (s_seeds.Count + PerPage - 1) / PerPage);

        // ------------------------------------------------------------------ public surface

        internal static bool Show()
        {
            try
            {
                EnsureLists();
                if (!Build())
                {
                    return false;
                }

                Active = true;
                Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "PreviewUi.Show");
                Hide();
                return false;
            }
        }

        internal static void Hide()
        {
            Active = false;
            if (s_root != null)
            {
                UnityEngine.Object.Destroy(s_root.gameObject);
            }

            s_root = null;
            s_grid = null;
            s_title = null;
            s_hint = null;
            s_cells.Clear();
            s_frames.Clear();
        }

        /// <summary>Keeps the frame clean while the preview owns the screen.</summary>
        internal static void Tick()
        {
            if (!Active)
            {
                return;
            }

            if (s_root == null)
            {
                Active = false;
            }
        }

        /// <summary>Leftovers of the real calendar version; the self drawn grid does not touch slots.</summary>
        internal static void DecorateSlot(DateSlot slot, int day)
        {
        }

        /// <summary>Fake batches so the calendar patches keep showing two rows while the preview is up.</summary>
        internal static List<CropBatch> BatchesFor(int day)
        {
            return new List<CropBatch>();
        }

        internal static void SelectBirthday(string npcId, int unused)
        {
            BirthdayNpc = string.IsNullOrEmpty(npcId) ? null : npcId;
            Refresh();
        }

        internal static void SelectFestival(string url, int unused)
        {
            FestivalIcon = string.IsNullOrEmpty(url) ? null : url;
            Refresh();
        }

        internal static void HandleKey(string key)
        {
            if (!Active)
            {
                return;
            }

            switch (key)
            {
                case "left":
                    Select(s_selected - 1);
                    return;
                case "right":
                    Select(s_selected + 1);
                    return;
                case "up":
                    Select(s_selected - Cols);
                    return;
                case "down":
                    Select(s_selected + Cols);
                    return;
                case "pageup":
                    s_page = (s_page - 1 + PageCount) % PageCount;
                    s_selected = 0;
                    Refresh();
                    return;
                case "pagedown":
                    s_page = (s_page + 1) % PageCount;
                    s_selected = 0;
                    Refresh();
                    return;
                case "a":
                case "d":
                case "w":
                case "s":
                {
                    SeedInfo seed = SelectedSeed();
                    if (seed == null)
                    {
                        return;
                    }

                    float dx = key == "a" ? -1f : key == "d" ? 1f : 0f;
                    float dy = key == "w" ? 1f : key == "s" ? -1f : 0f;
                    Layout.NudgeCrop(seed.Id, dx, dy);
                    CalendarInject.RefreshLayout();
                    Refresh();
                    return;
                }

                case "r":
                {
                    SeedInfo seed = SelectedSeed();
                    if (seed != null)
                    {
                        Layout.ResetCrop(seed.Id);
                        CalendarInject.RefreshLayout();
                        Refresh();
                    }

                    return;
                }

                case "b":
                    BirthdayNpc = Next(s_npcs, BirthdayNpc);
                    Refresh();
                    return;
                case "n":
                    FestivalIcon = Next(s_festivals, FestivalIcon);
                    Refresh();
                    return;
                case "c":
                    s_scenario = 0;
                    BirthdayNpc = null;
                    FestivalIcon = null;
                    Refresh();
                    return;
                case "space":
                    s_scenario = (s_scenario + 1) % Scenarios.Length;
                    ApplyScenario();
                    Refresh();
                    return;
            }
        }

        // ------------------------------------------------------------------ construction

        private static bool Build()
        {
            if (s_root != null)
            {
                return true;
            }

            RectTransform host = UiRoot.Overlay();
            if (host == null)
            {
                Log.Warn("preview: no canvas to host the grid");
                return false;
            }

            s_root = UiFactory.Create(host, "SomeOptPreview", null, new Color(0f, 0f, 0f, 0f));
            RectTransform root = s_root;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(GridWidth + 60f, GridHeight + 150f);
            root.anchoredPosition = new Vector2(0f, 10f);

            RectTransform plate = UiFactory.Create(root, "Plate", ProcSprites.Plate,
                new Color(0.06f, 0.07f, 0.10f, 0.94f));
            plate.anchorMin = Vector2.zero;
            plate.anchorMax = Vector2.one;
            plate.offsetMin = Vector2.zero;
            plate.offsetMax = Vector2.zero;
            plate.GetComponent<Image>().preserveAspect = false;

            Font font = EditorUi.PanelFont();
            s_title = UiFactory.CreateText(root, "Title", font, 24, new Color(1f, 0.86f, 0.35f, 1f));
            Anchor(s_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f),
                new Vector2(GridWidth, 32f));

            s_hint = UiFactory.CreateText(root, "Hint", font, 17, new Color(0.86f, 0.88f, 0.93f, 1f));
            Anchor(s_hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f),
                new Vector2(GridWidth, 60f));
            s_hint.alignment = TextAnchor.UpperCenter;

            s_grid = UiFactory.Create(root, "Grid", null, new Color(0f, 0f, 0f, 0f));
            Anchor(s_grid, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(GridWidth, GridHeight));

            BuildCells();
            return true;
        }

        private static void BuildCells()
        {
            for (int i = 0; i < PerPage; i++)
            {
                int col = i % Cols;
                int row = i / Cols;
                float x = col * (CellSize + CellGap);
                float y = (Rows - 1 - row) * (CellSize + CellGap);

                RectTransform cell = UiFactory.Create(s_grid, "Cell" + i, ProcSprites.Plate,
                    new Color(0.10f, 0.11f, 0.15f, 0.92f));
                cell.anchorMin = new Vector2(0f, 0f);
                cell.anchorMax = new Vector2(0f, 0f);
                cell.pivot = new Vector2(0f, 0f);
                cell.sizeDelta = new Vector2(CellSize, CellSize);
                cell.anchoredPosition = new Vector2(x, y);
                cell.GetComponent<Image>().preserveAspect = false;

                Image frame = UiFactory.Create(cell, "Frame", ProcSprites.Frame,
                    new Color(0.25f, 0.95f, 0.85f, 0.9f)).GetComponent<Image>();
                frame.rectTransform.anchorMin = Vector2.zero;
                frame.rectTransform.anchorMax = Vector2.one;
                frame.rectTransform.offsetMin = new Vector2(-2f, -2f);
                frame.rectTransform.offsetMax = new Vector2(2f, 2f);
                frame.preserveAspect = false;

                s_cells.Add(cell.gameObject);
                s_frames.Add(frame);
            }
        }

        // ------------------------------------------------------------------ rendering

        private static void Refresh()
        {
            if (s_root == null)
            {
                return;
            }

            EnsureLists();
            int start = s_page * PerPage;

            for (int i = 0; i < PerPage; i++)
            {
                RectTransform cell = (RectTransform)s_cells[i].transform;
                Clear(cell, "Name");
                Clear(cell, "Portrait");
                Clear(cell, "Banner");
                Clear(cell, "Icon");

                int seedIndex = start + i;
                s_frames[i].enabled = i == s_selected;
                if (seedIndex >= s_seeds.Count)
                {
                    continue;
                }

                SeedInfo seed = s_seeds[seedIndex];
                BuildCellContent(cell, seed, i);
            }

            if (s_title != null)
            {
                s_title.text = "日历预览  第 " + (s_page + 1) + "/" + PageCount + " 页  " +
                               "（" + Scenarios[s_scenario] + "）";
            }

            if (s_hint != null)
            {
                SeedInfo selected = SelectedSeed();
                string offset = selected == null
                    ? "-"
                    : Layout.Format(Layout.CropOffset(selected.Id).x) + "," +
                      Layout.Format(Layout.CropOffset(selected.Id).y);
                s_hint.text = "选中：" + (selected == null ? "-" : selected.Id) + "  偏移 " + offset +
                              "\n←/→/↑/↓ 选择   A/D 左右  W/S 上下   R 复位该作物   PgUp/PgDn 翻页" +
                              "   B 生日   N 节日   C 清空   空格 切换情境   Tab 返回";
            }
        }

        private static void BuildCellContent(RectTransform cell, SeedInfo seed, int index)
        {
            Sprite portrait = ScenarioPortrait(); 
            if (portrait != null)
            {
                Image image = UiFactory.Create(cell, "Portrait", portrait, Color.white).GetComponent<Image>();
                Anchor(image.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 2f),
                    new Vector2(96f, 96f));
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            Sprite banner = FestivalSprite();
            if (banner != null)
            {
                Image image = UiFactory.Create(cell, "Banner", banner, Color.white).GetComponent<Image>();
                Anchor(image.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-2f, 2f),
                    new Vector2(44f, 44f));
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            RectTransform pivot = UiFactory.Create(cell, "Icon", null, new Color(0f, 0f, 0f, 0f));
            pivot.anchorMin = new Vector2(0.5f, 0.5f);
            pivot.anchorMax = new Vector2(0.5f, 0.5f);
            pivot.pivot = new Vector2(0.5f, 0.5f);
            pivot.sizeDelta = Vector2.zero;

            Image icon = UiFactory.Create(pivot, "Sprite", SeedSprite(seed), Color.white).GetComponent<Image>();
            icon.raycastTarget = false;

            Font font = EditorUi.PanelFont();
            Text plus = UiFactory.CreateText(pivot, "Plus", font, Mathf.RoundToInt(Layout.CalStarSize), new Color(1f, 0.9f, 0.25f, 1f));
            plus.text = "+2";
            plus.rectTransform.sizeDelta = new Vector2(Layout.CalStarSize * 2f, Layout.CalStarSize * 1.6f);

            CalendarInject.PlaceCropIcon(pivot, icon, plus, seed.Id, banner != null, CellSize * 0.5f);

            Text name = UiFactory.CreateText(cell, "Name", font, 15, new Color(0.78f, 0.80f, 0.86f, 1f));
            Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, -4f),
                new Vector2(CellSize - 12f, 20f));
            name.text = SeedName(seed);
            name.alignment = TextAnchor.UpperLeft;
        }

        private static void ApplyScenario()
        {
            switch (s_scenario)
            {
                case 0:
                    BirthdayNpc = null;
                    FestivalIcon = null;
                    break;
                case 1:
                    BirthdayNpc = s_npcs.Count > 0 ? s_npcs[0] : null;
                    FestivalIcon = null;
                    break;
                case 2:
                    BirthdayNpc = null;
                    FestivalIcon = s_festivals.Count > 0 ? s_festivals[0] : null;
                    break;
                default:
                    BirthdayNpc = s_npcs.Count > 0 ? s_npcs[0] : null;
                    FestivalIcon = s_festivals.Count > 0 ? s_festivals[0] : null;
                    break;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void Select(int index)
        {
            s_selected = Mathf.Clamp(index, 0, PerPage - 1);
            Refresh();
        }

        private static SeedInfo SelectedSeed()
        {
            int index = s_page * PerPage + s_selected;
            return index >= 0 && index < s_seeds.Count ? s_seeds[index] : null;
        }

        private static void Clear(RectTransform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static string SeedName(SeedInfo seed)
        {
            try
            {
                if (seed.CropOutputs != null && seed.CropOutputs.Length > 0)
                {
                    string title = seed.CropOutputs[0].itemRef != null ? seed.CropOutputs[0].itemRef.Title : null;
                    if (!string.IsNullOrEmpty(title))
                    {
                        return title;
                    }
                }
            }
            catch
            {
                // fall through to the id
            }

            return seed.Id;
        }

        private static Sprite SeedSprite(SeedInfo seed)
        {
            try
            {
                if (seed.CropOutputs != null && seed.CropOutputs.Length > 0)
                {
                    Sprite item = seed.CropOutputs[0].itemRef != null ? seed.CropOutputs[0].itemRef.UiSpriteAsset?.Asset : null;
                    if (item != null)
                    {
                        return item;
                    }
                }

                return seed.GetLevelSprite(seed.MatureLevel, 0);
            }
            catch
            {
                return null;
            }
        }

        private static Sprite ScenarioPortrait()
        {
            return string.IsNullOrEmpty(BirthdayNpc) ? null : IconOf("icon_calendar_" + BirthdayNpc);
        }

        private static Sprite FestivalSprite()
        {
            return string.IsNullOrEmpty(FestivalIcon) ? null : IconOf(FestivalIcon);
        }

        private static Sprite IconOf(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            if (s_icons.TryGetValue(url, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = null;
            try
            {
                sprite = DolocAPI.LoadSprite(url);
            }
            catch
            {
                sprite = null;
            }

            s_icons[url] = sprite;
            return sprite;
        }

        private static string Next(List<string> values, string current)
        {
            if (values.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(current))
            {
                return values[0];
            }

            int index = values.IndexOf(current);
            if (index < 0)
            {
                return values[0];
            }

            return index + 1 < values.Count ? values[index + 1] : null;
        }

        private static void EnsureLists()
        {
            if (s_seeds.Count == 0)
            {
                List<SeedInfo> seeds = DolocConfig.Tables.TbSeed.DataList;
                for (int i = 0; i < seeds.Count; i++)
                {
                    if (seeds[i] != null && !string.IsNullOrEmpty(seeds[i].Id))
                    {
                        s_seeds.Add(seeds[i]);
                    }
                }
            }

            if (s_npcs.Count == 0)
            {
                List<DolocTown.Config.Calendar.DateEventInfo> events = DolocConfig.Tables.TbDateEvent.DataList;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] == null || events[i].Type != SpecialEventType.Birthday)
                    {
                        continue;
                    }

                    string id = events[i].Id;
                    if (!string.IsNullOrEmpty(id) && IconOf("icon_calendar_" + id) != null && !s_npcs.Contains(id))
                    {
                        s_npcs.Add(id);
                    }
                }
            }

            if (s_festivals.Count == 0)
            {
                List<DolocTown.Config.Calendar.DateEventInfo> events = DolocConfig.Tables.TbDateEvent.DataList;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] == null || events[i].Type != SpecialEventType.Festival)
                    {
                        continue;
                    }

                    string url = events[i].IconAsset != null ? events[i].IconAsset.AssetUrl : null;
                    if (string.IsNullOrEmpty(url) || s_festivals.Contains(url))
                    {
                        continue;
                    }

                    if (IconOf(url) != null)
                    {
                        s_festivals.Add(url);
                    }
                }
            }
        }
    }
}
