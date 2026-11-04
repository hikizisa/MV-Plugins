using System;
using System.Collections.Generic;
using System.Linq;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Parser.Objects.HitObjects;
using MapsetVerifier.Plugin.CustomSnapshots;
using MapsetVerifier.Snapshots.Diffing;
using Xunit;

namespace MV_Robust_Snapshot.Tests
{
    public class RobustBeatmapDifferTests
    {
        private static Beatmap CreateBeatmap(string hitObjects, string mode = "0", string timing = "0,500,4,1,0,100,1,0\n")
        {
            string code = $"osu file format v14\n\n[General]\nMode: {mode}\n\n[Metadata]\nTitle:Test\nArtist:Test\nCreator:Test\nVersion:Test\n\n[Difficulty]\nCircleSize:4\nSliderMultiplier:1.4\nSliderTickRate:1\n\n[TimingPoints]\n{timing}\n[HitObjects]\n{hitObjects}\n";
            return new Beatmap(code, "test", $"dummy_{Guid.NewGuid():N}.osu");
        }

        [Fact]
        public void HorizontalFlip_ReportedAsFlipped_NotAsRemovalsAndAdditions()
        {
            // Object at (100, 100) -> Horizontally flipped: (512 - 100, 100) = (412, 100)
            var before = CreateBeatmap("100,100,1000,1,0,0:0:0:0:\n");
            var after = CreateBeatmap("412,100,1000,1,0,0:0:0:0:\n");

            var result = RobustBeatmapDiffer.Diff(before, after);

            // Must NOT report 1 removed and 1 added
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Removed);
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Added);

            // Must report Flipped Horizontal
            var flipChange = Assert.Single(result.Changes, c => c.Field == "Flipped");
            Assert.Equal("Horizontal", flipChange.After);
        }

        [Fact]
        public void VerticalFlip_ReportedAsFlipped()
        {
            // Object at (100, 100) -> Vertically flipped: (100, 384 - 100) = (100, 284)
            var before = CreateBeatmap("100,100,1000,1,0,0:0:0:0:\n");
            var after = CreateBeatmap("100,284,1000,1,0,0:0:0:0:\n");

            var result = RobustBeatmapDiffer.Diff(before, after);

            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Removed);
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Added);

            var flipChange = Assert.Single(result.Changes, c => c.Field == "Flipped");
            Assert.Equal("Vertical", flipChange.After);
        }

        [Fact]
        public void CatchHorizontalFlip_ReportedAsCatchHorizontal()
        {
            var before = CreateBeatmap("100,100,1000,1,0,0:0:0:0:\n", mode: "2");
            var after = CreateBeatmap("412,100,1000,1,0,0:0:0:0:\n", mode: "2");

            var result = RobustBeatmapDiffer.Diff(before, after);

            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Removed);
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Added);

            var flipChange = Assert.Single(result.Changes, c => c.Field == "Flipped");
            Assert.Equal("CatchHorizontal", flipChange.After);
        }

        [Fact]
        public void LargeShift_Beyond1000ms_CorrectlyGroupedAsOffsetRollup()
        {
            // Shift whole map by +2500ms (Base MV's MaxShiftMs is hardcoded to 1000ms and fails here!)
            int shift = 2500;
            string beforeObjects = string.Concat(Enumerable.Range(1, 8).Select(i => $"100,100,{i * 1000},1,0,0:0:0:0:\n"));
            string afterObjects = string.Concat(Enumerable.Range(1, 8).Select(i => $"100,100,{i * 1000 + shift},1,0,0:0:0:0:\n"));

            string beforeTiming = "0,500,4,1,0,100,1,0\n";
            string afterTiming = $"{shift},500,4,1,0,100,1,0\n";

            var before = CreateBeatmap(beforeObjects, timing: beforeTiming);
            var after = CreateBeatmap(afterObjects, timing: afterTiming);

            var result = RobustBeatmapDiffer.Diff(before, after);

            // Must produce an Offset Rollup with amount 2500ms
            var rollup = Assert.Single(result.Rollups, r => r.Kind == RollupKind.Offset);
            Assert.Equal(2500, rollup.Amount);

            // Individual objects should be absorbed, not spammed as removed/added
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Removed);
            Assert.DoesNotContain(result.Changes, c => c.Op == ChangeOp.Added);
        }

        [Fact]
        public void LocalTimingShift_SectionShiftReported()
        {
            // 5 notes moved together by -231ms
            string beforeObjects = @"100,100,9215,1,0,0:0:0:0:
120,120,9446,1,0,0:0:0:0:
140,140,9677,1,0,0:0:0:0:
160,160,9908,1,0,0:0:0:0:
180,180,10139,1,0,0:0:0:0:
";
            string afterObjects = @"200,200,8984,1,0,0:0:0:0:
220,220,9215,1,0,0:0:0:0:
240,240,9446,1,0,0:0:0:0:
260,260,9677,1,0,0:0:0:0:
280,280,9908,1,0,0:0:0:0:
";
            var before = CreateBeatmap(beforeObjects);
            var after = CreateBeatmap(afterObjects);

            var result = RobustBeatmapDiffer.Diff(before, after);

            // Section shift must be detected
            Assert.NotEmpty(result.Rollups.Concat<object>(result.Changes));
            Assert.True(result.Rollups.Any(r => r.Kind == RollupKind.Offset && Math.Abs(r.Amount - (-231)) <= 2.0)
                || result.Changes.Any(c => c.Field == "Shift" && Math.Abs((c.Magnitude ?? 0) - (-231)) <= 2.0));
        }

        [Fact]
        public void CenterObjectsAndSpinners_NoFalseFlipsOrMoves()
        {
            var before = CreateBeatmap("256,192,1000,1,0,0:0:0:0:\n256,192,2000,12,0,2500,0:0:0:0:\n");
            var after = CreateBeatmap("256,192,1000,1,0,0:0:0:0:\n256,192,2000,12,0,2500,0:0:0:0:\n");

            var result = RobustBeatmapDiffer.Diff(before, after);

            Assert.Empty(result.Changes);
            Assert.Empty(result.Rollups);
        }

        [Fact]
        public void DetourHelper_SuccessfullyRedirectsBeatmapDiffer()
        {
            var targetMethod = typeof(BeatmapDiffer).GetMethod("Diff", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var hookMethod = typeof(RobustBeatmapDiffer).GetMethod("Diff", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var hooked = DetourHelper.TryDetour(targetMethod!, hookMethod!);
            Assert.True(hooked);

            // Now calling base BeatmapDiffer.Diff directly must execute RobustBeatmapDiffer.Diff!
            var before = CreateBeatmap("100,100,1000,1,0,0:0:0:0:\n");
            var after = CreateBeatmap("412,100,1000,1,0,0:0:0:0:\n");

            // If detoured to RobustBeatmapDiffer, it will detect Horizontal Flip (which base BeatmapDiffer cannot do!)
            var result = BeatmapDiffer.Diff(before, after);

            var flipChange = Assert.Single(result.Changes, c => c.Field == "Flipped");
            Assert.Equal("Horizontal", flipChange.After);
        }
    }
}
