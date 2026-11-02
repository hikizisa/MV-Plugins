using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MapsetVerifier.Plugin.SkinChecks.Model
{
    public class SkinAssetInfo
    {
        private static readonly Regex AnimRegex = new Regex(@"^(.*?)(?:-|\b)(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string FullPath { get; }
        public string RelativePath { get; }
        public string RawFileName { get; }
        public string RawBaseName { get; }
        public string AnimBaseName { get; }
        public bool IsRetina { get; }
        public int? AnimationIndex { get; }
        public string Extension { get; }

        public SkinAssetInfo(string fullPath, string songPath)
        {
            FullPath = fullPath;
            RelativePath = Path.GetRelativePath(songPath, fullPath).Replace('\\', '/');
            RawFileName = Path.GetFileName(fullPath);
            Extension = Path.GetExtension(fullPath).ToLowerInvariant();

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
            if (nameWithoutExt.EndsWith("@2x", StringComparison.OrdinalIgnoreCase))
            {
                IsRetina = true;
                nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - 3);
            }

            RawBaseName = nameWithoutExt.ToLowerInvariant();

            var match = AnimRegex.Match(nameWithoutExt);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int index))
            {
                AnimationIndex = index;
                AnimBaseName = match.Groups[1].Value.TrimEnd('-').ToLowerInvariant();
            }
            else
            {
                AnimBaseName = RawBaseName;
            }
        }
    }
}
