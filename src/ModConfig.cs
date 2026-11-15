using System;
using System.Collections.Generic;
using DTMAPI.Abstractions;

namespace SomeOptimizations
{
    /// <summary>Player configurable switches, registered into the DTMAPI config page.</summary>
    internal static class ModConfig
    {
        private static IManifest s_manifest;
        private static IConfigHelper s_config;
        private static Settings s_settings = new Settings();
        private static bool s_ready;

        internal static bool ShowExactTime => s_settings.ShowExactTime;

        /// <summary>Window (minutes) inside which equal species still count as one batch.</summary>
        internal static float SameBatchMinutes
        {
            get
            {
                int value = s_settings.SameBatchMinutes;
                if (value < 1)
                {
                    value = 60;
                }

                if (value > 720)
                {
                    value = 720;
                }

                return value;
            }
        }

        internal static bool DisableMatureHighlight => s_settings.DisableMatureHighlight;

        internal static string BadgeBackground
        {
            get
            {
                string value = s_settings.BadgeBackground;
                return string.IsNullOrEmpty(value) ? "dark" : value;
            }
        }

        internal static bool ShowFullLikingBirthday => s_settings.ShowFullLikingBirthday;

        internal static bool HideBirthdayHeart => s_settings.HideBirthdayHeart;

        internal static bool DisablePikeSpecial => s_settings.DisablePikeSpecial;

        internal static bool DisablePortMerchant => s_settings.DisablePortMerchant;

        internal static bool DisableLicca => s_settings.DisableLicca;

        internal static bool DisableMerchantCanMoveIn => s_settings.DisableMerchantCanMoveIn;

        internal static bool RestoreResinCollectorPriority => s_settings.RestoreResinCollectorPriority;

        internal static bool ShowLayoutEditor => s_settings.ShowLayoutEditor;

        internal static bool DemoBadges => s_settings.DemoBadges;

        internal static int PreviewOnStart => s_settings.PreviewOnStart;

        internal static string EditorKey => KeyName(s_settings.EditorKey, "F7");

        internal static string SaveKey => KeyName(s_settings.SaveKey, "F9");

        internal static string DemoKey => KeyName(s_settings.DemoKey, "F10");

        internal static string DumpKey => KeyName(s_settings.DumpKey, "F11");

        /// <summary>Config values read from an older file can be null; fall back to the default key.</summary>
        private static string KeyName(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) || value.Trim().Length == 0 ? fallback : value.Trim();
        }

        internal static void Bind(IDtmHelper helper)
        {
            s_manifest = helper.ModManifest;
            s_config = helper.Config;

            try
            {
                s_settings = s_config.ReadConfig<Settings>(s_manifest) ?? new Settings();
                s_ready = true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "config read failed; using defaults");
                s_settings = new Settings();
            }

            IDtmConfigMenuApi menu = null;
            try
            {
                menu = helper.ModRegistry.GetApi<IDtmConfigMenuApi>("DTMAPI.ModConfigMenu");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "config menu API lookup failed");
            }

            if (menu == null)
            {
                Log.Warn("DTMAPI.ModConfigMenu not available; running with default settings.");
                return;
            }

            try
            {
                Register(menu);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "config menu registration failed");
            }
        }

        private static string DisplayName()
        {
            try
            {
                string name = s_manifest != null ? s_manifest.Name : null;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "config menu display name");
            }

            return "多洛可日常信息增强";
        }

        private static void Register(IDtmConfigMenuApi menu)
        {
            menu.Register(s_manifest, ResetAll, () => Save(), false);
            menu.SetDisplayName(s_manifest, () => DisplayName());

            menu.AddSectionTitle(s_manifest, () => "作物成熟日期预测");
            menu.AddBoolOption(
                s_manifest,
                () => "显示具体成熟时间",
                () => "勾选后显示按当前生长速度推算的精确分钟（例如 10:21），不勾选则按游戏的时间粒度向上取整到 5 分钟（例如 10:25）。",
                () => s_settings.ShowExactTime,
                value =>
                {
                    s_settings.ShowExactTime = value;
                    Save();
                });
            menu.AddNumberOption(
                s_manifest,
                () => "同批成熟判定阈值（分钟）",
                () => "同种作物成熟时间相差不超过该值时算作同一批，只显示一条记录。默认 60。",
                () => s_settings.SameBatchMinutes,
                value =>
                {
                    s_settings.SameBatchMinutes = (int)Math.Round(value);
                    Save();
                },
                5,
                240,
                5);
            menu.AddBoolOption(
                s_manifest,
                () => "关闭成熟时的图标高亮",
                () => "勾选后作物成熟时，日历下方悬挂的图标不再发出淡黄色高亮。",
                () => s_settings.DisableMatureHighlight,
                value =>
                {
                    s_settings.DisableMatureHighlight = value;
                    Save();
                });
            menu.AddColorPresetOption(
                s_manifest,
                () => "悬挂图标背景颜色",
                () => "调整日历下方悬挂图标背后的底色。",
                () => BadgeBackground,
                value =>
                {
                    s_settings.BadgeBackground = value;
                    Save();
                },
                BadgeBackgrounds.Presets);

            menu.AddSectionTitle(s_manifest, () => "角色生日与节日提醒");
            menu.AddBoolOption(
                s_manifest,
                () => "满好感度角色仍显示生日提醒",
                () => "默认关闭：好感度已满的角色过生日时不再悬挂头像；勾选后照常提醒。",
                () => s_settings.ShowFullLikingBirthday,
                value =>
                {
                    s_settings.ShowFullLikingBirthday = value;
                    Save();
                });
            menu.AddBoolOption(
                s_manifest,
                () => "生日提醒不显示小爱心",
                () => "默认关闭：过生日的角色头像右下角显示一个小爱心；勾选后不显示。",
                () => s_settings.HideBirthdayHeart,
                value =>
                {
                    s_settings.HideBirthdayHeart = value;
                    Save();
                });

            menu.AddSectionTitle(s_manifest, () => "游商到来提醒");
            menu.AddBoolOption(
                s_manifest,
                () => "关闭特殊情况下的派克提醒",
                () => "主线进行到「旧城市废墟 · 和派克聊聊」时不再提醒派克在小镇上。",
                () => s_settings.DisablePikeSpecial,
                value =>
                {
                    s_settings.DisablePikeSpecial = value;
                    Save();
                });
            menu.AddBoolOption(
                s_manifest,
                () => "关闭莉卡到来的提醒",
                () => "莉卡来到小镇时不再悬挂她的头像。",
                () => s_settings.DisableLicca,
                value =>
                {
                    s_settings.DisableLicca = value;
                    Save();
                });
            menu.AddBoolOption(
                s_manifest,
                () => "关闭商埠游商到来的提醒",
                () => "夏洛克、帕伊雅、奇普来到小镇时不再悬挂头像（已入驻的不再提醒）。",
                () => s_settings.DisablePortMerchant,
                value =>
                {
                    s_settings.DisablePortMerchant = value;
                    Save();
                });
            menu.AddBoolOption(
                s_manifest,
                () => "关闭游商可入驻商埠的提醒",
                () => "游商达到入驻标准但尚未入驻时不再提示。",
                () => s_settings.DisableMerchantCanMoveIn,
                value =>
                {
                    s_settings.DisableMerchantCanMoveIn = value;
                    Save();
                });

            menu.AddSectionTitle(s_manifest, () => "其他");
            menu.AddBoolOption(
                s_manifest,
                () => "恢复树脂收集器的互动优先级",
                () => "勾选后树脂收集器回到原本的互动优先级；不勾选时它会优先于重叠的植被被选中。",
                () => s_settings.RestoreResinCollectorPriority,
                value =>
                {
                    s_settings.RestoreResinCollectorPriority = value;
                    Save();
                });

            menu.AddBoolOption(
                s_manifest,
                () => "开启布局编辑器",
                () => "打开后可用方向键微调时钟下方图标与日历图标的尺寸、位置；F9 保存，F11 把数值打印到日志。",
                () => s_settings.ShowLayoutEditor,
                value =>
                {
                    s_settings.ShowLayoutEditor = value;
                    Save();
                });

            menu.AddBoolOption(
                s_manifest,
                () => "显示示例图标（预览用）",
                () => "勾选后，当天没有任何提醒时也会显示五种示意图标，用来预览/调整时钟下方图标的外观。",
                () => s_settings.DemoBadges,
                value =>
                {
                    s_settings.DemoBadges = value;
                    Save();
                });

            menu.AddKeybindOption(
                s_manifest,
                () => "布局编辑器开关键",
                () => "按该键开关布局编辑器（默认 F7）。可填 F1~F12 或单个字母。",
                () => EditorKey,
                value =>
                {
                    s_settings.EditorKey = value;
                    Save();
                });
            menu.AddKeybindOption(
                s_manifest,
                () => "编辑器·保存键",
                () => "编辑器打开时按该键把当前数值写入 state.json（默认 F9）。",
                () => SaveKey,
                value =>
                {
                    s_settings.SaveKey = value;
                    Save();
                });
            menu.AddKeybindOption(
                s_manifest,
                () => "编辑器·示例图标键",
                () => "切换示例徽章（默认 F10）。",
                () => DemoKey,
                value =>
                {
                    s_settings.DemoKey = value;
                    Save();
                });
            menu.AddKeybindOption(
                s_manifest,
                () => "编辑器·打印全部键",
                () => "把全部布局数值打印到日志（默认 F11）。",
                () => DumpKey,
                value =>
                {
                    s_settings.DumpKey = value;
                    Save();
                });
        }

        private static void ResetAll()
        {
            s_settings = new Settings();
            Save();
        }

        private static void Save()
        {
            if (!s_ready || s_config == null || s_manifest == null)
            {
                return;
            }

            try
            {
                s_config.WriteConfig(s_manifest, s_settings);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "config write failed");
            }
        }
    }

    /// <summary>Background colour choices for the hanging badge icons.</summary>
    internal static class BadgeBackgrounds
    {
        internal const string Default = "dark";

        internal static readonly IReadOnlyList<DtmColorPreset> Presets = new List<DtmColorPreset>
        {
            new DtmColorPreset("dark", "深灰", "#1F1F24"),
            new DtmColorPreset("light", "浅灰", "#EBE6DB"),
            new DtmColorPreset("brown", "木色", "#5C4029"),
            new DtmColorPreset("warm", "暖黄", "#D9B86B"),
            new DtmColorPreset("none", "无背景", "#000000")
        };

        internal static UnityEngine.Color Resolve(string id)
        {
            switch (id)
            {
                case "light":
                    return new UnityEngine.Color(0.92f, 0.90f, 0.86f, 0.92f);
                case "brown":
                    return new UnityEngine.Color(0.36f, 0.25f, 0.16f, 0.92f);
                case "warm":
                    return new UnityEngine.Color(0.85f, 0.72f, 0.42f, 0.92f);
                case "none":
                    return new UnityEngine.Color(0f, 0f, 0f, 0f);
                default:
                    return new UnityEngine.Color(0.12f, 0.12f, 0.14f, 0.85f);
            }
        }
    }
}
