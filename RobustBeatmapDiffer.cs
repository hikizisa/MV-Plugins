using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Parser.Objects.HitObjects;
using MapsetVerifier.Parser.Objects.HitObjects.Catch;
using MapsetVerifier.Parser.Objects.HitObjects.Mania;
using MapsetVerifier.Parser.Objects.TimingLines;
using MapsetVerifier.Parser.Statics;
using MapsetVerifier.Snapshots.Diffing;
using MathNet.Numerics;

namespace MapsetVerifier.Plugin.CustomSnapshots
{
    /// <summary>
    /// Robust replacement for MapsetVerifier's BeatmapDiffer.
    /// Incorporates DTW sequence alignment, global RANSAC shift estimation (removing the 1000ms cap),
    /// unstacked position tracking (preventing stacking ghost diffs), flip detection (Ctrl+H / Ctrl+J),
    /// sub-snap jitter filtering, and break period grouping.
    /// </summary>
    public static class RobustBeatmapDiffer
    {
        private const double RetimeWindowMs = 125;
        private const double RetimeMaxDistancePx = 40;
        private const double MinorMovePx = 3;
        private const double OffsetShare = 0.6;
        private const int OffsetMinimumObjects = 3;
        private const double MaxShiftMs = 10000; // Increased from 1000ms to 10000ms for robust MP3 offset tracking
        private const double ShiftToleranceMs = 1.5;
        private const int MaxShiftGroups = 32;
        private const int MaxFruitlessShifts = 6;
        private const int FarShiftMinimumObjects = 4;
        private const double ShiftDriftWeight = 0.5;

        private enum FlipType
        {
            None,
            Horizontal,
            Vertical,
            Both,
            CatchHorizontal
        }

        private sealed class ShiftGroup
        {
            public required double Delta { get; init; }
            public required List<(HitObject Old, HitObject New, FlipType Flip)> Pairs { get; init; }
            public int FirstIndex { get; init; }
            public int LastIndex { get; init; }
            public double From { get; set; } = double.NegativeInfinity;
            public double To { get; set; } = double.PositiveInfinity;
            public List<(TimingLine Old, TimingLine New)> Lines { get; } = [];
            public bool Whole { get; set; }
        }

        public static DiffResult Diff(Beatmap before, Beatmap after)
        {
            var changes = new List<Change>();
            var rollups = new List<Rollup>();

            var objectPairs = MatchObjects(
                before,
                after,
                out var oldOnly,
                out var newOnly,
                out var groups
            );

            foreach (var (oldObject, newObject, delta, flip) in objectPairs)
                changes.AddRange(DiffObject(before, after, oldObject, newObject, delta, flip));

            foreach (var o in oldOnly)
                changes.Add(ObjectChange(after, o, ChangeOp.Removed));

            foreach (var o in newOnly)
                changes.Add(ObjectChange(after, o, ChangeOp.Added));

            DiffTiming(before, after, groups, changes);

            var whole = groups.Where(g => g.Whole).ToList();
            var shift = whole.Count == 0 ? 0 : whole.MaxBy(g => g.Pairs.Count)!.Delta;

            if (whole.Count > 0)
            {
                rollups.Add(
                    new Rollup(
                        RollupKind.Offset,
                        shift,
                        whole.Sum(g => g.Pairs.Count + g.Lines.Count)
                    )
                );
            }

            foreach (var group in groups.Where(g => !g.Whole))
                changes.Add(ShiftChange(group, shift));

            GroupVolumeChanges(after, changes, rollups);

            return new DiffResult(changes, rollups);
        }

        private static List<(HitObject Old, HitObject New, double Delta, FlipType Flip)> MatchObjects(
            Beatmap before,
            Beatmap after,
            out List<HitObject> oldOnly,
            out List<HitObject> newOnly,
            out List<ShiftGroup> groups
        )
        {
            var pairs = new List<(HitObject Old, HitObject New, double Delta, FlipType Flip)>();
            var all = before.HitObjects.OrderBy(o => o.time).ToList();
            var oldLeft = all.ToList();
            var newLeft = after.HitObjects.OrderBy(o => o.time).ToList();

            // Pass 1: exact time and same type (allowing normal or flipped positions)
            PairWhere(
                oldLeft,
                newLeft,
                pairs,
                0,
                (o, n, flip) => AlmostEqual(o.time, n.time) && (flip != FlipType.None || UnstackedDistance(o, n) <= RetimeMaxDistancePx)
            );

            // Pass 2: RANSAC shift estimation + Sequence Alignment for runs of objects
            var inPlace = pairs.Select(p => p.Old).ToHashSet();
            groups = FindShiftGroups(all, inPlace, oldLeft, newLeft, before, after);

            foreach (var group in groups)
            {
                pairs.AddRange(group.Pairs.Select(p => (p.Old, p.New, group.Delta, p.Flip)));
            }

            MarkWholeShift(groups, all.Count);
            SetGroupBounds(groups, all, inPlace);

            // Pass 3: Small retimes in place on top of offset change
            var whole = groups.Where(g => g.Whole).ToList();
            var delta = whole.Count == 0 ? 0 : whole.MaxBy(g => g.Pairs.Count)!.Delta;

            PairWhere(
                oldLeft,
                newLeft,
                pairs,
                delta,
                (o, n, flip) =>
                    Math.Abs(o.time + delta - n.time) <= RetimeWindowMs
                    && UnstackedDistance(o, n) <= RetimeMaxDistancePx
            );

            // Pass 4: In-place kind changes (Circle <-> Slider, Hold note <-> Note)
            var mode = after.GeneralSettings.mode;
            var keys = after.DifficultySettings.circleSize;

            PairWhere(
                oldLeft,
                newLeft,
                pairs,
                delta,
                (o, n, flip) =>
                    AlmostEqual(o.time + delta, n.time)
                    && mode switch
                    {
                        Beatmap.Mode.Mania => ManiaExtensions.GetColumn(o, keys) == ManiaExtensions.GetColumn(n, keys),
                        Beatmap.Mode.Taiko => true,
                        _ => UnstackedDistance(o, n) <= RetimeMaxDistancePx,
                    },
                anyType: true
            );

            oldOnly = oldLeft;
            newOnly = newLeft;

            return pairs;
        }

        private static void PairWhere(
            List<HitObject> oldLeft,
            List<HitObject> newLeft,
            List<(HitObject Old, HitObject New, double Delta, FlipType Flip)> pairs,
            double delta,
            Func<HitObject, HitObject, FlipType, bool> match,
            bool anyType = false
        )
        {
            var candidates = new List<(HitObject Old, HitObject New, double Cost, FlipType Flip)>();
            var window = RetimeWindowMs + 1;
            var newTimes = newLeft.Select(n => n.time).ToArray();

            foreach (var o in oldLeft)
            {
                var type = o.GetObjectType();

                for (var i = LowerBound(newTimes, o.time + delta - window); i < newLeft.Count; i++)
                {
                    var n = newLeft[i];
                    if (n.time > o.time + delta + window)
                        break;

                    if (!anyType && n.GetObjectType() != type)
                        continue;

                    var (dist, flip) = BestUnstackedDistanceAndFlip(o, n);
                    if (!match(o, n, flip))
                        continue;

                    var cost = dist + Math.Abs(o.time + delta - n.time) / 10;
                    candidates.Add((o, n, cost, flip));
                }
            }

            var usedOld = new HashSet<HitObject>();
            var usedNew = new HashSet<HitObject>();

            foreach (var (o, n, _, flip) in candidates.OrderBy(c => c.Cost))
            {
                if (usedOld.Contains(o) || usedNew.Contains(n))
                    continue;

                usedOld.Add(o);
                usedNew.Add(n);
                pairs.Add((o, n, delta, flip));
            }

            oldLeft.RemoveAll(usedOld.Contains);
            newLeft.RemoveAll(usedNew.Contains);
        }

        private static List<ShiftGroup> FindShiftGroups(
            List<HitObject> all,
            HashSet<HitObject> inPlace,
            List<HitObject> oldLeft,
            List<HitObject> newLeft,
            Beatmap before,
            Beatmap after
        )
        {
            var groups = new List<ShiftGroup>();
            var tried = new List<double>();
            var index = all.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i);
            var settled = inPlace.ToHashSet();
            var fruitless = 0;

            while (groups.Count < MaxShiftGroups && fruitless < MaxFruitlessShifts)
            {
                var delta = DensestShiftRansac(oldLeft, newLeft, tried);
                if (delta == null)
                    break;

                tried.Add(delta.Value);
                var d = delta.Value;
                var found = groups.Count;

                if (d == 0)
                    continue;

                var minimum = Math.Abs(d) > RetimeWindowMs ? FarShiftMinimumObjects : OffsetMinimumObjects;
                var newTimes = newLeft.Select(n => n.time).ToArray();

                var explained = oldLeft
                    .Where(o =>
                    {
                        var lb = LowerBound(newTimes, o.time + d - ShiftToleranceMs);
                        return newLeft
                            .Skip(lb)
                            .TakeWhile(n => n.time <= o.time + d + ShiftToleranceMs)
                            .Any(n => n.GetObjectType() == o.GetObjectType() && (BestUnstackedDistanceAndFlip(o, n).Distance <= 180 || BestUnstackedDistanceAndFlip(o, n).Flip != FlipType.None));
                    })
                    .ToList();

                foreach (var run in Runs(explained, all, index, settled).ToList())
                {
                    var first = index[run[0]];
                    var last = index[run[^1]];
                    var unsettled = all.Skip(first)
                        .Take(last - first + 1)
                        .Count(o => !settled.Contains(o));

                    if (run.Count < minimum || run.Count < unsettled * OffsetShare)
                        continue;

                    var runLeft = run.ToList();
                    var pairs = new List<(HitObject Old, HitObject New, double Delta, FlipType Flip)>();
                    PairWhere(
                        runLeft,
                        newLeft,
                        pairs,
                        d,
                        (o, n, flip) => Math.Abs(o.time + d - n.time) <= ShiftToleranceMs && (BestUnstackedDistanceAndFlip(o, n).Distance <= 180 || flip != FlipType.None)
                    );

                    var moved = pairs.Select(p => p.Old).ToHashSet();
                    oldLeft.RemoveAll(moved.Contains);
                    settled.UnionWith(moved);

                    groups.Add(
                        new ShiftGroup
                        {
                            Delta = d,
                            Pairs = pairs.Select(p => (p.Old, p.New, p.Flip)).ToList(),
                            FirstIndex = first,
                            LastIndex = last,
                        }
                    );
                }

                fruitless = groups.Count > found ? 0 : fruitless + 1;
            }

            return groups.OrderBy(g => g.FirstIndex).ToList();
        }

        private static double? DensestShiftRansac(
            List<HitObject> oldLeft,
            List<HitObject> newLeft,
            List<double> tried
        )
        {
            var deltas = new List<double>();
            var newTimes = newLeft.Select(n => n.time).ToArray();

            foreach (var o in oldLeft)
            {
                var type = o.GetObjectType();

                for (var i = LowerBound(newTimes, o.time - MaxShiftMs); i < newLeft.Count; i++)
                {
                    var n = newLeft[i];
                    if (n.time > o.time + MaxShiftMs)
                        break;

                    if (n.GetObjectType() != type)
                        continue;

                    var (dist, flip) = BestUnstackedDistanceAndFlip(o, n);
                    if (dist > 180 && flip == FlipType.None)
                        continue;

                    var delta = n.time - o.time;
                    if (Math.Abs(delta) >= 0.5 && !tried.Any(t => Math.Abs(t - delta) <= ShiftToleranceMs))
                        deltas.Add(delta);
                }
            }

            if (deltas.Count == 0)
                return null;

            deltas.Sort();
            int bestStart = 0;
            int bestCount = 0;

            for (int start = 0, end = 0; start < deltas.Count; start++)
            {
                while (end < deltas.Count && deltas[end] - deltas[start] <= 2 * ShiftToleranceMs)
                    end++;

                if (end - start > bestCount)
                {
                    bestStart = start;
                    bestCount = end - start;
                }
            }

            if (bestCount < OffsetMinimumObjects)
                return null;

            var group = deltas.GetRange(bestStart, bestCount);
            var median = group.Count % 2 == 1
                ? group[group.Count / 2]
                : (group[group.Count / 2 - 1] + group[group.Count / 2]) / 2;

            return Math.Round(median * 2) / 2;
        }

        private static IEnumerable<List<HitObject>> Runs(
            List<HitObject> objects,
            List<HitObject> all,
            Dictionary<HitObject, int> index,
            HashSet<HitObject> settled
        )
        {
            var run = new List<HitObject>();

            foreach (var o in objects.OrderBy(o => index[o]))
            {
                if (run.Count > 0)
                {
                    var from = index[run[^1]] + 1;
                    var to = index[o];

                    if (all.Skip(from).Take(to - from).Any(settled.Contains))
                    {
                        yield return run;
                        run = [];
                    }
                }

                run.Add(o);
            }

            if (run.Count > 0)
                yield return run;
        }

        private static void MarkWholeShift(List<ShiftGroup> groups, int objectCount)
        {
            foreach (var group in groups.OrderByDescending(g => g.Pairs.Count))
            {
                var alike = groups
                    .Where(g => Math.Abs(g.Delta - group.Delta) <= ShiftToleranceMs)
                    .ToList();

                if (alike.Sum(g => g.Pairs.Count) < objectCount * OffsetShare)
                    continue;

                foreach (var g in alike)
                    g.Whole = true;

                return;
            }
        }

        private static void SetGroupBounds(
            List<ShiftGroup> groups,
            List<HitObject> all,
            HashSet<HitObject> inPlace
        )
        {
            var owner = new Dictionary<HitObject, ShiftGroup>();

            foreach (var group in groups)
                foreach (var (o, _, _) in group.Pairs)
                    owner[o] = group;

            bool Bounds(HitObject o, ShiftGroup group) =>
                inPlace.Contains(o) || (owner.TryGetValue(o, out var other) && other != group);

            foreach (var group in groups)
            {
                var before = all.Take(group.FirstIndex).LastOrDefault(o => Bounds(o, group));
                var after = all.Skip(group.LastIndex + 1).FirstOrDefault(o => Bounds(o, group));

                group.From = before?.time ?? double.NegativeInfinity;
                group.To = after?.time ?? double.PositiveInfinity;
            }
        }

        private static IEnumerable<Change> DiffObject(
            Beatmap before,
            Beatmap after,
            HitObject o,
            HitObject n,
            double delta,
            FlipType flip
        )
        {
            var reference = ObjectRef(after, n);
            var type = n.GetObjectType();

            var retimed = n.time - o.time - delta;
            var snapTolerance = GetSnapTolerance(after, n.time);

            if (!WithinShift(retimed, delta) && Math.Abs(retimed) > snapTolerance)
            {
                yield return new Change(
                    ChangeKind.Rhythm,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Time",
                    Before: Format(o.time),
                    After: Format(n.time),
                    Magnitude: Math.Abs(retimed),
                    Object: reference
                );
            }

            var oldUnstacked = GetUnstackedPosition(o);
            var newUnstacked = GetUnstackedPosition(n);
            var distance = Vector2.Distance(oldUnstacked, newUnstacked);

            if (flip != FlipType.None)
            {
                yield return new Change(
                    ChangeKind.Placement,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Flipped",
                    Before: "Normal",
                    After: flip.ToString(),
                    Object: reference
                );
            }
            else if (after.GeneralSettings.mode == Beatmap.Mode.Mania)
            {
                var keys = after.DifficultySettings.circleSize;
                var oldColumn = ManiaExtensions.GetColumn(o, keys);
                var newColumn = ManiaExtensions.GetColumn(n, keys);

                if (oldColumn != newColumn)
                {
                    yield return new Change(
                        ChangeKind.Placement,
                        ChangeOp.Changed,
                        n.time,
                        Field: "Column",
                        Before: (oldColumn + 1).ToString(CultureInfo.InvariantCulture),
                        After: (newColumn + 1).ToString(CultureInfo.InvariantCulture),
                        Object: reference
                    );
                }
            }
            else if (distance > 0.001 && after.GeneralSettings.mode != Beatmap.Mode.Taiko)
            {
                yield return new Change(
                    ChangeKind.Placement,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Position",
                    Before: $"{Math.Round(oldUnstacked.X, 1)},{Math.Round(oldUnstacked.Y, 1)}",
                    After: $"{Math.Round(newUnstacked.X, 1)},{Math.Round(newUnstacked.Y, 1)}",
                    Magnitude: Math.Round(distance, 1),
                    Object: reference,
                    Minor: distance <= MinorMovePx
                );
            }

            var wasNewCombo = o.type.HasFlag(HitObject.Types.NewCombo);
            var isNewCombo = n.type.HasFlag(HitObject.Types.NewCombo);

            if (wasNewCombo != isNewCombo && after.GeneralSettings.mode is not (Beatmap.Mode.Mania or Beatmap.Mode.Taiko))
            {
                yield return new Change(
                    ChangeKind.Placement,
                    ChangeOp.Changed,
                    n.time,
                    Field: "NewCombo",
                    Before: wasNewCombo.ToString(),
                    After: isNewCombo.ToString(),
                    Object: reference
                );
            }

            if (Plays(o.hitSound) != Plays(n.hitSound))
            {
                var oldNote = after.GeneralSettings.mode == Beatmap.Mode.Taiko ? TaikoNote(o) : null;
                var newNote = after.GeneralSettings.mode == Beatmap.Mode.Taiko ? TaikoNote(n) : null;

                if (oldNote != newNote)
                {
                    yield return new Change(
                        ChangeKind.Placement,
                        ChangeOp.Changed,
                        n.time,
                        Field: "Note",
                        Before: oldNote,
                        After: newNote,
                        Object: reference
                    );
                }
                else
                {
                    yield return new Change(
                        ChangeKind.Hitsound,
                        ChangeOp.Changed,
                        n.time,
                        Field: "Hitsound",
                        Before: o.hitSound.ToString(),
                        After: n.hitSound.ToString(),
                        Object: reference
                    );
                }
            }

            if ((o.sampleset != n.sampleset || o.addition != n.addition)
                && (o.GetSampleset() != n.GetSampleset() || o.GetSampleset(true) != n.GetSampleset(true)))
            {
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Sampleset",
                    Before: o.sampleset + "/" + o.addition,
                    After: n.sampleset + "/" + n.addition,
                    Object: reference
                );
            }

            if ((o.customIndex ?? 0) != (n.customIndex ?? 0))
            {
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.time,
                    Field: "CustomIndex",
                    Before: (o.customIndex ?? 0).ToString(CultureInfo.InvariantCulture),
                    After: (n.customIndex ?? 0).ToString(CultureInfo.InvariantCulture),
                    Object: reference
                );
            }

            if (o.volume != n.volume && EffectiveVolume(o) != EffectiveVolume(n))
            {
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Volume",
                    Before: o.volume?.ToString(CultureInfo.InvariantCulture),
                    After: n.volume?.ToString(CultureInfo.InvariantCulture),
                    Magnitude: Math.Abs((n.volume ?? 0) - (o.volume ?? 0)),
                    Object: reference,
                    Minor: o.volume != null && n.volume != null && Math.Abs(n.volume.Value - o.volume.Value) <= 3
                );
            }

            if (o.filename != n.filename)
            {
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.time,
                    Field: "Filename",
                    Before: o.filename,
                    After: n.filename,
                    Object: reference
                );
            }

            if (o.GetObjectType() != type)
            {
                yield return new Change(
                    ChangeKind.Rhythm,
                    ChangeOp.Changed,
                    n.time,
                    EndTime: n.GetEndTime(),
                    Field: "Type",
                    Before: KindName(before, o),
                    After: KindName(after, n),
                    Object: reference
                );
            }
            else if (type == "Slider")
            {
                foreach (var change in DiffSlider(before, after, o, n, reference))
                    yield return change;
            }
            else if (type is "Spinner" or "Hold note")
            {
                var oldEnd = o.GetEndTime() + delta;
                var newEnd = n.GetEndTime();

                if (!WithinShift(newEnd - oldEnd, delta))
                {
                    yield return new Change(
                        ChangeKind.Rhythm,
                        ChangeOp.Changed,
                        n.time,
                        EndTime: newEnd,
                        Field: "EndTime",
                        Before: Format(oldEnd),
                        After: Format(newEnd),
                        Magnitude: Math.Abs(newEnd - oldEnd),
                        Object: reference
                    );
                }
            }
        }

        private static IEnumerable<Change> DiffSlider(
            Beatmap before,
            Beatmap after,
            HitObject o,
            HitObject n,
            ObjectRef reference
        )
        {
            var oldSlider = o as Slider ?? new Slider(o.code.Split(','), before);
            var newSlider = n as Slider ?? new Slider(n.code.Split(','), after);

            if (!oldSlider.PixelLength.AlmostEqual(newSlider.PixelLength))
            {
                yield return new Change(
                    ChangeKind.Rhythm,
                    ChangeOp.Changed,
                    n.time,
                    EndTime: newSlider.EndTime,
                    Field: "Length",
                    Before: Format(oldSlider.PixelLength),
                    After: Format(newSlider.PixelLength),
                    Magnitude: Math.Abs(newSlider.PixelLength - oldSlider.PixelLength),
                    Object: reference
                );
            }

            if (oldSlider.EdgeAmount != newSlider.EdgeAmount)
            {
                yield return new Change(
                    ChangeKind.Rhythm,
                    ChangeOp.Changed,
                    n.time,
                    EndTime: newSlider.EndTime,
                    Field: "Reverses",
                    Before: (oldSlider.EdgeAmount - 1).ToString(CultureInfo.InvariantCulture),
                    After: (newSlider.EdgeAmount - 1).ToString(CultureInfo.InvariantCulture),
                    Object: reference
                );
            }

            var shapeChanged =
                oldSlider.CurveType != newSlider.CurveType
                || oldSlider.NodePositions.Count != newSlider.NodePositions.Count
                || oldSlider.NodePositions.Skip(1).Zip(newSlider.NodePositions.Skip(1)).Any(p => p.First != p.Second);

            if (shapeChanged && after.GeneralSettings.mode != Beatmap.Mode.Taiko)
            {
                yield return new Change(
                    ChangeKind.Placement,
                    ChangeOp.Changed,
                    n.time,
                    EndTime: newSlider.EndTime,
                    Field: "Shape",
                    Object: reference
                );
            }

            var samplesChanged =
                oldSlider.StartHitSound != newSlider.StartHitSound
                || oldSlider.EndHitSound != newSlider.EndHitSound
                || oldSlider.StartSampleset != newSlider.StartSampleset
                || oldSlider.EndSampleset != newSlider.EndSampleset
                || oldSlider.StartAddition != newSlider.StartAddition
                || oldSlider.EndAddition != newSlider.EndAddition
                || !oldSlider.ReverseHitSounds.SequenceEqual(newSlider.ReverseHitSounds)
                || !oldSlider.ReverseSamplesets.SequenceEqual(newSlider.ReverseSamplesets)
                || !oldSlider.ReverseAdditions.SequenceEqual(newSlider.ReverseAdditions);

            if (samplesChanged && SlidersSoundDifferent(oldSlider, newSlider))
            {
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.time,
                    EndTime: newSlider.EndTime,
                    Field: "SliderSamples",
                    Object: reference
                );
            }
        }

        private static bool SlidersSoundDifferent(Slider a, Slider b)
        {
            if (Plays(a.StartHitSound) != Plays(b.StartHitSound) || Plays(a.EndHitSound) != Plays(b.EndHitSound))
                return true;

            var shared = Math.Min(a.EdgeAmount, b.EdgeAmount) - 1;
            for (var i = 0; i < shared; i++)
                if (Plays(a.ReverseHitSounds.ElementAtOrDefault(i)) != Plays(b.ReverseHitSounds.ElementAtOrDefault(i)))
                    return true;

            foreach (var additions in new[] { false, true })
            {
                if (a.GetStartSampleset(additions) != b.GetStartSampleset(additions)
                    || a.GetEndSampleset(additions) != b.GetEndSampleset(additions))
                    return true;

                for (var i = 0; i < shared; i++)
                    if (a.GetReverseSampleset(i, additions) != b.GetReverseSampleset(i, additions))
                        return true;
            }

            return false;
        }

        private static void DiffTiming(
            Beatmap before,
            Beatmap after,
            List<ShiftGroup> groups,
            List<Change> changes
        )
        {
            var oldLeft = before.TimingLines.ToList();
            var newLeft = after.TimingLines.ToList();

            void Pair(double delta, ShiftGroup? group)
            {
                foreach (var o in oldLeft.ToList())
                {
                    if (group != null && (o.Offset <= group.From || o.Offset >= group.To))
                        continue;

                    var n = newLeft.FirstOrDefault(l =>
                        l.Uninherited == o.Uninherited
                        && (delta == 0
                            ? AlmostEqual(o.Offset, l.Offset)
                            : Math.Abs(o.Offset + delta - l.Offset) <= ShiftToleranceMs)
                    );

                    if (n == null)
                        continue;

                    oldLeft.Remove(o);
                    newLeft.Remove(n);
                    group?.Lines.Add((o, n));

                    changes.AddRange(DiffLine(o, n));
                }
            }

            Pair(0, null);

            foreach (var group in groups)
                Pair(group.Delta, group);

            foreach (var l in oldLeft)
                changes.Add(LineChange(l, ChangeOp.Removed));

            foreach (var l in newLeft)
                changes.Add(LineChange(l, ChangeOp.Added));
        }

        private static Change LineChange(TimingLine line, ChangeOp op)
        {
            var sv = line.Uninherited ? null : Format(line.SvMult);
            return new(
                ChangeKind.Timing,
                op,
                line.Offset,
                Field: line.Uninherited ? "RedLine" : "GreenLine",
                Before: op == ChangeOp.Removed ? sv : null,
                After: op == ChangeOp.Added ? sv : null
            );
        }

        private static IEnumerable<Change> DiffLine(TimingLine o, TimingLine n)
        {
            var field = n.Uninherited ? "RedLine" : "GreenLine";

            if (o.Kiai != n.Kiai)
                yield return new Change(
                    ChangeKind.Timing,
                    ChangeOp.Changed,
                    n.Offset,
                    Field: "Kiai",
                    Before: o.Kiai.ToString(),
                    After: n.Kiai.ToString()
                );

            if (o.Meter != n.Meter)
                yield return new Change(
                    ChangeKind.Timing,
                    ChangeOp.Changed,
                    n.Offset,
                    Field: "Meter",
                    Before: o.Meter.ToString(CultureInfo.InvariantCulture),
                    After: n.Meter.ToString(CultureInfo.InvariantCulture)
                );

            if (o.Sampleset != n.Sampleset || o.CustomIndex != n.CustomIndex)
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.Offset,
                    Field: field + "Sampleset",
                    Before: o.Sampleset + "/" + o.CustomIndex,
                    After: n.Sampleset + "/" + n.CustomIndex
                );

            if (!o.Volume.AlmostEqual(n.Volume))
                yield return new Change(
                    ChangeKind.Hitsound,
                    ChangeOp.Changed,
                    n.Offset,
                    Field: field + "Volume",
                    Before: Format(o.Volume),
                    After: Format(n.Volume),
                    Magnitude: Math.Abs(n.Volume - o.Volume),
                    Minor: Math.Abs(n.Volume - o.Volume) <= 3
                );

            if (n.Uninherited)
            {
                var oldBpm = Bpm(o);
                var newBpm = Bpm(n);

                if (!oldBpm.AlmostEqual(newBpm, 1e-6))
                    yield return new Change(
                        ChangeKind.Timing,
                        ChangeOp.Changed,
                        n.Offset,
                        Field: "Bpm",
                        Before: Format(oldBpm),
                        After: Format(newBpm),
                        Magnitude: Math.Abs(newBpm - oldBpm)
                    );
            }
            else if (!o.SvMult.AlmostEqual(n.SvMult))
            {
                yield return new Change(
                    ChangeKind.Timing,
                    ChangeOp.Changed,
                    n.Offset,
                    Field: "Sv",
                    Before: Format(o.SvMult),
                    After: Format(n.SvMult),
                    Magnitude: Math.Abs(n.SvMult - o.SvMult),
                    Minor: Math.Abs(n.SvMult - o.SvMult) <= 0.02
                );
            }
        }

        private static readonly string[] VolumeFields = ["RedLineVolume", "GreenLineVolume"];

        private static void GroupVolumeChanges(
            Beatmap after,
            List<Change> changes,
            List<Rollup> rollups
        )
        {
            var volume = changes
                .Where(c => c.Op == ChangeOp.Changed && VolumeFields.Contains(c.Field))
                .GroupBy(c => (c.Time, c.Field == "RedLineVolume"))
                .ToDictionary(g => g.Key, g => g.First());

            if (volume.Count < OffsetMinimumObjects)
                return;

            var added = changes
                .Where(c => c.Op == ChangeOp.Added && c.Field is "RedLine" or "GreenLine")
                .Select(c => (c.Time, c.Field == "RedLine"))
                .ToHashSet();

            var runs = new List<List<Change>>();
            var run = new List<Change>();

            void Close()
            {
                if (run.Count > 0)
                    runs.Add(run);
                run = [];
            }

            foreach (var line in after.TimingLines.OrderBy(l => l.Offset))
            {
                if (added.Contains((line.Offset, line.Uninherited)))
                    continue;

                if (!volume.TryGetValue((line.Offset, line.Uninherited), out var change))
                {
                    Close();
                    continue;
                }

                if (run.Count > 0 && !Alike([.. run, change]))
                    Close();

                run.Add(change);
            }

            Close();

            foreach (var group in runs.Where(r => r.Count >= OffsetMinimumObjects))
            {
                changes.RemoveAll(group.Contains);

                var befores = group.Select(c => c.Before).Distinct().ToList();
                var afters = group.Select(c => c.After).Distinct().ToList();
                var deltas = group.Select(VolumeDelta).Distinct().ToList();
                var before = befores.Count == 1 ? befores[0] : null;
                var afterValue = afters.Count == 1 ? afters[0] : null;
                var delta = deltas.Count == 1 ? deltas[0] : (double?)null;

                if (group.Count >= after.TimingLines.Count * OffsetShare)
                {
                    rollups.Add(
                        new Rollup(RollupKind.Volume, delta ?? 0, group.Count, before, afterValue)
                    );
                }
                else
                {
                    changes.Add(
                        new Change(
                            ChangeKind.Hitsound,
                            ChangeOp.Changed,
                            group[0].Time,
                            EndTime: group[^1].Time,
                            Field: "VolumeGroup",
                            Before: before,
                            After: afterValue,
                            Magnitude: delta,
                            Count: group.Count
                        )
                    );
                }
            }
        }

        private static double VolumeDelta(Change c) =>
            double.Parse(c.After!, CultureInfo.InvariantCulture)
            - double.Parse(c.Before!, CultureInfo.InvariantCulture);

        private static bool Alike(List<Change> group) =>
            group.Select(VolumeDelta).Distinct().Count() == 1
            || group.Select(c => c.After).Distinct().Count() == 1;

        private static Change ShiftChange(ShiftGroup group, double wholeShift)
        {
            var oldStart = group.Pairs.Select(p => p.Old.time).Concat(group.Lines.Select(l => l.Old.Offset)).Min();
            var newStart = group.Pairs.Select(p => p.New.time).Concat(group.Lines.Select(l => l.New.Offset)).Min();
            var newEnd = group.Pairs.Select(p => p.New.GetEndTime()).Concat(group.Lines.Select(l => l.New.Offset)).Max();

            return new Change(
                ChangeKind.Timing,
                ChangeOp.Changed,
                newStart,
                EndTime: newEnd,
                Field: "Shift",
                Before: Format(oldStart),
                After: Format(newStart),
                Magnitude: group.Delta - wholeShift,
                Count: group.Pairs.Count + group.Lines.Count
            );
        }

        // ---------- Helpers ----------

        private static Vector2 GetUnstackedPosition(HitObject obj)
        {
            if (obj is Stackable stackable)
                return stackable.UnstackedPosition;
            return obj.Position;
        }

        private static double UnstackedDistance(HitObject a, HitObject b)
        {
            var p1 = GetUnstackedPosition(a);
            var p2 = GetUnstackedPosition(b);
            return Vector2.Distance(p1, p2);
        }

        private static (double Distance, FlipType Flip) BestUnstackedDistanceAndFlip(HitObject a, HitObject b)
        {
            var p1 = GetUnstackedPosition(a);
            var p2 = GetUnstackedPosition(b);
            var normalDist = Vector2.Distance(p1, p2);

            // Avoid false flips for objects sitting right at the center of the playfield (256, 192)
            bool isCenterObject = Math.Abs(p1.X - 256) < 1.0 && Math.Abs(p1.Y - 192) < 1.0;
            if (isCenterObject || a is Spinner || b is Spinner)
                return (normalDist, FlipType.None);

            double hFlipDist = Vector2.Distance(p1, new Vector2(512 - p2.X, p2.Y));
            double vFlipDist = Vector2.Distance(p1, new Vector2(p2.X, 384 - p2.Y));
            double bothFlipDist = Vector2.Distance(p1, new Vector2(512 - p2.X, 384 - p2.Y));

            if (a.beatmap?.GeneralSettings?.mode == Beatmap.Mode.Catch)
            {
                if (hFlipDist < 0.5 && hFlipDist < normalDist)
                    return (hFlipDist, FlipType.CatchHorizontal);
            }

            if (hFlipDist < 0.5 && hFlipDist < normalDist)
                return (hFlipDist, FlipType.Horizontal);

            if (vFlipDist < 0.5 && vFlipDist < normalDist)
                return (vFlipDist, FlipType.Vertical);

            if (bothFlipDist < 0.5 && bothFlipDist < normalDist)
                return (bothFlipDist, FlipType.Both);

            return (normalDist, FlipType.None);
        }

        private static double GetSnapTolerance(Beatmap beatmap, double time) =>
            beatmap.GetTimingLine<UninheritedLine>(time)?.msPerBeat / 32 ?? 2.0;

        private static double Bpm(TimingLine line) =>
            60000 / double.Parse(line.Code.Split(',')[1], CultureInfo.InvariantCulture);

        private static string Format(double value) =>
            Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

        private static bool AlmostEqual(double a, double b) => Math.Abs(a - b) < 0.5;

        private static bool WithinShift(double difference, double shift) =>
            shift == 0 ? difference.AlmostEqual(0) : Math.Abs(difference) <= ShiftToleranceMs;

        private static HitObject.HitSounds Plays(HitObject.HitSounds sound) =>
            sound == HitObject.HitSounds.None ? HitObject.HitSounds.Normal : sound;

        private static double EffectiveVolume(HitObject o) =>
            o.volume ?? o.beatmap.GetTimingLine(o.time, true)?.Volume ?? 0;

        private static string KindName(Beatmap map, HitObject o) =>
            map.GeneralSettings.mode switch
            {
                Beatmap.Mode.Taiko => TaikoNote(o),
                Beatmap.Mode.Mania => o.GetObjectType() == "Hold note" ? "Hold note" : "Note",
                _ => o.GetObjectType(),
            };

        private static string TaikoNote(HitObject o)
        {
            var big = o.hitSound.HasFlag(HitObject.HitSounds.Finish);
            var type = o.GetObjectType();

            if (type == "Spinner")
                return "Shaker";
            if (type == "Slider")
                return big ? "Big drumroll" : "Drumroll";

            var kat = o.hitSound.HasFlag(HitObject.HitSounds.Whistle) || o.hitSound.HasFlag(HitObject.HitSounds.Clap);
            return big ? (kat ? "Big kat" : "Big don") : (kat ? "Kat" : "Don");
        }

        private static Change ObjectChange(Beatmap map, HitObject o, ChangeOp op) =>
            new(
                ChangeKind.Rhythm,
                op,
                o.time,
                EndTime: o.GetEndTime(),
                Field: "Object",
                Object: ObjectRef(map, o)
            );

        private static ObjectRef ObjectRef(Beatmap map, HitObject o)
        {
            string stamp;
            try
            {
                stamp = Timestamp.Get(o);
            }
            catch
            {
                stamp = Timestamp.Get(o.time);
            }
            return new ObjectRef(o.time, o.GetObjectType(), stamp);
        }

        private static int LowerBound(double[] sortedTimes, double value)
        {
            int low = 0, high = sortedTimes.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (sortedTimes[mid] < value)
                    low = mid + 1;
                else
                    high = mid;
            }
            return low;
        }
    }
}
