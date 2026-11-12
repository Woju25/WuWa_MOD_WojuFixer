using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WuWa_MOD_WojuFixer.Core
{
    public enum HighLodCharacter
    {
        Unknown = 0,
        Aemeath,
        Lynae,
        Mornye,
        Luuk
    }

    public sealed record HighLodPlan(string Path, string UpdatedText, HighLodCharacter Character);

    public sealed record HighLodSummary(
        int ModIniFound,
        int SupportedFoundCount,
        int SupportedFound, // 0/1 for convenience in logs
        int ModIniChanged,
        bool AnyChange);

    public sealed class HighLodFixer
    {
        private static readonly string[] AemeathGroups = ["Aemeath31"];
        private static readonly string[] LynaeGroups = ["Lynae31"];
        private static readonly string[] MornyeGroups = ["Mornye31"];
        private static readonly string[] LuukGroups = ["Luuk31"];

        private readonly Dictionary<HighLodCharacter, HashSet<string>> _characterHashes;

        public HighLodFixer()
        {
            var hashesPath = Path.Combine(AppContext.BaseDirectory, "hashes.json");
            var replacements = HashMappingLoader.LoadReplacementsFromJson(hashesPath);


            _characterHashes = new Dictionary<HighLodCharacter, HashSet<string>>
            {
                [HighLodCharacter.Aemeath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                [HighLodCharacter.Lynae] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                [HighLodCharacter.Mornye] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                [HighLodCharacter.Luuk] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
        }

        public (HighLodSummary Summary, List<HighLodPlan> Plans) Preview(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                throw new DirectoryNotFoundException("Selected folder does not exist.");

            var files = Directory.GetFiles(rootFolder, "mod.ini", SearchOption.AllDirectories);

            var plans = new List<HighLodPlan>();

            int supportedFoundCount = 0;
            int changed = 0;

            foreach (var file in files)
            {
                var original = File.ReadAllText(file, Encoding.UTF8);

                var character = DetectCharacterFromIni(original);
                if (character == HighLodCharacter.Unknown)
                    continue;

                supportedFoundCount++;

                var updated = ApplyHighLodFix(original, character);

                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    changed++;
                    plans.Add(new HighLodPlan(file, updated, character));
                }
            }

            var summary = new HighLodSummary(
                ModIniFound: files.Length,
                SupportedFoundCount: supportedFoundCount,
                SupportedFound: supportedFoundCount > 0 ? 1 : 0,
                ModIniChanged: changed,
                AnyChange: changed > 0);

            return (summary, plans);
        }

        public void ExecutePlans(List<HighLodPlan> plans)
        {
            if (plans is null) throw new ArgumentNullException(nameof(plans));

            foreach (var plan in plans)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = plan.Path + ".LOD." + timestamp;

                File.Copy(plan.Path, backupPath, overwrite: false);
                File.WriteAllText(plan.Path, plan.UpdatedText, Encoding.UTF8);
            }
        }

        private static string ApplyHighLodFix(string originalIniText, HighLodCharacter character)
        {
            
            return originalIniText;
        }

        private static HighLodCharacter DetectCharacterFromIni(string iniText)
        {
            // Detection approach:
            var hashes = ExtractAllHashEquals8(iniText);

            if (hashes.Contains("513eace9", StringComparer.OrdinalIgnoreCase)) return HighLodCharacter.Lynae;
            if (hashes.Contains("350db74d", StringComparer.OrdinalIgnoreCase)) return HighLodCharacter.Mornye;
            if (hashes.Contains("3bbfa5a0", StringComparer.OrdinalIgnoreCase)) return HighLodCharacter.Luuk;
            if (hashes.Contains("2738a0f0", StringComparer.OrdinalIgnoreCase)) return HighLodCharacter.Aemeath;

            // FIX: ensure all code paths return a value
            return HighLodCharacter.Unknown;
        }

        private static List<string> ExtractAllHashEquals8(string iniText)
        {
            var lines = iniText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var list = new List<string>();

            foreach (var line in lines)
            {
                var t = line.Trim();
                if (!t.StartsWith("hash = ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var h = t.Substring("hash = ".Length).Trim();
                if (h.Length == 8)
                    list.Add(h);
            }

            return list;
        }

        private static string? ExtractHash8(string hashEqualsLine)
        {
            if (hashEqualsLine is null) return null;
            var t = hashEqualsLine.Trim();
            if (!t.StartsWith("hash = ", StringComparison.OrdinalIgnoreCase)) return null;

            var h = t.Substring("hash = ".Length).Trim();
            return h.Length == 8 ? h : null;
        }
    }
}