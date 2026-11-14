using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Item;
using DolocTown.Config.Plant;
using DolocTown.GameData;
using UnityEngine;

namespace SomeOptimizations
{
    /// <summary>One batch of same-species crops that mature within the configured window.</summary>
    internal sealed class CropBatch
    {
        internal string SeedId;
        internal string Name;
        internal Sprite Icon;
        internal int Count;
        internal int AbsDay;
        internal float FirstExact;
        internal float LastExact;

        /// <summary>How many of these crops are already fully grown (standing ripe, waiting for harvest).</summary>
        internal int Ripe;

        /// <summary>True for the row repeated on today's page for a crop that ripened on an earlier day.</summary>
        internal bool RipeCarry;
    }

    /// <summary>
    /// Read-only projection of every planted crop in the farm rooms. Nothing in here writes to
    /// game state: growth rate is taken from the value the game itself applied on the last tick
    /// (<see cref="Crop.LastGrowth"/>), which already contains genes, lighting, room bonuses and
    /// fertilizer. The theoretical recomputation is only a fallback for crops that never ticked.
    /// </summary>
    internal static class CropScan
    {
        private static readonly FieldInfo FunctionsField =
            typeof(Crop).GetField("functions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly ConditionalWeakTable<PlantBasin, BasinMemo> Memos =
            new ConditionalWeakTable<PlantBasin, BasinMemo>();

        private sealed class BasinMemo
        {
            internal bool Ripe;
            internal int RipeMinute;
        }

        private static readonly List<CropBatch> s_today = new List<CropBatch>();
        private static readonly Dictionary<int, List<CropBatch>> s_byDay = new Dictionary<int, List<CropBatch>>();
        private static readonly Dictionary<string, Sprite> s_iconCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, string> s_nameCache = new Dictionary<string, string>();
        private static float s_next;

        /// <summary>Batches that mature (or are standing ripe) on the current day, earliest first.</summary>
        internal static List<CropBatch> Today => s_today;

        /// <summary>Flat list of every known batch, used by the preview pages to find sample icons.</summary>
        internal static List<CropBatch> AllBatches()
        {
            List<CropBatch> result = new List<CropBatch>();
            foreach (KeyValuePair<int, List<CropBatch>> pair in s_byDay)
            {
                if (pair.Value != null)
                {
                    result.AddRange(pair.Value);
                }
            }

            return result;
        }

        /// <summary>True when at least one harvestable crop is standing in a basin right now.</summary>
        internal static bool AnyRipe { get; private set; }

        internal static void Update()
        {
            if (!GameTime.Ready)
            {
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
                Log.Exception(ex, "CropScan.Rebuild");
            }
        }

        internal static List<CropBatch> BatchesOfDay(int absDay)
        {
            return s_byDay.TryGetValue(absDay, out List<CropBatch> list) ? list : null;
        }

        /// <summary>Diagnostics only: any upcoming batch, so the HUD row can be inspected on quiet days.</summary>
        internal static CropBatch DebugFirstBatch()
        {
            foreach (KeyValuePair<int, List<CropBatch>> entry in s_byDay)
            {
                if (entry.Value != null && entry.Value.Count > 0)
                {
                    return entry.Value[0];
                }
            }

            return null;
        }

        /// <summary>Everything one room contributed on its last observed scan.</summary>
        private sealed class RoomCache
        {
            internal readonly List<string> RipeSeeds = new List<string>();
            internal readonly List<string> PendingSeeds = new List<string>();
            internal readonly List<float> PendingMinutes = new List<float>();
        }

        private static readonly Dictionary<string, RoomCache> s_roomCache = new Dictionary<string, RoomCache>();

        private static void Rebuild()
        {
            s_today.Clear();
            s_byDay.Clear();
            AnyRipe = false;

            DateInfo now = GameTime.Now;
            int nowAbsMinute = now.TotalDays * 1440 + now.Hour * 60 + now.Minute;
            int lastDay = now.TotalDays + 27;

            // day -> seedId -> list of exact absolute minutes
            Dictionary<int, Dictionary<string, List<float>>> acc =
                new Dictionary<int, Dictionary<string, List<float>>>();
            Dictionary<int, Dictionary<string, int>> ripeAcc =
                new Dictionary<int, Dictionary<string, int>>();

            HashSet<string> seenRooms = new HashSet<string>();
            foreach (Room room in EnumerateRooms())
            {
                if (room == null)
                {
                    continue;
                }

                string roomId = null;
                try
                {
                    roomId = room.RoomId;
                }
                catch
                {
                    roomId = null;
                }

                if (!string.IsNullOrEmpty(roomId) && !seenRooms.Add(roomId))
                {
                    continue;
                }

                EquipmentManager manager = room.DM_equipment;
                if (manager == null)
                {
                    continue;
                }

                RoomCache cache = !string.IsNullOrEmpty(roomId) ? new RoomCache() : null;

                foreach (Equipment equipment in manager.AllEquipments)
                {
                    if (!(equipment is PlantBasin basin))
                    {
                        continue;
                    }

                    Crop crop = basin.Crop;
                    if (crop == null || crop.seedProto == null)
                    {
                        continue;
                    }

                    float exact;
                    bool ripe = false;
                    if (crop.isMature)
                    {
                        AnyRipe = true;
                        ripe = true;
                        exact = RipeMoment(basin, nowAbsMinute);
                    }
                    else
                    {
                        if (crop.isDead)
                        {
                            continue;
                        }

                        if (!TryPredict(crop, basin, out float deltaMinutes))
                        {
                            continue;
                        }

                        exact = nowAbsMinute + deltaMinutes;
                    }

                    if (cache != null)
                    {
                        if (ripe)
                        {
                            cache.RipeSeeds.Add(crop.SeedId);
                        }
                        else
                        {
                            cache.PendingSeeds.Add(crop.SeedId);
                            cache.PendingMinutes.Add(exact);
                        }
                    }

                    AddSample(acc, ripeAcc, exact, crop.SeedId, now.TotalDays, lastDay, ripe);
                }

                if (cache != null && !string.IsNullOrEmpty(roomId))
                {
                    s_roomCache[roomId] = cache;
                }
            }

            // Rooms that are not loaded (greenhouses and sheds the player is not standing in) keep
            // their last projection: their crops do not tick while away, but the game catches the
            // room up the moment it is entered again, so the absolute minute is still the best guess.
            foreach (KeyValuePair<string, RoomCache> entry in s_roomCache)
            {
                if (seenRooms.Contains(entry.Key))
                {
                    continue;
                }

                RoomCache cache = entry.Value;
                for (int i = 0; i < cache.RipeSeeds.Count; i++)
                {
                    AddSample(acc, ripeAcc, nowAbsMinute, cache.RipeSeeds[i], now.TotalDays, lastDay, true);
                }

                for (int i = 0; i < cache.PendingSeeds.Count; i++)
                {
                    float minute = cache.PendingMinutes[i];
                    bool overdue = minute <= nowAbsMinute;
                    AddSample(acc, ripeAcc, overdue ? nowAbsMinute : minute, cache.PendingSeeds[i],
                        now.TotalDays, lastDay, overdue);
                }
            }

            float threshold = (float)ModConfig.SameBatchMinutes;
            foreach (KeyValuePair<int, Dictionary<string, List<float>>> dayEntry in acc)
            {
                List<CropBatch> batches = new List<CropBatch>();
                Dictionary<string, int> ripeBySeed = ripeAcc.TryGetValue(dayEntry.Key, out Dictionary<string, int> r)
                    ? r
                    : null;

                foreach (KeyValuePair<string, List<float>> seedEntry in dayEntry.Value)
                {
                    List<float> times = seedEntry.Value;
                    times.Sort();

                    int ripeLeft = 0;
                    if (ripeBySeed != null)
                    {
                        ripeBySeed.TryGetValue(seedEntry.Key, out ripeLeft);
                    }

                    float batchStart = times[0];
                    CropBatch current = NewBatch(seedEntry.Key, dayEntry.Key, batchStart);
                    current.LastExact = batchStart;
                    current.Count = 1;
                    if (ripeLeft > 0)
                    {
                        current.Ripe = 1;
                        ripeLeft--;
                    }

                    for (int i = 1; i < times.Count; i++)
                    {
                        if (times[i] - batchStart > threshold)
                        {
                            batches.Add(current);
                            batchStart = times[i];
                            current = NewBatch(seedEntry.Key, dayEntry.Key, batchStart);
                            current.Count = 1;
                        }
                        else
                        {
                            current.Count++;
                        }

                        if (ripeLeft > 0)
                        {
                            current.Ripe++;
                            ripeLeft--;
                        }

                        current.LastExact = times[i];
                    }

                    batches.Add(current);
                }

                batches.Sort((a, b) => a.FirstExact.CompareTo(b.FirstExact));
                s_byDay[dayEntry.Key] = batches;
            }

            // A crop that is standing ripe and unharvested keeps nagging: it is repeated on TODAY
            // until the player finally picks it (never on later days, that filled up the month).
            int today = now.TotalDays;
            int[] days = new int[s_byDay.Count];
            s_byDay.Keys.CopyTo(days, 0);
            Dictionary<string, CropBatch> carried = new Dictionary<string, CropBatch>();
            for (int d = 0; d < days.Length; d++)
            {
                List<CropBatch> source = s_byDay[days[d]];
                for (int i = 0; i < source.Count; i++)
                {
                    CropBatch batch = source[i];
                    if (batch.Ripe <= 0 || batch.AbsDay >= today)
                    {
                        continue;
                    }

                    if (carried.TryGetValue(batch.SeedId, out CropBatch existing))
                    {
                        existing.Count += batch.Ripe;
                        existing.Ripe += batch.Ripe;
                        continue;
                    }

                    carried[batch.SeedId] = new CropBatch
                    {
                        SeedId = batch.SeedId,
                        Name = batch.Name,
                        Icon = batch.Icon,
                        Count = batch.Ripe,
                        AbsDay = today,
                        FirstExact = nowAbsMinute,
                        LastExact = nowAbsMinute,
                        Ripe = batch.Ripe,
                        RipeCarry = true
                    };
                }
            }

            if (carried.Count > 0)
            {
                if (!s_byDay.TryGetValue(today, out List<CropBatch> target))
                {
                    target = new List<CropBatch>();
                    s_byDay[today] = target;
                }

                foreach (CropBatch batch in carried.Values)
                {
                    target.Add(batch);
                }
            }

            foreach (KeyValuePair<int, List<CropBatch>> entry in s_byDay)
            {
                entry.Value.Sort((a, b) => a.FirstExact.CompareTo(b.FirstExact));
            }

            if (s_byDay.TryGetValue(today, out List<CropBatch> todayBatches))
            {
                s_today.AddRange(todayBatches);
            }

            s_today.Sort((a, b) => a.FirstExact.CompareTo(b.FirstExact));
        }

        private static void AddSample(
            Dictionary<int, Dictionary<string, List<float>>> acc,
            Dictionary<int, Dictionary<string, int>> ripeAcc,
            float exact,
            string seedId,
            int nowDay,
            int lastDay,
            bool ripe)
        {
            int day = (int)Math.Floor(exact / 1440f);
            if (day < nowDay || day > lastDay)
            {
                return;
            }

            if (!acc.TryGetValue(day, out Dictionary<string, List<float>> perSeed))
            {
                perSeed = new Dictionary<string, List<float>>();
                acc[day] = perSeed;
            }

            if (!perSeed.TryGetValue(seedId, out List<float> times))
            {
                times = new List<float>();
                perSeed[seedId] = times;
            }

            if (ripe)
            {
                if (!ripeAcc.TryGetValue(day, out Dictionary<string, int> perSeedRipe))
                {
                    perSeedRipe = new Dictionary<string, int>();
                    ripeAcc[day] = perSeedRipe;
                }

                perSeedRipe.TryGetValue(seedId, out int count);
                perSeedRipe[seedId] = count + 1;
            }

            times.Add(exact);
        }

        internal static string DebugSummary()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int rooms = 0;
            int basins = 0;
            int planted = 0;
            try
            {
                foreach (Room room in EnumerateRooms())
                {
                    if (room == null)
                    {
                        continue;
                    }

                    rooms++;
                    EquipmentManager manager = room.DM_equipment;
                    if (manager == null)
                    {
                        continue;
                    }

                    foreach (Equipment equipment in manager.AllEquipments)
                    {
                        if (!(equipment is PlantBasin basin))
                        {
                            continue;
                        }

                        basins++;
                        if (basin.Crop != null && basin.Crop.seedProto != null)
                        {
                            planted++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan.DebugSummary");
            }

            sb.Append("crops rooms=").Append(rooms)
              .Append(" basins=").Append(basins)
              .Append(" planted=").Append(planted)
              .Append(" today=").Append(s_today.Count)
              .Append(" days=").Append(s_byDay.Count)
              .Append(" now=").Append(NowClock());

            int shown = 0;
            foreach (KeyValuePair<int, List<CropBatch>> day in s_byDay)
            {
                for (int i = 0; i < day.Value.Count; i++)
                {
                    if (shown++ >= 8)
                    {
                        break;
                    }

                    CropBatch batch = day.Value[i];
                    sb.Append(" | ").Append(batch.Name)
                      .Append(" d=").Append(batch.AbsDay)
                      .Append(" x").Append(batch.Count)
                      .Append(" ").Append(GameTime.FormatMinute(batch.FirstExact % 1440f, true))
                      .Append("-").Append(GameTime.FormatMinute(batch.LastExact % 1440f, true));
                }
            }

            return sb.ToString();
        }

        private static string NowClock()
        {
            DateInfo now = GameTime.Now;
            return GameTime.Pad(now.Month) + "/" + GameTime.Pad(now.Day) + " " +
                   GameTime.Clock(now.Hour * 60 + now.Minute);
        }

        private static CropBatch NewBatch(string seedId, int absDay, float first)
        {
            CropBatch batch = new CropBatch
            {
                SeedId = seedId,
                AbsDay = absDay,
                FirstExact = first,
                LastExact = first,
                Count = 0
            };

            batch.Name = SeedDisplayName(seedId);
            batch.Icon = SeedIcon(seedId);
            return batch;
        }

        private static IEnumerable<Room> EnumerateRooms()
        {
            HashSet<string> seen = new HashSet<string>();
            List<Room> roots = new List<Room>();
            foreach (Room room in RootRooms())
            {
                roots.Add(room);
                seen.Add(room.RoomId);
                yield return room;
            }

            for (int i = 0; i < roots.Count; i++)
            {
                foreach (Room inner in BuildingRooms(roots[i]))
                {
                    if (inner != null && seen.Add(inner.RoomId))
                    {
                        yield return inner;
                    }
                }
            }
        }

        private static IEnumerable<Room> RootRooms()
        {
            ArchiveDataHandle handle = DolocAPI.archiveHandle;
            HashSet<string> seen = new HashSet<string>();

            if (handle != null)
            {
                foreach (TemplateRoomOutdoor farm in handle.AllFarms)
                {
                    if (farm == null)
                    {
                        continue;
                    }

                    if (seen.Add(farm.RoomId))
                    {
                        yield return farm;
                    }
                }

                // Every city room exists in memory whether or not the player is standing in it,
                // and the planters inside buildings are reachable through the building manager.
                List<CityRoom> cities = null;
                try
                {
                    if (handle.cityData != null && handle.cityData.cityRooms != null)
                    {
                        cities = new List<CityRoom>(handle.cityData.cityRooms.Values);
                    }
                }
                catch
                {
                    cities = null;
                }

                if (cities != null)
                {
                    for (int i = 0; i < cities.Count; i++)
                    {
                        CityRoom city = cities[i];
                        if (city != null && seen.Add(city.RoomId))
                        {
                            yield return city;
                        }
                    }
                }
            }

            Room current = DolocAPI.CurrentRoom;
            if (current != null && seen.Add(current.RoomId))
            {
                yield return current;
            }
        }

        /// <summary>Rooms inside buildings (greenhouses, sheds, ...) that belong to <paramref name="room"/>.</summary>
        private static IEnumerable<Room> BuildingRooms(Room room)
        {
            BuildingManager manager = room != null ? room.DM_building : null;
            if (manager == null)
            {
                yield break;
            }

            foreach (Building building in manager.Buildings)
            {
                Room inner = building != null ? building.room : null;
                if (inner != null)
                {
                    yield return inner;
                }
            }
        }

        private static float RipeMoment(PlantBasin basin, int nowAbsMinute)
        {
            BasinMemo memo = Memos.GetOrCreateValue(basin);
            if (!memo.Ripe)
            {
                memo.Ripe = true;
                memo.RipeMinute = nowAbsMinute;
            }

            return memo.RipeMinute;
        }

        /// <summary>Growth units applied per time unit, mirroring <c>PlantBasin.GrowthAddition</c>.</summary>
        private static float GrowthPerTU(Crop crop, PlantBasin basin)
        {
            float observed = crop.LastGrowth;
            if (observed > 0.0001f)
            {
                return observed;
            }

            float rate = 1f;
            try
            {
                if (FunctionsField != null &&
                    FunctionsField.GetValue(crop) is CropGeneFunction[] functions &&
                    functions.Length > 0)
                {
                    rate = CropGeneUtils.CalcGrowthValue(functions);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan genes");
            }

            try
            {
                if (basin.IsLighting)
                {
                    rate += DolocAPI.GlobalParameter.SunGrowthAddition;
                }
            }
            catch
            {
                // lighting probe is best effort only
            }

            try
            {
                if (basin.Supply.IsFertilized)
                {
                    rate += basin.Supply.FertilizerData.growthAddition;
                }
            }
            catch
            {
                // fertilizer probe is best effort only
            }

            return rate;
        }

        private static bool TryPredict(Crop crop, PlantBasin basin, out float deltaMinutes)
        {
            deltaMinutes = 0f;

            float rate = GrowthPerTU(crop, basin);
            if (rate <= 0.0001f)
            {
                return false;
            }

            SeedInfo seed = crop.seedProto;
            int matureLevel = seed.MatureLevel;
            int level = crop.data.currentLevel;
            if (level >= matureLevel)
            {
                deltaMinutes = 0f;
                return true;
            }

            float below = 0f;
            for (int i = 0; i < level; i++)
            {
                below += seed.GetLevelData(i).GrowthValue;
            }

            float current = crop.CurrentGrowthValue - below;
            if (current < 0f)
            {
                current = 0f;
            }

            float tus = 0f;
            for (int l = level; l < matureLevel; l++)
            {
                float need = seed.GetLevelData(l).GrowthValue;
                float left = need - (l == level ? current : 0f);
                if (left <= 0f)
                {
                    continue;
                }

                float turns = left / rate;
                if (l == matureLevel - 1)
                {
                    tus += turns;
                }
                else
                {
                    tus += Mathf.Ceil(turns);
                }
            }

            deltaMinutes = tus * DolocAPI.GlobalParameter.TU2Min;
            return true;
        }

        internal static string SeedDisplayName(string seedId)
        {
            if (s_nameCache.TryGetValue(seedId, out string cached))
            {
                return cached;
            }

            string name = seedId;
            try
            {
                SeedInfo seed = DolocConfig.Tables.TbSeed.GetOrDefault(seedId);
                if (seed != null && seed.CropOutputs != null && seed.CropOutputs.Length > 0)
                {
                    ItemInfo item = seed.CropOutputs[0].itemRef;
                    if (item != null && !string.IsNullOrEmpty(item.Title))
                    {
                        name = item.Title;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan.SeedDisplayName " + seedId);
            }

            s_nameCache[seedId] = name;
            return name;
        }

        internal static Sprite SeedIcon(string seedId)
        {
            if (s_iconCache.TryGetValue(seedId, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = null;
            try
            {
                SeedInfo seed = DolocConfig.Tables.TbSeed.GetOrDefault(seedId);
                if (seed != null)
                {
                    // The harvested item icon is a centred, small icon - the growing-stage sprite
                    // has big transparent margins, which makes it look off-centre in a badge.
                    if (seed.CropOutputs != null && seed.CropOutputs.Length > 0)
                    {
                        ItemInfo item = seed.CropOutputs[0].itemRef;
                        if (item != null && item.UiSpriteAsset != null)
                        {
                            sprite = item.UiSpriteAsset.Asset;
                        }
                    }

                    if (sprite == null)
                    {
                        sprite = seed.GetLevelSprite(seed.MatureLevel, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan.SeedIcon " + seedId);
            }

            s_iconCache[seedId] = sprite;
            return sprite;
        }
    }
}
