using System;
using System.Collections.Generic;
using DolocTown.Config;
using DolocTown.Config.Calendar;
using DolocTown.GameData;

namespace SomeOptimizations
{
    /// <summary>
    /// Multi-day festival dates, read straight out of the calendar table at runtime.
    ///
    /// Nothing about the actual dates is hard-coded: a festival's span is simply every day of this
    /// month that carries an event of that family, so a game update that moves the dates (or renames
    /// a sub-event) keeps working. The only thing pinned here is which day of the span a reminder
    /// behaviour starts on.
    /// </summary>
    internal static class Festivals
    {
        /// <summary>长夜节: 备光 / 驱祟 / 涤尘 / 传薪 / 游园 / 守岁, seven days, all sharing this prefix.</summary>
        internal const string EverNight = "festival_evernight";

        /// <summary>蘑菇节: 采摘期 + 评选日.</summary>
        private const string Mushroom = "festival_mushroom";

        internal sealed class Span
        {
            internal string Family;
            internal int FirstDay;
            internal int LastDay;

            internal bool Contains(int day)
            {
                return day >= FirstDay && day <= LastDay;
            }
        }

        private static readonly Dictionary<string, Span> s_spans = new Dictionary<string, Span>();
        private static int s_month = -1;

        /// <summary>
        /// 长夜节期间莉卡一直待在镇上，最后三天更是固定经营的长夜集市（不是游商摊位），
        /// 这时候再提示「莉卡来到了小镇」只会碍事。
        /// </summary>
        internal static bool SilenceLicca(DateInfo now)
        {
            Span span = SpanOf(EverNight, now);
            if (span == null || !span.Contains(now.Day))
            {
                return false;
            }

            if (ModConfig.DisableLiccaEverNight)
            {
                // 设置项：整个长夜节都不提醒。
                return true;
            }

            return now.Day > span.LastDay - 3;
        }

        /// <summary>节日角标是否该在此时高亮（有剧情/活动需要玩家在特定时段到场）。</summary>
        internal static bool HighlightEvent(string eventId, DateInfo now)
        {
            if (ModConfig.DisableFestivalWindow || string.IsNullOrEmpty(eventId))
            {
                return false;
            }

            if (eventId.StartsWith(EverNight, StringComparison.OrdinalIgnoreCase))
            {
                return EverNightWindow(now);
            }

            if (eventId.StartsWith(Mushroom, StringComparison.OrdinalIgnoreCase))
            {
                return MushroomWindow(now);
            }

            return false;
        }

        /// <summary>
        /// 长夜节只有第 4 天（烈旱季 25 日，传薪）和最后一天（28 日，守岁）在 18:00 之后有必须到场的活动；
        /// 26/27 日是游园/长夜集市，没有定时活动，不闪。
        /// </summary>
        private static bool EverNightWindow(DateInfo now)
        {
            Span span = SpanOf(EverNight, now);
            if (span == null || !span.Contains(now.Day))
            {
                return false;
            }

            int index = now.Day - span.FirstDay;
            int last = span.LastDay - span.FirstDay;
            bool hasEveningEvent = index == 3 || index == last;
            return hasEveningEvent && now.Hour >= 18;
        }

        /// <summary>蘑菇节最后一天（深雨季 18 日）9:00-18:00 交蘑菇登记。</summary>
        private static bool MushroomWindow(DateInfo now)
        {
            Span span = SpanOf(Mushroom, now);
            if (span == null || now.Day != span.LastDay)
            {
                return false;
            }

            return now.Hour >= 9 && now.Hour < 18;
        }

        private static Span SpanOf(string family, DateInfo now)
        {
            if (now.Month != s_month)
            {
                s_spans.Clear();
                s_month = now.Month;
            }

            if (s_spans.TryGetValue(family, out Span cached))
            {
                return cached;
            }

            Span span = Build(family, now.Month);
            s_spans[family] = span;
            return span;
        }

        private static Span Build(string family, int month)
        {
            try
            {
                CalendarInfo info = DolocConfig.Tables.TbCalendar.GetOrDefault(month);
                if (info?.Date_Index == null)
                {
                    return null;
                }

                Span span = null;
                foreach (KeyValuePair<int, CalendarDateInfo> pair in info.Date_Index)
                {
                    if (!HasFamily(pair.Value, family))
                    {
                        continue;
                    }

                    if (span == null)
                    {
                        span = new Span { Family = family, FirstDay = pair.Key, LastDay = pair.Key };
                        continue;
                    }

                    if (pair.Key < span.FirstDay)
                    {
                        span.FirstDay = pair.Key;
                    }

                    if (pair.Key > span.LastDay)
                    {
                        span.LastDay = pair.Key;
                    }
                }

                return span;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Festivals.Build " + family);
                return null;
            }
        }

        private static bool HasFamily(CalendarDateInfo day, string family)
        {
            if (day?.Events_Ref == null)
            {
                return false;
            }

            for (int i = 0; i < day.Events_Ref.Length; i++)
            {
                DateEventInfo ev = day.Events_Ref[i];
                if (ev != null && !string.IsNullOrEmpty(ev.Id) &&
                    ev.Id.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
