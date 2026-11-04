using System;
using System.Linq;
using MapsetVerifier.Plugin.CustomSnapshots;
using Xunit;

namespace MV_Robust_Snapshot.Tests
{
    public class SequenceAlignmentTests
    {
        [Theory]
        [InlineData(600)]
        [InlineData(600.000005)]
        public void AlignmentPreservesExactAndNearlyEqualMatchTies(double cost)
        {
            var path = SequenceAlignment.Align(
                new[] { 0 },
                new[] { 0 },
                time => time,
                (_, _) => cost,
                5000
            );
            Assert.Equal(Tuple.Create(0, 0), Assert.Single(path));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 2)]
        [InlineData(2, 0)]
        public void AlignmentHandlesEmptyInputs(int oldCount, int newCount)
        {
            var path = SequenceAlignment.Align(
                Enumerable.Range(0, oldCount).ToArray(),
                Enumerable.Range(0, newCount).ToArray(),
                time => time,
                (_, _) => 0,
                5000
            );
            Assert.Equal(oldCount + newCount, path.Count);
            Assert.All(path, pair => Assert.True(pair.Item1 == -1 || pair.Item2 == -1));
        }

        [Theory]
        [InlineData(5000, 1)]
        [InlineData(5001, 2)]
        public void AlignmentHonorsTheTimeWindow(int newTime, int expectedSteps)
        {
            var path = SequenceAlignment.Align(
                new[] { 0 },
                new[] { newTime },
                time => time,
                (_, _) => 0,
                5000
            );
            Assert.Equal(expectedSteps, path.Count);
        }
    }
}
