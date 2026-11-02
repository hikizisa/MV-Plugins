using System;
using System.Collections.Generic;
using System.IO;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Framework.Objects.Attributes;
using MapsetVerifier.Framework.Objects.Metadata;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Plugin.SkinChecks.Model;
using SixLabors.ImageSharp;

namespace MapsetVerifier.Plugin.SkinChecks.Checks
{
    [Check]
    public class CheckSkinDimensions : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Skin element dimensions",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "CatchDimensionMismatch",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" has dimensions {1}x{2} px, expected {3}x{4} px to match the osu!catch hitbox.",
                        "File Name", "Actual Width", "Actual Height", "Expected Width", "Expected Height")
                },
                {
                    "ComboburstTooTall",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" exceeds maximum allowed height ({1} px > {2} px).",
                        "File Name", "Actual Height", "Max Height")
                },
                {
                    "UnreadableImage",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "Could not read image dimensions from \"{0}\". File may be corrupted.",
                        "File Name")
                }
            };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var assets = SkinScanner.GetSkinAssets(beatmapSet);
            var issues = new List<Issue>();

            foreach (var asset in assets)
            {
                // Check if this is an osu!catch element or comboburst
                bool isCatchFruit = asset.RawBaseName.StartsWith("fruit-apple") ||
                                    asset.RawBaseName.StartsWith("fruit-bananas") ||
                                    asset.RawBaseName.StartsWith("fruit-grapes") ||
                                    asset.RawBaseName.StartsWith("fruit-orange");

                bool isCatchDrop = asset.RawBaseName.Equals("fruit-drop") ||
                                   asset.RawBaseName.Equals("fruit-drop-overlay");

                bool isCatchCatcher = asset.RawBaseName.StartsWith("fruit-catcher");

                bool isComboburst = asset.RawBaseName.StartsWith("comboburst");

                if (!isCatchFruit && !isCatchDrop && !isCatchCatcher && !isComboburst)
                    continue;

                ImageInfo? info = null;
                bool readFailed = false;
                try
                {
                    info = Image.Identify(asset.FullPath);
                    if (info == null)
                    {
                        readFailed = true;
                    }
                }
                catch
                {
                    readFailed = true;
                }

                if (readFailed || info == null)
                {
                    issues.Add(new Issue(GetTemplate("UnreadableImage"), null, asset.RelativePath));
                    continue;
                }

                int scale = asset.IsRetina ? 2 : 1;

                if (isCatchFruit)
                {
                    int expectedW = 128 * scale;
                    int expectedH = 128 * scale;
                    if (info.Width != expectedW || info.Height != expectedH)
                    {
                        issues.Add(new Issue(GetTemplate("CatchDimensionMismatch"), null,
                            asset.RelativePath, info.Width, info.Height, expectedW, expectedH));
                    }
                }
                else if (isCatchDrop)
                {
                    int expectedW = 82 * scale;
                    int expectedH = 103 * scale;
                    if (info.Width != expectedW || info.Height != expectedH)
                    {
                        issues.Add(new Issue(GetTemplate("CatchDimensionMismatch"), null,
                            asset.RelativePath, info.Width, info.Height, expectedW, expectedH));
                    }
                }
                else if (isCatchCatcher)
                {
                    int expectedW = 306 * scale;
                    int expectedH = 320 * scale;
                    if (info.Width != expectedW || info.Height != expectedH)
                    {
                        issues.Add(new Issue(GetTemplate("CatchDimensionMismatch"), null,
                            asset.RelativePath, info.Width, info.Height, expectedW, expectedH));
                    }
                }
                else if (isComboburst)
                {
                    int maxH = 768 * scale;
                    if (info.Height > maxH)
                    {
                        issues.Add(new Issue(GetTemplate("ComboburstTooTall"), null,
                            asset.RelativePath, info.Height, maxH));
                    }
                }
            }

            return issues;
        }
    }
}
