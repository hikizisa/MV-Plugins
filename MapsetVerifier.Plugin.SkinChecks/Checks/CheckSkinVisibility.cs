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
    public class CheckSkinVisibility : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Invisible gameplay elements",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "InvisibleGameplayElement",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" is completely transparent/invisible. Gameplay elements must be visible to ensure fair and readable play.",
                        "File Name")
                }
            };

        private static readonly HashSet<string> ExemptElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "cursormiddle",
            "fruit-drop-empty",
            "fail-background",
            "pause-overlay"
        };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var assets = SkinScanner.GetSkinAssets(beatmapSet);
            var issues = new List<Issue>();

            foreach (var asset in assets)
            {
                if (ExemptElements.Contains(asset.RawBaseName))
                    continue;

                // Only check gameplay elements registered in SkinRegistry
                bool isGameplayElement = SkinRegistry.AllSets.Any(s => s.Elements.Any(e => e.Matches(asset)));
                if (!isGameplayElement)
                    continue;

                bool hasAnyVisiblePixel = false;
                bool loadSuccess = false;
                try
                {
                    using var image = Image.Load<Rgba32>(asset.FullPath);
                    loadSuccess = true;
                    for (int y = 0; y < image.Height; y++)
                    {
                        for (int x = 0; x < image.Width; x++)
                        {
                            if (image[x, y].A > 5)
                            {
                                hasAnyVisiblePixel = true;
                                break;
                            }
                        }
                        if (hasAnyVisiblePixel) break;
                    }
                }
                catch
                {
                    // Corrupt files are handled in CheckSkinFormat
                }

                if (loadSuccess && !hasAnyVisiblePixel)
                {
                    issues.Add(new Issue(GetTemplate("InvisibleGameplayElement"), null, asset.RelativePath));
                }
            }

            return issues;
        }
    }
}
