using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Framework.Objects.Attributes;
using MapsetVerifier.Framework.Objects.Metadata;
using MapsetVerifier.Parser.Objects;
using Serilog;

namespace MapsetVerifier.Plugin.CustomSnapshots
{
    [Check]
    public class CustomSnapshotPlugin : GeneralCheck
    {
        private static bool Hooked = false;

        public CustomSnapshotPlugin()
        {
            InitializeHook();
        }

        public static bool IsHooked => Hooked;

        public static void InitializeHook()
        {
            if (Hooked) return;

            try
            {
                Log.Information("[CustomSnapshotsPlugin] Initializing robust snapshot differ hook...");

                // Detect host snapshot engine
                // MapsetVerifier 2.2+: MapsetVerifier.Snapshots.Diffing.BeatmapDiffer
                var beatmapDifferType = typeof(MapsetVerifier.Snapshots.Diffing.BeatmapDiffer);

                if (beatmapDifferType != null)
                {
                    var targetMethod = beatmapDifferType.GetMethod("Diff", BindingFlags.Public | BindingFlags.Static);
                    var hookMethod = typeof(RobustBeatmapDiffer).GetMethod("Diff", BindingFlags.Public | BindingFlags.Static);

                    if (targetMethod != null && hookMethod != null)
                    {
                        if (DetourHelper.TryDetour(targetMethod, hookMethod))
                        {
                            Log.Information("[CustomSnapshotsPlugin] Hooked BeatmapDiffer.Diff successfully. Robust RANSAC/DTW alignment active.");
                            Hooked = true;
                            return;
                        }
                        else
                        {
                            Log.Error("[CustomSnapshotsPlugin] Failed to detour BeatmapDiffer.Diff.");
                        }
                    }
                    else
                    {
                        Log.Error("[CustomSnapshotsPlugin] Could not find Diff method on BeatmapDiffer or RobustBeatmapDiffer.");
                    }
                }
                else
                {
                    Log.Warning("[CustomSnapshotsPlugin] BeatmapDiffer not found in MapsetVerifier.Snapshots.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CustomSnapshotsPlugin] Exception while initializing snapshot hook.");
            }
        }

        public override CheckMetadata GetMetadata() =>
            new CheckMetadata
            {
                Category = "Snapshots",
                Message = "Custom Robust Snapshots Plugin",
                Author = "Self"
            };

        public override Dictionary<string, IssueTemplate> GetTemplates() => new Dictionary<string, IssueTemplate>();

        public override IEnumerable<Issue> GetIssues(BeatmapSet beatmapSet) => [];
    }
}
