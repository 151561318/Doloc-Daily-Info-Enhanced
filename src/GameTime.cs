using System;
using DolocTown;
using DolocTown.GameData;
using UnityEngine;

namespace SomeOptimizations
{
    internal static class GameTime
    {
        internal const int MinutesPerDay = 1440;

        internal static bool Ready
        {
            get
            {
                try
                {
                    return DolocAPI.IsDataLoaded && DolocAPI.archiveHandle != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static DateInfo Now => DolocAPI.archiveHandle.DateNow;

        internal static int Today => Now.TotalDays;

        internal static int AbsMinuteNow => Now.TotalDays * MinutesPerDay + Now.Hour * 60 + Now.Minute;

        /// <summary>Maps a calendar day-of-month of the currently displayed month onto the absolute day counter.</summary>
        internal static int DayOfMonthToAbsDay(int dayOfMonth) => Now.TotalDays + (dayOfMonth - Now.Day);

        internal static string Clock(int minutes)
        {
            minutes %= MinutesPerDay;
            if (minutes < 0)
            {
                minutes += MinutesPerDay;
            }

            int hour = minutes / 60;
            int minute = minutes % 60;
            return Pad(hour) + ":" + Pad(minute);
        }

        /// <summary>
        /// Vanilla rounds crop display down to whole five minute steps (the game clock only has
        /// five minute resolution). "Show exact time" reveals the interpolated minute instead.
        /// </summary>
        internal static string FormatMinute(float absMinute, bool exact)
        {
            int value = exact
                ? Mathf.FloorToInt(absMinute)
                : Mathf.CeilToInt(absMinute / 5f) * 5;

            // A crop that ripens exactly when the clock rolls over is listed on the day that ends,
            // so it must read "24:00" there rather than "00:00" of a day it is not shown on.
            if (value == MinutesPerDay)
            {
                return "24:00";
            }

            return Clock(value);
        }

        internal static string Pad(int value) => value < 10 ? "0" + value : value.ToString();
    }
}

