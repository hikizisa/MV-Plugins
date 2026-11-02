using System;
using System.Collections.Generic;
using System.Linq;
using MapsetVerifier.Parser.Objects;

namespace MapsetVerifier.Plugin.SkinChecks.Model
{
    public enum SkinMode
    {
        General,
        Standard,
        Taiko,
        Catch,
        Mania
    }

    public class SkinElementRequirement
    {
        public string Pattern { get; }
        public string NormalizedBaseName { get; }
        public bool IsAnimatable { get; }
        public bool IsRequired { get; }
        public string? Group { get; }
        public string? Note { get; }

        public SkinElementRequirement(string pattern, bool isRequired, bool isAnimatable = false, string? group = null, string? note = null)
        {
            Pattern = pattern;
            IsRequired = isRequired;
            IsAnimatable = isAnimatable;
            Group = group;
            Note = note;

            string name = pattern;
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }
            name = name.Replace("{-n}", "").Replace("{n}", "");
            NormalizedBaseName = name.ToLowerInvariant();
        }

        public bool Matches(SkinAssetInfo asset)
        {
            if (asset.RawBaseName.Equals(NormalizedBaseName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsAnimatable)
            {
                return asset.AnimBaseName.Equals(NormalizedBaseName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }

    public class SkinSetDefinition
    {
        public string Name { get; }
        public SkinMode Mode { get; }
        public List<SkinElementRequirement> Elements { get; }
        public Func<BeatmapSet, bool>? IsRelevantPredicate { get; }

        public SkinSetDefinition(string name, SkinMode mode, List<SkinElementRequirement> elements, Func<BeatmapSet, bool>? isRelevantPredicate = null)
        {
            Name = name;
            Mode = mode;
            Elements = elements;
            IsRelevantPredicate = isRelevantPredicate;
        }

        public IEnumerable<SkinElementRequirement> RequiredElements => Elements.Where(e => e.IsRequired && e.Group == null);
        public IEnumerable<SkinElementRequirement> OptionalElements => Elements.Where(e => !e.IsRequired && e.Group == null);
        public ILookup<string, SkinElementRequirement> GroupedElements => Elements.Where(e => e.Group != null).ToLookup(e => e.Group!);
    }
}
