using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Plugin.SkinChecks.Checks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MV_Robust_Snapshot.Tests
{
    public class SkinChecksTests : IDisposable
    {
        private readonly string _tempDir;

        public SkinChecksTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "MV_Skin_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        private BeatmapSet CreateMockBeatmapSet(Beatmap.Mode mode = Beatmap.Mode.Standard, bool withSpinner = false)
        {
            return BeatmapSetHelper.CreateMock(_tempDir, mode, withSpinner);
        }

        private void CreateTestPng(string fileName, int width, int height, Rgba32 color)
        {
            string path = Path.Combine(_tempDir, fileName);
            using var image = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    image[x, y] = color;
                }
            }
            image.SaveAsPng(path);
        }

        [Fact]
        public void TestCursorMissingTrail()
        {
            CreateTestPng("cursor.png", 64, 64, new Rgba32(255, 255, 255, 255));
            var set = CreateMockBeatmapSet();

            var check = new CheckCompleteSkinSets();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("cursortrail.png"));
        }

        [Fact]
        public void TestCursorComplete()
        {
            CreateTestPng("cursor.png", 64, 64, new Rgba32(255, 255, 255, 255));
            CreateTestPng("cursortrail.png", 64, 64, new Rgba32(255, 255, 255, 255));
            var set = CreateMockBeatmapSet();

            var check = new CheckCompleteSkinSets();
            var issues = check.GetIssues(set).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void TestHitcircleSetMissingApproachCircle()
        {
            CreateTestPng("hitcircle.png", 128, 128, new Rgba32(200, 200, 200, 255));
            CreateTestPng("hitcircleoverlay.png", 128, 128, new Rgba32(255, 255, 255, 255));
            var set = CreateMockBeatmapSet();

            var check = new CheckCompleteSkinSets();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("Hitcircle Set") && i.message.Contains("approachcircle.png"));
        }

        [Fact]
        public void TestComboNumbersIncompleteGroup()
        {
            CreateTestPng("hitcircle.png", 128, 128, new Rgba32(200, 200, 200, 255));
            CreateTestPng("hitcircleoverlay.png", 128, 128, new Rgba32(255, 255, 255, 255));
            CreateTestPng("approachcircle.png", 128, 128, new Rgba32(255, 255, 255, 255));
            CreateTestPng("default-0.png", 32, 32, new Rgba32(255, 255, 255, 255));

            var set = CreateMockBeatmapSet();
            var check = new CheckCompleteSkinSets();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("ComboNumbers"));
        }

        [Fact]
        public void TestCatchFruitWrongDimension()
        {
            CreateTestPng("fruit-apple.png", 100, 100, new Rgba32(128, 128, 128, 255));
            var set = CreateMockBeatmapSet(Beatmap.Mode.Catch);

            var check = new CheckSkinDimensions();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("100x100 px"));
        }

        [Fact]
        public void TestCatchFruitCorrectDimension()
        {
            CreateTestPng("fruit-apple.png", 128, 128, new Rgba32(128, 128, 128, 255));
            var set = CreateMockBeatmapSet(Beatmap.Mode.Catch);

            var check = new CheckSkinDimensions();
            var issues = check.GetIssues(set).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void TestCatchCatcherRequiresSkinIniV2()
        {
            CreateTestPng("fruit-catcher-idle.png", 306, 320, new Rgba32(128, 128, 128, 255));
            var set = CreateMockBeatmapSet(Beatmap.Mode.Catch);

            var check = new CheckCatchSkinRules();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("Version: 2"));

            File.WriteAllText(Path.Combine(_tempDir, "skin.ini"), "[General]\nVersion: 2.5\n");
            var issuesAfter = check.GetIssues(set).ToList();
            Assert.DoesNotContain(issuesAfter, i => i.message.Contains("Version: 2"));
        }

        [Fact]
        public void TestCatchFruitGreyscaleRequirement()
        {
            CreateTestPng("fruit-apple.png", 128, 128, new Rgba32(255, 0, 0, 255));
            var set = CreateMockBeatmapSet(Beatmap.Mode.Catch);

            var check = new CheckCatchSkinRules();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("non-greyscale"));

            CreateTestPng("fruit-apple.png", 128, 128, new Rgba32(160, 160, 160, 255));
            var issuesAfter = check.GetIssues(set).ToList();
            Assert.DoesNotContain(issuesAfter, i => i.message.Contains("non-greyscale"));
        }

        [Fact]
        public void TestInvisibleGameplayElement()
        {
            CreateTestPng("hitcircle.png", 128, 128, new Rgba32(0, 0, 0, 0));
            var set = CreateMockBeatmapSet();

            var check = new CheckSkinVisibility();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("transparent/invisible"));

            CreateTestPng("cursormiddle.png", 32, 32, new Rgba32(0, 0, 0, 0));
            var issuesWithMiddle = check.GetIssues(set).ToList();
            Assert.DoesNotContain(issuesWithMiddle, i => i.message.Contains("cursormiddle.png"));
        }

        [Fact]
        public void TestModeMismatchWarning()
        {
            CreateTestPng("mania-hit300.png", 64, 64, new Rgba32(255, 255, 255, 255));
            var set = CreateMockBeatmapSet(Beatmap.Mode.Standard);

            var check = new CheckUnusedSkinFiles();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("osu!mania skin element"));
        }

        [Fact]
        public void TestZeroByteFile()
        {
            string zeroFile = Path.Combine(_tempDir, "cursor.png");
            File.WriteAllBytes(zeroFile, Array.Empty<byte>());
            var set = CreateMockBeatmapSet();

            var check = new CheckSkinFormat();
            var issues = check.GetIssues(set).ToList();

            Assert.Contains(issues, i => i.message.Contains("0 bytes"));
        }
    }
}
