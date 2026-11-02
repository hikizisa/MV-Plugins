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
    public class CheckCompleteSkinSets : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Incomplete skin sets",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "MissingRequired",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "Skin set \"{0}\" is incomplete. Missing required element(s): {1}.",
                        "Set Name", "Missing Elements")
                },
                {
                    "IncompleteGroup",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "Group \"{0}\" in skin set \"{1}\" is partially skinned. Missing element(s): {2}.",
                        "Group Name", "Set Name", "Missing Elements")
                },
                {
                    "MissingCursorTrail",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "Skin file \"{0}\" is included, but \"{1}\" is missing. Both must be provided together.",
                        "Included File", "Missing File")
                }
            };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var assets = SkinScanner.GetSkinAssets(beatmapSet);
            if (!assets.Any())
                yield break;

            bool hasSpinners = SkinScanner.HasSpinners(beatmapSet);

            foreach (var set in SkinRegistry.AllSets)
            {
                // Skip spinner checks if beatmapset has no spinners at all
                if (set.Name.StartsWith("Spinner", StringComparison.OrdinalIgnoreCase) && !hasSpinners)
                    continue;

                // Find all assets that match elements in this set
                var matchedAssets = new List<(SkinElementRequirement Requirement, SkinAssetInfo Asset)>();
                foreach (var elem in set.Elements)
                {
                    var found = assets.Where(elem.Matches);
                    foreach (var f in found)
                    {
                        matchedAssets.Add((elem, f));
                    }
                }

                if (!matchedAssets.Any())
                    continue;

                // 1. Special Cursor check: cursor & cursortrail
                if (set.Name.Equals("Cursor Set", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasCursor = matchedAssets.Any(m => m.Requirement.NormalizedBaseName == "cursor");
                    bool hasTrail = matchedAssets.Any(m => m.Requirement.NormalizedBaseName == "cursortrail");
                    if (hasCursor && !hasTrail)
                    {
                        yield return new Issue(GetTemplate("MissingCursorTrail"), null, "cursor.png", "cursortrail.png");
                    }
                    else if (!hasCursor && hasTrail)
                    {
                        yield return new Issue(GetTemplate("MissingCursorTrail"), null, "cursortrail.png", "cursor.png");
                    }
                }

                // 2. Required elements check
                var missingRequired = new List<string>();
                foreach (var req in set.RequiredElements)
                {
                    bool isPresent = matchedAssets.Any(m => m.Requirement.NormalizedBaseName == req.NormalizedBaseName);
                    if (!isPresent)
                    {
                        missingRequired.Add(req.Pattern);
                    }
                }

                if (missingRequired.Any())
                {
                    // Special case: old vs new spinner style.
                    if (set.Name.Equals("Spinner Set (Old Style)", StringComparison.OrdinalIgnoreCase))
                    {
                        bool hasNewStyle = assets.Any(a => a.RawBaseName.StartsWith("spinner-top") || a.RawBaseName.StartsWith("spinner-bottom"));
                        if (hasNewStyle) continue;
                    }
                    if (set.Name.Equals("Spinner Set (New Style)", StringComparison.OrdinalIgnoreCase))
                    {
                        bool hasOldStyle = assets.Any(a => a.RawBaseName.Equals("spinner-background") || a.RawBaseName.Equals("spinner-circle"));
                        if (hasOldStyle) continue;
                    }

                    yield return new Issue(GetTemplate("MissingRequired"), null, set.Name, string.Join(", ", missingRequired));
                }

                // 3. Grouped optional elements check
                foreach (var group in set.GroupedElements)
                {
                    var groupElems = group.ToList();
                    bool anyInGroup = groupElems.Any(ge => matchedAssets.Any(m => m.Requirement.NormalizedBaseName == ge.NormalizedBaseName));
                    if (!anyInGroup) continue;

                    var missingInGroup = new List<string>();
                    foreach (var ge in groupElems)
                    {
                        bool isPresent = matchedAssets.Any(m => m.Requirement.NormalizedBaseName == ge.NormalizedBaseName);
                        if (!isPresent)
                        {
                            missingInGroup.Add(ge.Pattern);
                        }
                    }

                    if (missingInGroup.Any())
                    {
                        yield return new Issue(GetTemplate("IncompleteGroup"), null, group.Key, set.Name, string.Join(", ", missingInGroup));
                    }
                }
            }
        }
    }
}
