using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SomeOptimizations
{
    /// <summary>
    /// Small side-car file for the two things this mod has to remember between sessions: the
    /// "already gifted / already talked to" per in-game day marks, and the pixel layout tuned in
    /// the F8 editor. It lives next to the DTMAPI config, never inside the save file, so removing
    /// the mod leaves the save untouched.
    /// </summary>
    internal static class ModState
    {
        [DataContract]
        private sealed class Data
        {
            [DataMember(Name = "lastDay", Order = 0)]
            public int LastDay = -1;

            [DataMember(Name = "gifts", Order = 1)]
            public Dictionary<string, int> Gifts = new Dictionary<string, int>();

            [DataMember(Name = "talks", Order = 2)]
            public Dictionary<string, int> Talks = new Dictionary<string, int>();

            [DataMember(Name = "layout", Order = 3)]
            public Dictionary<string, string> Layout = new Dictionary<string, string>();

            /// <summary>Per-crop pixel nudges inside the calendar cell: seedId -> "x,y".</summary>
            [DataMember(Name = "cropOffsets", Order = 4)]
            public Dictionary<string, string> CropOffsets = new Dictionary<string, string>();

            /// <summary>
            /// Absolute day the "Pike is in town" mission was first seen listening, so the reminder
            /// can stay on that day only. 0 / negative means "not recorded".
            /// </summary>
            [DataMember(Name = "pikeDay", Order = 5)]
            public int PikeDay = -1;
        }

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(Data));

        private static Data s_data = new Data();
        private static string s_path;

        internal static string Path_ => s_path;

        internal static void Init(string configPath)
        {
            try
            {
                string dir = string.IsNullOrEmpty(configPath)
                    ? null
                    : System.IO.Path.GetDirectoryName(configPath);

                if (string.IsNullOrEmpty(dir))
                {
                    dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DTMAPI", "config");
                }

                s_path = System.IO.Path.Combine(dir, "SomeOptimizations.state.json");
                Load();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "ModState.Init");
            }
        }

        private static void Load()
        {
            if (s_path == null || !File.Exists(s_path))
            {
                return;
            }

            try
            {
                using (FileStream stream = File.OpenRead(s_path))
                {
                    Data loaded = Serializer.ReadObject(stream) as Data;
                    if (loaded != null)
                    {
                        s_data = loaded;
                        if (s_data.Gifts == null)
                        {
                            s_data.Gifts = new Dictionary<string, int>();
                        }

                        if (s_data.Talks == null)
                        {
                            s_data.Talks = new Dictionary<string, int>();
                        }

                        if (s_data.Layout == null)
                        {
                            s_data.Layout = new Dictionary<string, string>();
                        }

                        if (s_data.CropOffsets == null)
                        {
                            s_data.CropOffsets = new Dictionary<string, string>();
                        }

                        // DataContractJsonSerializer builds the object without running field
                        // initialisers, so a member that the old file does not contain keeps the
                        // default of its type (0) instead of the -1 written above.
                        if (s_data.PikeDay <= 0)
                        {
                            s_data.PikeDay = -1;
                        }

                        Layout.ApplyCropOffsets(s_data.CropOffsets);

                        Log.Info("state loaded: lastDay=" + s_data.LastDay + " gifts=" + s_data.Gifts.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "ModState.Load");
                s_data = new Data();
            }
        }

        internal static void Save()
        {
            if (s_path == null)
            {
                return;
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(s_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (FileStream stream = File.Create(s_path))
                {
                    Serializer.WriteObject(stream, s_data);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "ModState.Save");
            }
        }

        // --- day-scoped marks ---------------------------------------------------------------

        internal static int GiftDay(string npcId)
        {
            return npcId != null && s_data.Gifts.TryGetValue(npcId, out int day) ? day : -1;
        }

        internal static int TalkDay(string npcId)
        {
            return npcId != null && s_data.Talks.TryGetValue(npcId, out int day) ? day : -1;
        }

        internal static void SetGift(string npcId, int day)
        {
            if (npcId == null)
            {
                return;
            }

            s_data.Gifts[npcId] = day;
            s_data.LastDay = day;
            Prune(day);
            Save();
        }

        internal static void SetTalk(string npcId, int day)
        {
            if (npcId == null)
            {
                return;
            }

            s_data.Talks[npcId] = day;
            s_data.LastDay = day;
            Prune(day);
            Save();
        }

        /// <summary>Drops marks from previous days so the file cannot grow without bound.</summary>
        private static void Prune(int day)
        {
            PruneDict(s_data.Gifts, day);
            PruneDict(s_data.Talks, day);
        }

        private static void PruneDict(Dictionary<string, int> map, int day)
        {
            if (map.Count == 0)
            {
                return;
            }

            List<string> stale = null;
            foreach (KeyValuePair<string, int> pair in map)
            {
                if (pair.Value != day)
                {
                    stale = stale ?? new List<string>();
                    stale.Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                map.Remove(stale[i]);
            }
        }

        // --- Pike arrival day ---------------------------------------------------------------

        /// <summary>Absolute day Pike was first seen waiting for the player; 0 or less = unknown.</summary>
        internal static int PikeDay => s_data.PikeDay;

        internal static void SetPikeDay(int day)
        {
            if (s_data.PikeDay == day)
            {
                return;
            }

            s_data.PikeDay = day;
            Save();
        }

        /// <summary>Called on every scan while the mission is not listening, so it must stay cheap.</summary>
        internal static void ClearPikeDay()
        {
            if (s_data.PikeDay == -1)
            {
                return;
            }

            s_data.PikeDay = -1;
            Save();
        }

        // --- layout -------------------------------------------------------------------------
        internal static void ApplyLayout(List<Layout.Item> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Layout.Item item = items[i];
                if (item == null || !s_data.Layout.TryGetValue(item.Key, out string raw) || string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    item.Set(value);
                }
            }
        }

        internal static void CaptureLayout(List<Layout.Item> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Layout.Item item = items[i];
                if (item == null)
                {
                    continue;
                }

                s_data.Layout[item.Key] = Layout.Format(Layout.Round(item.Get()));
            }

            s_data.CropOffsets = Layout.CaptureCropOffsets();
            Save();
        }
    }
}
