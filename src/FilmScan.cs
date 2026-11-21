using System;
using System.Collections.Generic;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Item;
using DolocTown.GameData;
using UnityEngine;

namespace SomeOptimizations
{
    /// <summary>
    /// "Which farm has a plastic film that is about to wear out?"
    ///
    /// Only the outdoor farms themselves are scanned. Planters that stand inside a building
    /// (greenhouse, container, cellar, ...) belong to that building's own room, so they are never
    /// reached here - which is what the design asked for.
    ///
    /// The film's durability is stored as a plain counter of bad-weather time units: one point is
    /// consumed per time unit of acid rain or scorch sun, so the remaining time is
    /// <c>counter * TU2Min</c> minutes. It is deliberately NOT derived from
    /// <c>ProtectedPercent</c>: that divides by <c>ProtectorCapacity = 240</c>, which is a display
    /// bug - a fresh film holds 576 points (two full in-game days), so the bar sits pinned at 100%
    /// until the film is already down to its last 20 hours.
    /// </summary>
    internal static class FilmScan
    {
        /// <summary>Warn once a film has less than this many in-game hours left.</summary>
        private const float WarnHours = 20f;

        private static readonly List<string> s_farms = new List<string>();
        private static readonly Dictionary<string, float> s_minHours = new Dictionary<string, float>();
        private static Sprite s_icon;
        private static bool s_iconProbed;
        private static float s_next;
        private static float s_tuMinutes;

        /// <summary>Display names of the farms that hold at least one film below the threshold.</summary>
        internal static List<string> LowFarms => s_farms;

        internal static bool Any => s_farms.Count > 0;

        internal static void Update()
        {
            if (!GameTime.Ready)
            {
                s_farms.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < s_next)
            {
                return;
            }

            s_next = now + 1f;
            try
            {
                Rebuild();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "FilmScan.Rebuild");
            }
        }

        private static void Rebuild()
        {
            s_farms.Clear();
            s_minHours.Clear();

            ArchiveDataHandle handle = DolocAPI.archiveHandle;
            if (handle == null)
            {
                return;
            }

            foreach (TemplateRoomOutdoor farm in handle.AllFarms)
            {
                if (farm == null)
                {
                    continue;
                }

                EquipmentManager manager = farm.DM_equipment;
                if (manager == null)
                {
                    continue;
                }

                float lowest = LowestHours(manager);
                if (lowest >= float.MaxValue)
                {
                    // no film on this farm at all
                    continue;
                }

                string name = FarmName(farm);
                s_minHours[name] = lowest;

                if (lowest < WarnHours && !s_farms.Contains(name))
                {
                    s_farms.Add(name);
                }
            }
        }

        /// <summary>Shortest remaining film time on this farm; <c>float.MaxValue</c> when it has none.</summary>
        private static float LowestHours(EquipmentManager manager)
        {
            float lowest = float.MaxValue;

            foreach (Equipment equipment in manager.AllEquipments)
            {
                if (!(equipment is PlantBasin basin))
                {
                    continue;
                }

                int counter;
                try
                {
                    // PlantBasinSupply is a struct: the property hands back a copy, which is fine for
                    // reading the current durability.
                    counter = basin.Supply.ProtectedCounter;
                }
                catch
                {
                    continue;
                }

                if (counter <= 0)
                {
                    continue;
                }

                float hours = HoursLeft(counter);
                if (hours < lowest)
                {
                    lowest = hours;
                }
            }

            return lowest;
        }

        /// <summary>In-game hours a film with <paramref name="counter"/> points left can still protect.</summary>
        internal static float HoursLeft(int counter)
        {
            return counter * TuMinutes / 60f;
        }

        private static float TuMinutes
        {
            get
            {
                if (s_tuMinutes > 0f)
                {
                    return s_tuMinutes;
                }

                try
                {
                    s_tuMinutes = Mathf.Max(1f, DolocAPI.GlobalParameter.TU2Min);
                }
                catch
                {
                    return 5f;
                }

                return s_tuMinutes;
            }
        }

        /// <summary>Scene title of the farm ("农场" / "旧城农场" / "湿地农场"), with safe fallbacks.</summary>
        private static string FarmName(Room room)
        {
            try
            {
                string title = room.SceneConfig != null ? room.SceneConfig.Title : null;
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "FilmScan.FarmName");
            }

            try
            {
                if (!string.IsNullOrEmpty(room.SceneShortName))
                {
                    return room.SceneShortName;
                }
            }
            catch
            {
                // nothing else to try
            }

            return "农场";
        }

        /// <summary>The film item's own icon, used for the reminder badge.</summary>
        internal static Sprite Icon()
        {
            if (s_iconProbed)
            {
                return s_icon;
            }

            s_iconProbed = true;

            try
            {
                s_icon = DolocAPI.LoadSprite("icon_item_plastic_film");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "FilmScan.Icon");
            }

            if (s_icon == null)
            {
                try
                {
                    ItemInfo item = DolocConfig.Tables.TbItem.GetOrDefault("plastic_film");
                    if (item != null && item.UiSpriteAsset != null)
                    {
                        s_icon = item.UiSpriteAsset.Asset;
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "FilmScan.Icon item");
                }
            }

            return s_icon;
        }

        internal static string Hover()
        {
            List<string> farms = s_farms;
            if (farms.Count == 0)
            {
                return "塑料薄膜耐久度低于20小时。";
            }

            if (farms.Count == 1)
            {
                return farms[0] + "的塑料薄膜耐久度低于20小时。";
            }

            return string.Join("、", farms) + "的塑料薄膜耐久度低于20小时。";
        }

        /// <summary>Diagnostics only: every farm that carries a film, with its lowest remaining hours.</summary>
        internal static string DebugSummary()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("films");
            foreach (KeyValuePair<string, float> pair in s_minHours)
            {
                sb.Append(' ').Append(pair.Key).Append('=')
                  .Append(pair.Value.ToString("0.0")).Append("h");
            }

            if (s_farms.Count > 0)
            {
                sb.Append(" low=[").Append(string.Join(",", s_farms)).Append(']');
            }

            return sb.ToString();
        }
    }
}
