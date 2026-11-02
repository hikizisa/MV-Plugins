using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapsetVerifier.Parser.Objects;

namespace MapsetVerifier.Plugin.SkinChecks.Model
{
    public static class SkinScanner
    {
        public static List<SkinAssetInfo> GetSkinAssets(BeatmapSet beatmapSet)
        {
            var assets = new List<SkinAssetInfo>();
            if (string.IsNullOrEmpty(beatmapSet.SongPath) || !Directory.Exists(beatmapSet.SongPath))
                return assets;

            try
            {
                var files = Directory.GetFiles(beatmapSet.SongPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        assets.Add(new SkinAssetInfo(file, beatmapSet.SongPath));
                    }
                }
            }
            catch
            {
                // Directory access issue
            }

            return assets;
        }

        public static HashSet<SkinMode> GetPresentModes(BeatmapSet beatmapSet)
        {
            var modes = new HashSet<SkinMode> { SkinMode.General };
            foreach (var bm in beatmapSet.Beatmaps)
            {
                switch (bm.GeneralSettings.mode)
                {
                    case Beatmap.Mode.Standard:
                        modes.Add(SkinMode.Standard);
                        break;
                    case Beatmap.Mode.Taiko:
                        modes.Add(SkinMode.Taiko);
                        break;
                    case Beatmap.Mode.Catch:
                        modes.Add(SkinMode.Catch);
                        break;
                    case Beatmap.Mode.Mania:
                        modes.Add(SkinMode.Mania);
                        break;
                }
            }
            return modes;
        }

        public static bool HasSpinners(BeatmapSet beatmapSet)
        {
            return beatmapSet.Beatmaps.Any(bm => bm.HitObjects.Any(ho => ho.type.HasFlag(HitObject.Types.Spinner)));
        }
    }
}
