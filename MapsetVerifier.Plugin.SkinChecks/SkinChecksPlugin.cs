using System;
using System.Collections.Generic;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Framework.Objects.Attributes;
using MapsetVerifier.Framework.Objects.Metadata;
using MapsetVerifier.Parser.Objects;
using Serilog;

namespace MapsetVerifier.Plugin.SkinChecks
{
    [Check]
    public class SkinChecksPlugin : GeneralCheck
    {
        static SkinChecksPlugin()
        {
            Log.Information("[SkinChecksPlugin] MapsetVerifier Skin Checks Plugin loaded successfully.");
        }

        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Skinning",
                Message = "Skin Checks Plugin Marker",
                Author = "Self"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() => new();

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet) => Array.Empty<Issue>();
    }
}
