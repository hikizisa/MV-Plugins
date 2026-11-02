using System;
using System.Collections.Generic;
using System.Linq;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Framework.Objects.Attributes;
using MapsetVerifier.Framework.Objects.Metadata;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Plugin.SkinChecks.Model;

namespace MapsetVerifier.Plugin.SkinChecks.Checks
{
    [Check]
    public class CheckUnusedSkinFiles : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Mode-mismatched skin files",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "ModeMismatch",
                    new IssueTemplate(
                        Issue.Level.Warning,
                        "\"{0}\" is an {1} skin element, but this mapset does not contain any {1} difficulties.",
                        "File Name", "Target Mode")
                }
            };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var presentModes = SkinScanner.GetPresentModes(beatmapSet);
            var assets = SkinScanner.GetSkinAssets(beatmapSet);

            foreach (var asset in assets)
            {
                // Find which specific mode set(s) this asset belongs to
                var matchingSets = SkinRegistry.AllSets
                    .Where(s => s.Mode != SkinMode.General && s.Elements.Any(e => e.Matches(asset)))
                    .ToList();

                foreach (var set in matchingSets)
                {
                    if (!presentModes.Contains(set.Mode))
                    {
                        string modeName = set.Mode switch
                        {
                            SkinMode.Standard => "osu!",
                            SkinMode.Taiko => "osu!taiko",
                            SkinMode.Catch => "osu!catch",
                            SkinMode.Mania => "osu!mania",
                            _ => set.Mode.ToString()
                        };

                        yield return new Issue(GetTemplate("ModeMismatch"), null, asset.RelativePath, modeName);
                        break;
                    }
                }
            }
        }
    }
}
