using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Framework.Objects.Attributes;
using MapsetVerifier.Framework.Objects.Metadata;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Plugin.SkinChecks.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapsetVerifier.Plugin.SkinChecks.Checks
{
    [Check]
    public class CheckCatchSkinRules : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "osu!catch catcher format and fruit colors",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "CatcherV2Required",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "Custom catcher elements are included, but skin.ini does not specify \"Version: 2\" or higher under [General] (found: {0}). Custom catchers require v2 skin format.",
                        "Current Version")
                },
                {
                    "FruitNotGreyscale",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" contains coloured pixels ({1}% non-greyscale). Custom fruits must be coloured in a scale of grey colours so combo colours can tint them.",
                        "File Name", "Colored Percentage")
                }
            };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var assets = SkinScanner.GetSkinAssets(beatmapSet);
            var issues = new List<Issue>();
            if (!assets.Any())
                return issues;

            // 1. Check Catcher v2 requirement
            bool hasCustomCatcher = assets.Any(a => a.RawBaseName.StartsWith("fruit-catcher"));
            if (hasCustomCatcher)
            {
                string skinIniPath = Path.Combine(beatmapSet.SongPath, "skin.ini");
                var ini = SkinIni.Parse(skinIniPath);
                if (ini.Version < 2.0)
                {
                    string verStr = File.Exists(skinIniPath) ? ini.Version.ToString("0.0") : "missing skin.ini (defaults to 1.0)";
                    issues.Add(new Issue(GetTemplate("CatcherV2Required"), null, verStr));
                }
            }

            // 2. Check Fruits greyscale requirement (only base fruit sprites, not overlays)
            string[] fruitBases = { "fruit-apple", "fruit-bananas", "fruit-grapes", "fruit-orange" };
            foreach (var asset in assets)
            {
                if (!fruitBases.Contains(asset.RawBaseName))
                    continue;

                int nonGreyscalePixels = 0;
                int visiblePixels = 0;

                try
                {
                    using var image = Image.Load<Rgba32>(asset.FullPath);
                    int sampleStep = Math.Max(1, (image.Width * image.Height) / 5000);

                    for (int y = 0; y < image.Height; y += (int)Math.Max(1, Math.Sqrt(sampleStep)))
                    {
                        for (int x = 0; x < image.Width; x += (int)Math.Max(1, Math.Sqrt(sampleStep)))
                        {
                            var pixel = image[x, y];
                            if (pixel.A < 15) continue;

                            visiblePixels++;
                            int diffRG = Math.Abs(pixel.R - pixel.G);
                            int diffGB = Math.Abs(pixel.G - pixel.B);
                            int diffRB = Math.Abs(pixel.R - pixel.B);

                            if (diffRG > 12 || diffGB > 12 || diffRB > 12)
                            {
                                nonGreyscalePixels++;
                            }
                        }
                    }

                    if (visiblePixels > 50 && (nonGreyscalePixels / (double)visiblePixels) > 0.05)
                    {
                        double percent = Math.Round((nonGreyscalePixels / (double)visiblePixels) * 100, 1);
                        issues.Add(new Issue(GetTemplate("FruitNotGreyscale"), null, asset.RelativePath, percent));
                    }
                }
                catch
                {
                    // Handled by CheckSkinFormat
                }
            }

            return issues;
        }
    }
}
