using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MapsetVerifier.Plugin.SkinChecks.Model
{
    public class SkinIni
    {
        public double Version { get; set; } = 1.0;
        public string? Name { get; set; }
        public string? Author { get; set; }
        public Dictionary<string, string> GeneralValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ComboColours { get; } = new();

        public static SkinIni Parse(string filePath)
        {
            var ini = new SkinIni();
            if (!File.Exists(filePath))
                return ini;

            string currentSection = "";
            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int colonIndex = line.IndexOf(':');
                if (colonIndex <= 0) continue;

                string key = line.Substring(0, colonIndex).Trim();
                string val = line.Substring(colonIndex + 1).Trim();

                if (currentSection.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    ini.GeneralValues[key] = val;
                    if (key.Equals("Version", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val.Equals("latest", StringComparison.OrdinalIgnoreCase))
                        {
                            ini.Version = 2.5;
                        }
                        else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVer))
                        {
                            ini.Version = parsedVer;
                        }
                    }
                    else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.Name = val;
                    }
                    else if (key.Equals("Author", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.Author = val;
                    }
                }
                else if (currentSection.Equals("Colours", StringComparison.OrdinalIgnoreCase) ||
                         currentSection.Equals("Colors", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.ComboColours.Add(val);
                    }
                }
            }

            return ini;
        }
    }
}
