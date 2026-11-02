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

namespace MapsetVerifier.Plugin.SkinChecks.Checks
{
    [Check]
    public class CheckSkinFormat : GeneralCheck
    {
        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Skin file format and integrity",
                Author = "MapsetVerifier"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() =>
            new Dictionary<string, IssueTemplate>
            {
                {
                    "ZeroBytes",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" is 0 bytes and contains no image data.",
                        "File Name")
                },
                {
                    "CorruptImage",
                    new IssueTemplate(
                        Issue.Level.Problem,
                        "\"{0}\" is corrupt or not a valid image file ({1}).",
                        "File Name", "Error Message")
                }
            };

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet)
        {
            var assets = SkinScanner.GetSkinAssets(beatmapSet);
            var issues = new List<Issue>();

            foreach (var asset in assets)
            {
                var fileInfo = new FileInfo(asset.FullPath);
                if (fileInfo.Length == 0)
                {
                    issues.Add(new Issue(GetTemplate("ZeroBytes"), null, asset.RelativePath));
                    continue;
                }

                string? corruptError = null;
                try
                {
                    var info = Image.Identify(asset.FullPath);
                    if (info == null)
                    {
                        corruptError = "unrecognized image format";
                    }
                }
                catch (Exception ex)
                {
                    corruptError = ex.Message;
                }

                if (corruptError != null)
                {
                    issues.Add(new Issue(GetTemplate("CorruptImage"), null, asset.RelativePath, corruptError));
                }
            }

            return issues;
        }
    }
}
