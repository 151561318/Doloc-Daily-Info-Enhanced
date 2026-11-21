using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DolocTown;
using DolocTown.Config;
using DolocTown.Config.Item;
using DolocTown.Config.Plant;
using DolocTown.Config.Resource;
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
    ///
    /// Two kinds of host are scanned: ordinary planters (<see cref="PlantBasin"/>) and the tree
    /// stand (<see cref="PlantBasinTree"/>, where the cocoa tree lives).
    /// </summary>
    internal static class CropScan
    {
        private const float DayLength = GameTime.MinutesPerDay;

        private static readonly FieldInfo FunctionsField =
            typeof(Crop).GetField("functions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Both are private on TreeCrop and are what the game itself grows the tree with:
        // maxGrowthValues is picked once from the seed's random range and then kept for the plant.
        private static readonly FieldInfo TreeMaxGrowthField =
            typeof(TreeCrop).GetField("maxGrowthValues", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo TreeRandomSeedField =
            typeof(TreeCrop).GetField("randomSeed", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly ConditionalWeakTable<object, BasinMemo> Memos =
            new ConditionalWeakTable<object, BasinMemo>();

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
        private static float s_step;

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

            // 27 days covers everything this mod reports: a freshly planted cocoa tree needs 21
            // (6048 time units) and a re-growing one 6, so nothing is clipped.
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
                    if (!TrySample(equipment, nowAbsMinute, out string seedId, out float exact, out bool ripe))
                    {
                        continue;
                    }

                    if (ripe)
                    {
                        AnyRipe = true;
                    }

                    if (cache != null)
                    {
                        if (ripe)
                        {
                            cache.RipeSeeds.Add(seedId);
                        }
                        else
                        {
                            cache.PendingSeeds.Add(seedId);
                            cache.PendingMinutes.Add(exact);
                        }
                    }

                    AddSample(acc, ripeAcc, exact, seedId, now.TotalDays, lastDay, ripe);
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

        /// <summary>
        /// Files one sample into its day bucket.
        ///
        /// The old version did <c>day = floor(exact / 1440)</c> on the raw float. A crop that ripens
        /// exactly at 24:00 produced something like 35999.99998 one scan and 36000.00002 the next,
        /// so it fell on day 24 and day 25 alternately: it blinked in and out of today's list, and
        /// once the clock had rolled over, the "day 24" copy was before today and got dropped, which
        /// is why it vanished from both days. Two things fix that:
        ///
        ///  - every minute is rounded to a whole one before it is bucketed, so tiny float error can
        ///    no longer move a sample across the midnight line;
        ///  - a minute that lands exactly on midnight belongs to the day it ends, as 24:00 (the
        ///    clock reads "24:00 成熟" that day, not "00:00" the next), and a sample that ends up
        ///    before today is clamped onto today instead of being thrown away.
        /// </summary>
        private static void AddSample(
            Dictionary<int, Dictionary<string, List<float>>> acc,
            Dictionary<int, Dictionary<string, int>> ripeAcc,
            float exact,
            string seedId,
            int nowDay,
            int lastDay,
            bool ripe)
        {
            if (!TryBucket(exact, nowDay, lastDay, out int day, out float minute))
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

            times.Add(minute);
        }

        /// <summary>
        /// Normalises a predicted absolute minute onto the day it is displayed in. Returns false only
        /// when the sample is too far in the future to be worth showing. <paramref name="day"/> is
        /// the bucket key, <paramref name="minute"/> the value kept for ordering and display (it sits
        /// at 24:00 of <paramref name="day"/> for a midnight batch).
        /// </summary>
        private static bool TryBucket(float exact, int nowDay, int lastDay, out int day, out float minute)
        {
            day = 0;
            minute = 0f;
            if (float.IsNaN(exact) || float.IsInfinity(exact) || exact <= 0f)
            {
                return false;
            }

            // Snap to the game's own minute grid first. The clock only has TU2Min (5) minute
            // resolution, the crop tick and the clock tick are two separate updates, and the last
            // level is extrapolated with a division - all three move the predicted minute by up to
            // one step around midnight. Judging the day from the un-rounded value made a 24:00 crop
            // hop between "today" and "tomorrow" for a few seconds every time; rounding to the grid
            // the player actually sees makes that impossible.
            float step = StepMinutes;
            float snapped = Mathf.Round(exact / step) * step;

            day = (int)Math.Floor(snapped / DayLength);
            float rest = snapped - day * DayLength;

            if (rest <= step)
            {
                // Midnight, and the first few minutes after it, belong to the day that is ending and
                // are shown as 24:00. The player reads "24 号 24:00 成熟", not "25 号 00:00".
                day--;
            }

            if (day > lastDay)
            {
                return false;
            }

            if (day < nowDay)
            {
                // Overdue: the crop should already be ripe (or is a tick away). Keep it on today
                // rather than dropping it - dropping is what made the row disappear entirely.
                day = nowDay;
            }

            // Keep the un-rounded minute when it still falls inside the chosen day, so the optional
            // "show exact minute" display is not coarsened by the bucketing above.
            minute = Mathf.Clamp(exact - day * DayLength, 0f, DayLength);
            return true;
        }

        /// <summary>The game clock's step in minutes (one time unit), i.e. its display resolution.</summary>
        private static float StepMinutes
        {
            get
            {
                if (s_step > 0f)
                {
                    return s_step;
                }

                try
                {
                    s_step = Mathf.Max(1f, DolocAPI.GlobalParameter.TU2Min);
                }
                catch
                {
                    return 5f;
                }

                return s_step;
            }
        }

        /// <summary>Minute of the day a batch is shown at; 1440 means "24:00" on its own day.</summary>
        internal static float MinuteOfDay(CropBatch batch)
        {
            return batch == null ? 0f : batch.FirstExact - batch.AbsDay * DayLength;
        }

        internal static string DebugSummary()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int rooms = 0;
            int basins = 0;
            int planted = 0;
            int trees = 0;
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
                        if (equipment is PlantBasin basin)
                        {
                            basins++;
                            if (basin.Crop != null && basin.Crop.seedProto != null)
                            {
                                planted++;
                            }

                            continue;
                        }

                        if (equipment is PlantBasinTree tree)
                        {
                            trees++;
                            if (tree.Crop != null && tree.Crop.protoTree != null)
                            {
                                planted++;
                            }
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
              .Append(" trees=").Append(trees)
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
                      .Append(" ").Append(GameTime.FormatMinute(MinuteOfDay(batch), true))
                      .Append("-").Append(GameTime.FormatMinute(batch.LastExact - batch.AbsDay * DayLength, true));
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

        /// <summary>
        /// Reads one equipment and turns it into a (seed, absolute minute, ripe) sample. Both the
        /// planter and the tree stand are handled here so the rest of the scan does not care which
        /// kind of host a crop lives in.
        /// </summary>
        private static bool TrySample(Equipment equipment, int nowAbsMinute, out string seedId, out float exact,
            out bool ripe)
        {
            seedId = null;
            exact = 0f;
            ripe = false;

            if (equipment is PlantBasin basin)
            {
                Crop crop = basin.Crop;
                if (crop == null || crop.seedProto == null)
                {
                    return false;
                }

                if (crop.isMature)
                {
                    ripe = true;
                    exact = RipeMoment(basin, nowAbsMinute);
                }
                else
                {
                    if (crop.isDead)
                    {
                        return false;
                    }

                    if (!TryPredict(crop, basin, out float deltaMinutes))
                    {
                        return false;
                    }

                    exact = nowAbsMinute + deltaMinutes;
                }

                seedId = crop.SeedId;
                return true;
            }

            if (equipment is PlantBasinTree tree)
            {
                if (ModConfig.DisableTreeCrops)
                {
                    return false;
                }

                TreeCrop crop = tree.Crop;
                if (crop == null || crop.protoTree == null)
                {
                    return false;
                }

                if (crop.isMature)
                {
                    ripe = true;
                    exact = RipeMoment(tree, nowAbsMinute);
                }
                else
                {
                    if (!TryPredictTree(crop, out float deltaMinutes))
                    {
                        return false;
                    }

                    exact = nowAbsMinute + deltaMinutes;
                }

                seedId = crop.protoTree.Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A crop that is standing ripe keeps a "first noticed" minute so the row does not move
        /// around while it waits to be harvested; it is re-stamped onto the current day after every
        /// midnight. Works for basins and tree stands alike (the memo is keyed by host instance).
        /// </summary>
        private static float RipeMoment(object host, int nowAbsMinute)
        {
            BasinMemo memo = Memos.GetOrCreateValue(host);
            int dayStart = nowAbsMinute - nowAbsMinute % GameTime.MinutesPerDay;
            if (!memo.Ripe || memo.RipeMinute < dayStart)
            {
                // A crop that was already ripe before midnight is re-stamped onto the current day:
                // otherwise the remembered minute lands on a past day, and the "ready to harvest"
                // reminder would slide into yesterday's bucket.
                memo.Ripe = true;
                memo.RipeMinute = dayStart;
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

        /// <summary>
        /// Tree stand version of <see cref="TryPredict"/>. The tree grows a flat amount per time unit
        /// (1, plus whatever fertiliser adds) and only rolls over to the next level once the level's
        /// random growth target is reached, so the remaining time is exactly
        /// <c>sum(ceil(left / rate))</c> - no lighting or moisture bonuses are involved.
        /// </summary>
        private static bool TryPredictTree(TreeCrop crop, out float deltaMinutes)
        {
            deltaMinutes = 0f;

            TreeSeedInfo proto = crop.protoTree;
            if (proto == null)
            {
                return false;
            }

            int[] max = TreeGrowthValues(crop, proto);
            if (max == null || max.Length == 0)
            {
                return false;
            }

            int maxLevel = proto.MaxLevel;
            int level = Mathf.Clamp(crop.CurrentLevel, 0, maxLevel);
            if (level >= maxLevel)
            {
                return true;
            }

            float rate = TreeGrowthPerTU(crop);
            if (rate <= 0.0001f)
            {
                return false;
            }

            float tus = 0f;
            for (int l = level; l < maxLevel; l++)
            {
                float need = max[Mathf.Clamp(l, 0, max.Length - 1)];
                float left = need - (l == level ? crop.currentGrowthValue : 0f);
                if (left <= 0f)
                {
                    continue;
                }

                tus += Mathf.Ceil(left / rate);
            }

            deltaMinutes = tus * DolocAPI.GlobalParameter.TU2Min;
            return true;
        }

        /// <summary>
        /// Growth per time unit of a tree. <c>TreeCrop.Growth</c> decrements the fertiliser counter as
        /// a side effect, so it must never be called from this read-only scan - the same number is
        /// rebuilt here instead.
        /// </summary>
        private static float TreeGrowthPerTU(TreeCrop crop)
        {
            try
            {
                if (crop.fertilizerData.duration > 0)
                {
                    return 1f + crop.fertilizerData.growthAddition;
                }
            }
            catch
            {
                // fertiliser probe is best effort only
            }

            return 1f;
        }

        /// <summary>
        /// The per-level growth targets the game generated for this particular tree. They come from a
        /// random range seeded per plant, so they cannot be recomputed from the seed table alone.
        /// </summary>
        private static int[] TreeGrowthValues(TreeCrop crop, TreeSeedInfo proto)
        {
            try
            {
                if (TreeMaxGrowthField != null &&
                    TreeMaxGrowthField.GetValue(crop) is int[] values && values.Length > 0)
                {
                    return values;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan tree growth values");
            }

            try
            {
                if (TreeRandomSeedField != null &&
                    TreeRandomSeedField.GetValue(crop) is int seed)
                {
                    return proto.GetRandomMaxGrowthValues(seed);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan tree random seed");
            }

            return null;
        }

        /// <summary>The item a fully grown tree drops - used for the name and icon of the reminder.</summary>
        private static string TreeOutputItem(TreeSeedInfo proto)
        {
            try
            {
                ResourceLevelData level = proto.GetLevelData(proto.MaxLevel);
                ItemSpawnEntry entry = level != null ? level.DropSpawnEntry : null;
                ItemSpawnInfo lut = entry != null ? entry.SpawnLut_Ref : null;
                if (lut != null && lut.SpawnDatas != null && lut.SpawnDatas.Count > 0)
                {
                    return lut.SpawnDatas[0].ItemName;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan tree output");
            }

            return null;
        }

        private static ItemInfo TreeOutputInfo(TreeSeedInfo proto)
        {
            try
            {
                string itemId = TreeOutputItem(proto);
                if (!string.IsNullOrEmpty(itemId))
                {
                    return DolocConfig.Tables.TbItem.GetOrDefault(itemId);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "CropScan tree output item");
            }

            return null;
        }
        internal static string SeedDisplayName(string seedId)
        {
            if (string.IsNullOrEmpty(seedId))
            {
                return string.Empty;
            }

            if (s_nameCache.TryGetValue(seedId, out string cached))
            {
                return cached;
            }

            string name = seedId;
            try
            {
                SeedInfo seed = DolocConfig.Tables.TbSeed.GetOrDefault(seedId);
                if (seed != null)
                {
                    if (seed.CropOutputs != null && seed.CropOutputs.Length > 0)
                    {
                        ItemInfo item = seed.CropOutputs[0].itemRef;
                        if (item != null && !string.IsNullOrEmpty(item.Title))
                        {
                            name = item.Title;
                        }
                    }
                }
                else
                {
                    // The tree stand plants tree seeds, which live in their own table; the name shown
                    // is the harvested item (cocoa berry), matching how ordinary crops are named.
                    TreeSeedInfo tree = DolocConfig.Tables.TbTreeSeed.GetOrDefault(seedId);
                    if (tree != null)
                    {
                        ItemInfo item = TreeOutputInfo(tree);
                        name = item != null && !string.IsNullOrEmpty(item.Title) ? item.Title : tree.Title;
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
                if (seed == null)
                {
                    TreeSeedInfo tree = DolocConfig.Tables.TbTreeSeed.GetOrDefault(seedId);
                    if (tree != null)
                    {
                        ItemInfo item = TreeOutputInfo(tree);
                        sprite = item != null && item.UiSpriteAsset != null ? item.UiSpriteAsset.Asset : null;
                    }
                }
                else
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
