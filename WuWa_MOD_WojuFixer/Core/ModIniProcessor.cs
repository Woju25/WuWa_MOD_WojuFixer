using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed record FilePlan(string Path, string UpdatedText, int Replacements, int Passes, bool HitMaxPasses);

    public sealed record ProcessSummary(
        int ModIniFound,
        int ModIniChanged,
        int TotalReplacements,
        bool AnyChange,
        int FilesHitMaxPasses);

    public sealed class ModIniProcessor
    {
        private readonly IReadOnlyList<Replacement> _replacements;

        public ModIniProcessor(IReadOnlyList<Replacement> replacements)
        {
            _replacements = replacements ?? throw new ArgumentNullException(nameof(replacements));
        }

        // Pre-scan: if nothing changes, do nothing at all.
        public (ProcessSummary Summary, List<FilePlan> Plans) Preview(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                throw new DirectoryNotFoundException("Selected folder does not exist.");

            var files = Directory.GetFiles(rootFolder, "mod.ini", SearchOption.AllDirectories);

            var plans = new List<FilePlan>();
            int totalReplacements = 0;
            int hitMaxPassesCount = 0;

            foreach (var file in files)
            {
                var original = File.ReadAllText(file, Encoding.UTF8);
                var rr = IniReplacer.ReplaceStepByStepUntilStable(original, _replacements);

                if (rr.HitMaxPasses) hitMaxPassesCount++;

                if (!string.Equals(original, rr.UpdatedText, StringComparison.Ordinal))
                {
                    plans.Add(new FilePlan(
                        Path: file,
                        UpdatedText: rr.UpdatedText,
                        Replacements: rr.TotalReplacements,
                        Passes: rr.Passes,
                        HitMaxPasses: rr.HitMaxPasses));

                    totalReplacements += rr.TotalReplacements;
                }
            }

            var summary = new ProcessSummary(
                ModIniFound: files.Length,
                ModIniChanged: plans.Count,
                TotalReplacements: totalReplacements,
                AnyChange: plans.Count > 0,
                FilesHitMaxPasses: hitMaxPassesCount);

            return (summary, plans);
        }

        public void ExecutePlans(List<FilePlan> plans)
        {
            if (plans is null) throw new ArgumentNullException(nameof(plans));

            foreach (var plan in plans)
            {
                // Backup with timestamp
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = plan.Path + ".bak_" + timestamp;

                File.Copy(plan.Path, backupPath, overwrite: false);
                File.WriteAllText(plan.Path, plan.UpdatedText, Encoding.UTF8);
            }
        }
    }
}
