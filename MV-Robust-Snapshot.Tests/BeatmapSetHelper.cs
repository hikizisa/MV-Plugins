using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MapsetVerifier.Parser.Objects;

namespace MV_Robust_Snapshot.Tests
{
    public static class BeatmapSetHelper
    {
        public static BeatmapSet CreateMock(string songPath, Beatmap.Mode mode = Beatmap.Mode.Standard, bool withSpinner = false)
        {
            string osuContent = $"[General]\nMode: {(int)mode}\n\n[Metadata]\nTitle:Test\nArtist:Test\nCreator:Test\nVersion:Test\n\n[TimingPoints]\n0,500,4,1,0,100,1,0\n\n[HitObjects]\n" + (withSpinner ? "256,192,1000,12,0,2000,0:0:0:0:\n" : "100,100,1000,1,0,0:0:0:0:\n");
            File.WriteAllText(Path.Combine(songPath, "test.osu"), osuContent);

            // Let's use constructor or reflection
            var ctor = typeof(BeatmapSet).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(string));
            if (ctor != null)
            {
                return (BeatmapSet)ctor.Invoke(new object[] { songPath });
            }

            // Otherwise default or other constructors
            var anyCtor = typeof(BeatmapSet).GetConstructors().First();
            var parameters = anyCtor.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)songPath : null).ToArray();
            return (BeatmapSet)anyCtor.Invoke(parameters);
        }
    }
}
