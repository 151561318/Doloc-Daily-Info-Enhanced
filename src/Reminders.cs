using System;
using System.Collections.Generic;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Calendar;
using DolocTown.Config.Mission;
using DolocTown.GameData;
using UnityEngine;

namespace SomeOptimizations
{
    internal enum BadgeKind
    {
        Pike = 0,
        Birthday = 1,
        Merchant = 2,
        Crop = 3,
        Film = 4,
        Festival = 5
    }

    internal sealed class Badge
    {
        internal BadgeKind Kind;
        internal string Key;
        internal Sprite Icon;
        internal bool Star;
        internal int ExtraKinds;
        internal bool Glow;
        internal bool Heart;
        internal string Hover;
    }

    /// <summary>
    /// Builds the ordered list of things worth showing under the clock: special pike visit,
    /// birthdays, travelling merchants, maturing crops and festivals - in exactly that order.
    /// </summary>
    internal static class Reminders
    {
        internal const string PikeMission = "ruinedcity_main_1";

        private static readonly string[] PortMerchants = { "shylock", "paiea", "quipu", "kenenimuu" };

        private static readonly Dictionary<string, string> MerchantFaction = new Dictionary<string, string>
        {
            { "shylock", "vulture" },
            { "paiea", "kontiki" },
            { "quipu", "cerro_rico" },
            { "kenenimuu", "skychild" }
        };

        private static readonly Dictionary<string, Sprite> s_npcIcons = new Dictionary<string, Sprite>();

        private static int s_visitDay = -1;
        private static readonly HashSet<string> s_visitingToday = new HashSet<string>();

        /// <summary>F10 in the layout editor: show one of every kind so the row can be tuned anytime.</summary>
        internal static bool DemoMode;

        internal static void Build(List<Badge> list)
        {
            list.Clear();
            if (!GameTime.Ready)
            {
                return;
            }

            DateInfo now = GameTime.Now;
            List<DateEventInfo> events = TodayEvents(now);

            AddPike(list);
            AddBirthdays(list, events);
            AddMerchants(list, now);
            AddCrops(list);
            AddFilm(list);
            AddFestivals(list, events, now);

            if ((DemoMode || ModConfig.DemoBadges) && list.Count == 0)
            {
                AddDemo(list);
            }
        }

        private static void AddDemo(List<Badge> list)
        {
            try
            {
                list.Add(new Badge
                {
                    Kind = BadgeKind.Pike,
                    Key = "demo:pike",
                    Icon = NpcIcon("pike"),
                    Glow = true,
                    Hover = "示例：派克（高亮）"
                });
                list.Add(new Badge
                {
                    Kind = BadgeKind.Birthday,
                    Key = "demo:birthday",
                    Icon = NpcIcon("kasia"),
                    Heart = true,
                    Hover = "示例：生日（带爱心）"
                });
                list.Add(new Badge
                {
                    Kind = BadgeKind.Merchant,
                    Key = "demo:merchant",
                    Icon = NpcIcon("licca"),
                    Hover = "示例：游商"
                });
                list.Add(new Badge
                {
                    Kind = BadgeKind.Crop,
                    Key = "demo:crop",
                    Icon = CropScan.SeedIcon("seed_endyam"),
                    Star = true,
                    ExtraKinds = 2,
                    Hover = "示例：作物（多种）"
                });
                list.Add(new Badge
                {
                    Kind = BadgeKind.Film,
                    Key = "demo:film",
                    Icon = FilmScan.Icon(),
                    Hover = "示例：农场的塑料薄膜耐久度低于20小时。"
                });
                list.Add(new Badge
                {
                    Kind = BadgeKind.Festival,
                    Key = "demo:festival",
                    Icon = DolocAPI.LoadSprite("节日角标-鲑鱼节"),
                    Hover = "示例：节日"
                });
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Reminders.AddDemo");
            }
        }

        private static void AddPike(List<Badge> list)
        {
            if (ModConfig.DisablePikeSpecial)
            {
                return;
            }

            bool listening;
            try
            {
                listening = DolocAPI.IsMissionListening(PikeMission);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "pike mission probe");
                return;
            }

            if (!listening)
            {
                // Mission finished (or not started): forget the arrival day so the next time it
                // starts listening is treated as a new arrival.
                ModState.ClearPikeDay();
                return;
            }

            int day = ModState.PikeDay;
            if (day <= 0)
            {
                // The mission keeps "listening" until the player finally talks to him, so the flag
                // alone cannot tell arrival day from a week later. Stamp the day the moment it turns
                // on - but only while he is actually on the map, otherwise loading an old save into
                // a day he is already gone would raise a bogus reminder.
                if (!PikeInTown())
                {
                    return;
                }

                day = GameTime.Today;
                ModState.SetPikeDay(day);
            }

            if (day != GameTime.Today)
            {
                // He came, nobody talked, and the day is over: stop nagging.
                return;
            }

            list.Add(new Badge
            {
                Kind = BadgeKind.Pike,
                Key = "pike",
                Icon = NpcIcon("pike"),
                Glow = true,
                Hover = "派克来到了小镇"
            });
        }

        /// <summary>True while Pike is somewhere in town rather than parked in the off-map scene.</summary>
        private static bool PikeInTown()
        {
            try
            {
                if (DolocAPI.QueryNpc("pike", out Npc npc) && npc != null)
                {
                    return !npc.IsAtVoidScene;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "pike npc probe");
            }

            return false;
        }

        private static void AddBirthdays(List<Badge> list, List<DateEventInfo> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                DateEventInfo ev = events[i];
                if (ev == null || ev.Type != SpecialEventType.Birthday)
                {
                    continue;
                }

                string npcId = ev.Id;

                // Only giftable townsfolk get a birthday reminder; background villagers still
                // keep their vanilla calendar portrait.
                if (DolocAPI.GetNpcLikingLvLimit(npcId) <= 0)
                {
                    continue;
                }

                if (Tracking.GiftedToday(npcId))
                {
                    continue;
                }

                if (!ModConfig.ShowFullLikingBirthday && IsFullLiking(npcId))
                {
                    continue;
                }

                list.Add(new Badge
                {
                    Kind = BadgeKind.Birthday,
                    Key = "birthday:" + npcId,
                    Icon = NpcIcon(npcId),
                    Heart = !ModConfig.HideBirthdayHeart,
                    Hover = NpcTitle(npcId) + "的生日"
                });
            }
        }

        private static void AddMerchants(List<Badge> list, DateInfo now)
        {
            EnsureVisits(now);

            // 长夜节期间莉卡本来就在镇上（最后三天经营的是长夜集市，不是游商摊位）。
            if (!ModConfig.DisableLicca && !Festivals.SilenceLicca(now))
            {
                AddMerchant(list, "licca", "莉卡来到了小镇");
            }

            for (int i = 0; i < PortMerchants.Length; i++)
            {
                string merchant = PortMerchants[i];
                if (Tracking.TalkedToday(merchant))
                {
                    continue;
                }

                // Once a merchant has settled in the trading port they live in town; no reminder.
                if (IsJoined(merchant))
                {
                    continue;
                }

                bool canMoveIn = CanMoveIn(merchant);
                bool visiting = s_visitingToday.Contains(merchant);

                if (visiting && !ModConfig.DisablePortMerchant)
                {
                    list.Add(new Badge
                    {
                        Kind = BadgeKind.Merchant,
                        Key = "merchant:" + merchant,
                        Icon = NpcIcon(merchant),
                        Glow = canMoveIn && !ModConfig.DisableMerchantCanMoveIn,
                        Hover = canMoveIn && !ModConfig.DisableMerchantCanMoveIn
                            ? NpcTitle(merchant) + "可以入驻商埠了！"
                            : NpcTitle(merchant) + "来到了小镇"
                    });
                    continue;
                }

                if (canMoveIn && !ModConfig.DisableMerchantCanMoveIn)
                {
                    list.Add(new Badge
                    {
                        Kind = BadgeKind.Merchant,
                        Key = "movein:" + merchant,
                        Icon = NpcIcon(merchant),
                        Glow = true,
                        Hover = NpcTitle(merchant) + "可以入驻商埠了！"
                    });
                }
            }
        }

        private static void AddMerchant(List<Badge> list, string npcId, string hover)
        {
            if (Tracking.TalkedToday(npcId) || !s_visitingToday.Contains(npcId))
            {
                return;
            }

            list.Add(new Badge
            {
                Kind = BadgeKind.Merchant,
                Key = "merchant:" + npcId,
                Icon = NpcIcon(npcId),
                Hover = hover
            });
        }

        private static void AddCrops(List<Badge> list)
        {
            List<CropBatch> today = CropScan.Today;
            if (today == null || today.Count == 0)
            {
                return;
            }

            CropBatch first = today[0];
            HashSet<string> seeds = new HashSet<string>();
            for (int i = 0; i < today.Count; i++)
            {
                CropBatch batch = today[i];
                if (batch == null)
                {
                    continue;
                }

                seeds.Add(batch.SeedId);
            }

            string hover;
            if (first.Ripe > 0)
            {
                // Standing ripe and unharvested: the time of day is meaningless here (the remembered
                // moment is when it was first noticed), so only report the count that is ready now.
                hover = first.Name + " x" + first.Ripe + " 已成熟";
                if (seeds.Count > 1)
                {
                    hover += "（还有 " + (seeds.Count - 1) + " 种作物）";
                }
            }
            else
            {
                hover = first.Name + " x" + first.Count + "，预计 "
                        + GameTime.FormatMinute(CropScan.MinuteOfDay(first), ModConfig.ShowExactTime) + " 成熟";
                if (seeds.Count > 1)
                {
                    hover += "（共 " + seeds.Count + " 种作物）";
                }
            }

            list.Add(new Badge
            {
                Kind = BadgeKind.Crop,
                Key = "crop:" + first.SeedId,
                Icon = first.Icon,
                Star = seeds.Count > 1,
                ExtraKinds = seeds.Count - 1,
                Glow = CropScan.AnyRipe && !ModConfig.DisableMatureHighlight,
                Hover = hover
            });
        }

        /// <summary>
        /// One badge for every farm that has a plastic film about to wear out, whatever the species
        /// or the count - the hover text names the farms instead of counting planters.
        /// </summary>
        private static void AddFilm(List<Badge> list)
        {
            if (ModConfig.DisableFilmWarning || !FilmScan.Any)
            {
                return;
            }

            list.Add(new Badge
            {
                Kind = BadgeKind.Film,
                Key = "film",
                Icon = FilmScan.Icon(),
                Hover = FilmScan.Hover()
            });
        }

        private static void AddFestivals(List<Badge> list, List<DateEventInfo> events, DateInfo now)
        {
            for (int i = 0; i < events.Count; i++)
            {
                DateEventInfo ev = events[i];
                if (ev == null || ev.Type != SpecialEventType.Festival)
                {
                    continue;
                }

                Sprite icon = null;
                try
                {
                    icon = ev.IconAsset != null ? ev.IconAsset.Asset : null;
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "festival icon " + ev.Id);
                }

                // Some festivals only need the player at a certain hour on certain days (the long
                // night story starts at 18:00, the mushroom judging closes at 18:00); those badges
                // pulse so the moment is not missed.
                bool highlight;
                try
                {
                    highlight = Festivals.HighlightEvent(ev.Id, now);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "festival window " + ev.Id);
                    highlight = false;
                }

                string title = TrimStar(ev.Title);
                list.Add(new Badge
                {
                    Kind = BadgeKind.Festival,
                    Key = "festival:" + ev.Id,
                    Icon = icon,
                    Glow = highlight,
                    Hover = string.IsNullOrEmpty(ev.Description) ? title : title + "\n" + ev.Description
                });
            }
        }

        internal static List<DateEventInfo> TodayEvents(DateInfo now)
        {
            List<DateEventInfo> result = new List<DateEventInfo>();
            try
            {
                CalendarInfo month = DolocConfig.Tables.TbCalendar.GetOrDefault(now.Month);
                if (month?.Date_Index == null ||
                    !month.Date_Index.TryGetValue(now.Day, out CalendarDateInfo day) ||
                    day?.Events_Ref == null)
                {
                    return result;
                }

                for (int i = 0; i < day.Events_Ref.Length; i++)
                {
                    DateEventInfo ev = day.Events_Ref[i];
                    if (ev != null && IsUnlocked(ev))
                    {
                        result.Add(ev);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Reminders.TodayEvents");
            }

            return result;
        }

        private static bool IsUnlocked(DateEventInfo ev)
        {
            if (ev.DefaultUnlock)
            {
                return true;
            }

            try
            {
                return DolocAPI.archiveHandle.cityData.calendarManager.CheckFestivalUnlocked(ev.Id);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFullLiking(string npcId)
        {
            try
            {
                if (DolocAPI.QueryNpcLikingInfo(npcId, out Liking liking) && liking != null)
                {
                    return liking.likingValue >= DolocAPI.GlobalParameter.LikingCeiling;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "liking probe " + npcId);
            }

            return false;
        }

        private static bool IsJoined(string merchant)
        {
            if (!MerchantFaction.TryGetValue(merchant, out string factionId))
            {
                return false;
            }

            try
            {
                return DolocAPI.QueryTreatyPortFaction(factionId, out TreatyPortFaction faction) &&
                       faction != null && faction.IsJoin;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "faction join probe " + factionId);
                return false;
            }
        }

        private static bool CanMoveIn(string merchant)
        {
            if (!MerchantFaction.TryGetValue(merchant, out string factionId))
            {
                return false;
            }

            try
            {
                if (!DolocAPI.QueryTreatyPortFaction(factionId, out TreatyPortFaction faction) || faction == null)
                {
                    return false;
                }

                if (faction.IsJoin)
                {
                    return false;
                }

                TreatyPortFactionInfo info = faction.Proto;
                if (info == null || !info.UnlockInDemo)
                {
                    return false;
                }

                return faction.ReputationValue >= info.SettledReputation;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "faction probe " + factionId);
                return false;
            }
        }

        /// <summary>Recomputed once per in-game day: who is around town at any point today.</summary>
        private static void EnsureVisits(DateInfo now)
        {
            if (s_visitDay == now.TotalDays)
            {
                return;
            }

            s_visitDay = now.TotalDays;
            s_visitingToday.Clear();

            var weather = DolocAPI.archiveHandle.LocalWeatherType;
            string[] merchants = { "licca", "shylock", "paiea", "quipu" };
            for (int i = 0; i < merchants.Length; i++)
            {
                if (VisitsToday(merchants[i], now, weather))
                {
                    s_visitingToday.Add(merchants[i]);
                }
            }
        }

        private static bool VisitsToday(string npcId, DateInfo now, DolocTown.Config.Weather.WeatherType weather)
        {
            try
            {
                if (DolocAPI.QueryNpc(npcId, out Npc npc) && npc != null && !npc.IsAtVoidScene)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "npc probe " + npcId);
            }

            try
            {
                if (!DolocAPI.QueryNpc(npcId, out Npc npc2) || npc2 == null || npc2.proto == null)
                {
                    return false;
                }

                DateInfo probe = now.Copy();
                for (int hour = 6; hour < 24; hour++)
                {
                    probe.Hour = hour;
                    probe.Minute = 0;
                    NpcScheduleResult result = npc2.proto.QueryScheduleResult(new NpcScheduleParams(probe, weather));
                    if (!string.IsNullOrEmpty(result.sceneName))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "schedule probe " + npcId);
            }

            return false;
        }

        internal static string DebugMerchants()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("merchants");
            string[] ids = { "licca", "shylock", "paiea", "quipu", "pike" };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                sb.Append(" | ").Append(id);
                try
                {
                    if (!DolocAPI.QueryNpc(id, out Npc npc) || npc == null)
                    {
                        sb.Append(" missing");
                        continue;
                    }

                    sb.Append(" void=").Append(npc.IsAtVoidScene)
                      .Append(" sched=").Append(npc.useSchedule)
                      .Append(" scene=").Append(npc.sceneName)
                      .Append(" ovr=").Append(npc.overrideMarkPoint);

                    DateInfo probe = GameTime.Now.Copy();
                    string firstScene = "-";
                    int firstHour = -1;
                    DolocTown.Config.Weather.WeatherType weather = DolocAPI.archiveHandle.LocalWeatherType;
                    for (int hour = 0; hour < 24; hour++)
                    {
                        probe.Hour = hour;
                        probe.Minute = 0;
                        NpcScheduleResult result = npc.proto.QueryScheduleResult(new NpcScheduleParams(probe, weather));
                        if (!string.IsNullOrEmpty(result.sceneName))
                        {
                            firstScene = result.sceneName;
                            firstHour = hour;
                            break;
                        }
                    }

                    sb.Append(" at=").Append(firstHour).Append(':').Append(firstScene)
                      .Append(" todayVisit=").Append(s_visitingToday.Contains(id));
                }
                catch (Exception ex)
                {
                    sb.Append(" err=").Append(ex.GetType().Name);
                }
            }

            foreach (KeyValuePair<string, string> pair in MerchantFaction)
            {
                sb.Append(" | ").Append(pair.Key).Append('/').Append(pair.Value);
                try
                {
                    if (DolocAPI.QueryTreatyPortFaction(pair.Value, out TreatyPortFaction faction) && faction != null)
                    {
                        TreatyPortFactionInfo info = faction.Proto;
                        sb.Append(" join=").Append(faction.IsJoin)
                          .Append(" rep=").Append(faction.ReputationValue)
                          .Append(" settled=").Append(info != null ? info.SettledReputation : -1)
                          .Append(" demo=").Append(info != null && info.UnlockInDemo)
                          .Append(" dlg=").Append(info != null ? (info.Dialogue ?? "-") : "-")
                          .Append(" canMove=").Append(CanMoveIn(pair.Key));
                    }
                    else
                    {
                        sb.Append(" missing");
                    }
                }
                catch (Exception ex)
                {
                    sb.Append(" err=").Append(ex.GetType().Name);
                }
            }

            return sb.ToString();
        }

        internal static string NpcTitle(string npcId)
        {
            try
            {
                string title = DolocAPI.GetNpcTitle(npcId);
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }
            catch
            {
                // fall through to the raw id
            }

            return npcId;
        }

        internal static Sprite NpcIcon(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return null;
            }

            if (s_npcIcons.TryGetValue(npcId, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = null;
            try
            {
                sprite = DolocAPI.LoadSprite("icon_calendar_" + npcId);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "npc icon " + npcId);
            }

            s_npcIcons[npcId] = sprite;
            return sprite;
        }

        internal static string TrimStar(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            return title[0] == '*' ? title.Substring(1) : title;
        }
    }
}
