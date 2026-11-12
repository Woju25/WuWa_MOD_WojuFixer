using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WuWa_MOD_WojuFixer.Core;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed record FilePlan(
        string Path,
        string UpdatedText,
        int Replacements,
        IReadOnlyList<(string OldValue, string NewValue)> HashMappings);

    public sealed record ProcessSummary(
        int IniFilesFound,
        int IniFilesChanged,
        int TotalReplacements,
        bool AnyChange);
    public sealed record IndexMapping(
    int Component,
    string Property,
    string OldValue,
    string NewValue);
    public sealed class ModIniProcessor
    {
        private readonly IReadOnlyList<Replacement> _globalReplacements;
        private readonly Dictionary<string, List<Replacement>> _characterReplacements;

        public ModIniProcessor(
            IReadOnlyList<Replacement> globalReplacements,
            Dictionary<string, List<Replacement>> characterReplacements = null)
        {
            _globalReplacements =
                globalReplacements ?? throw new ArgumentNullException(nameof(globalReplacements));

            _characterReplacements =
                characterReplacements ?? new Dictionary<string, List<Replacement>>();
        }

        /// <summary>
        /// Preview hash replacements for all mod.ini files in the folder.
        /// Uses per-character replacements if character is detected,
        /// otherwise uses global replacements.
        /// </summary>
        public (ProcessSummary Summary, List<FilePlan> Plans) Preview(
            string rootFolder,
            Dictionary<string, string> uniqueHashToCharacter)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) ||
                !Directory.Exists(rootFolder))
            {
                throw new DirectoryNotFoundException(
                    "Selected folder does not exist.");
            }

            var files = Directory.GetFiles(
                    rootFolder,
                    "*.ini",
                    SearchOption.AllDirectories)
                .Where(file =>
                    !Path.GetFileName(file)
                        .Contains("disabled",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var plans = new List<FilePlan>();
            int totalReplacements = 0;

            foreach (var file in files)
            {
                var original = File.ReadAllText(
                    file,
                    Encoding.UTF8);

                // Detect character from the ORIGINAL mod.ini
                var detectedCharacter =
                    CharacterDetector.DetectCharacterFromModIni(
                        original,
                        uniqueHashToCharacter);

                IReadOnlyList<Replacement> applicableReplacements;

                if (!string.IsNullOrWhiteSpace(detectedCharacter) &&
                    _characterReplacements.TryGetValue(
                        detectedCharacter,
                        out var charReplacements))
                {
                    // Character-specific replacements
                    applicableReplacements = charReplacements;
                }
                else
                {
                    // Global replacements
                    applicableReplacements = _globalReplacements;
                }

                var (updated, count, mappings) =
                    ReplaceOnceTopToBottom(
                        original,
                        applicableReplacements);

                if (!string.Equals(
                        original,
                        updated,
                        StringComparison.Ordinal))
                {
                    plans.Add(new FilePlan(
                        Path: file,
                        UpdatedText: updated,
                        Replacements: count,
                        HashMappings: mappings));

                    totalReplacements += count;
                }
            }

            var summary = new ProcessSummary(
                IniFilesFound: files.Length,
                IniFilesChanged: plans.Count,
                TotalReplacements: totalReplacements,
                AnyChange: plans.Count > 0);

            return (summary, plans);
        }

        public List<IndexMapping> ExecutePlans(
            List<FilePlan> plans,
            BackupType backupType)
        {
            var indexMappings = new List<IndexMapping>();

            foreach (var plan in plans)
            {
                var backupPath =
                    BackupManager.CreateBackupPath(
                        plan.Path,
                        backupType);

                BackupManager.CreateBackupAndRegister(
                    plan.Path,
                    backupPath);

                // -------------------------------------------------
                // STEP 1:
                // Write the result of the normal hash replacement.
                // -------------------------------------------------
                File.WriteAllText(
                    plan.Path,
                    plan.UpdatedText);

                // -------------------------------------------------
                // STEP 2:
                // Scan the NEWLY UPDATED .ini.
                //
                // YangyangSP is detected using the NEW hash:
                // efd45ce3
                // -------------------------------------------------
                var fileIndexMappings =
                    ApplyYangyangIndexFix(plan.Path);

                indexMappings.AddRange(fileIndexMappings);
            }

            return indexMappings;
        }
        // =========================================================
        // YANGYANG SP INDEX FIX
        // =========================================================

        private static List<IndexMapping> ApplyYangyangIndexFix(
            string iniPath)
        {
            var mappings = new List<IndexMapping>();

            if (!File.Exists(iniPath))
                return mappings;

            string text = File.ReadAllText(
                iniPath,
                Encoding.UTF8);

            // YangyangSP unique hash after hash replacement.
            const string yangyangHash = "efd45ce3";

            // -----------------------------------------------------
            // Only run if this .ini actually contains YangyangSP.
            // -----------------------------------------------------
            if (!ContainsYangyangHash(text, yangyangHash))
                return mappings;

            // -----------------------------------------------------
            // Component 4
            //
            // 3.5 -> 3.6
            // 82332 -> 83268
            // -----------------------------------------------------
            const string component4Old = "82332";
            const string component4New = "83268";

            if (ComponentContainsValue(
                    text,
                    4,
                    "match_index_count",
                    component4Old))
            {
                text = ReplaceInComponent(
                    text,
                    componentNumber: 4,
                    "match_index_count",
                    component4Old,
                    component4New);

                mappings.Add(
                    new IndexMapping(
                        4,
                        "match_index_count",
                        component4Old,
                        component4New));
            }

            // -----------------------------------------------------
            // Component 5
            //
            // 3.5 -> 3.6
            // 226401 -> 227337
            // -----------------------------------------------------
            const string component5Old = "226401";
            const string component5New = "227337";

            if (ComponentContainsValue(
                    text,
                    5,
                    "match_first_index",
                    component5Old))
            {
                text = ReplaceInComponent(
                    text,
                    componentNumber: 5,
                    "match_first_index",
                    component5Old,
                    component5New);

                mappings.Add(
                    new IndexMapping(
                        5,
                        "match_first_index",
                        component5Old,
                        component5New));
            }

            // -----------------------------------------------------
            // Component 6
            //
            // 3.5 -> 3.6
            // 261141 -> 262077
            // -----------------------------------------------------
            const string component6Old = "261141";
            const string component6New = "262077";

            if (ComponentContainsValue(
                    text,
                    6,
                    "match_first_index",
                    component6Old))
            {
                text = ReplaceInComponent(
                    text,
                    componentNumber: 6,
                    "match_first_index",
                    component6Old,
                    component6New);

                mappings.Add(
                    new IndexMapping(
                        6,
                        "match_first_index",
                        component6Old,
                        component6New));
            }

            // -----------------------------------------------------
            // Write only if something actually changed.
            // -----------------------------------------------------
            if (mappings.Count > 0)
            {
                File.WriteAllText(
                    iniPath,
                    text,
                    Encoding.UTF8);
            }

            return mappings;
        }
        private static bool ContainsYangyangHash(
            string text,
            string hash)
        {
            return text.IndexOf(
                       hash,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static bool ComponentContainsValue(
    string text,
    int componentNumber,
    string propertyName,
    string value)
        {
            string header =
                $"[TextureOverrideComponent{componentNumber}]";

            int componentStart =
                text.IndexOf(
                    header,
                    StringComparison.OrdinalIgnoreCase);

            if (componentStart < 0)
                return false;

            int nextComponent =
                text.IndexOf(
                    "[TextureOverrideComponent",
                    componentStart + header.Length,
                    StringComparison.OrdinalIgnoreCase);

            int sectionEnd =
                nextComponent >= 0
                    ? nextComponent
                    : text.Length;

            string section =
                text.Substring(
                    componentStart,
                    sectionEnd - componentStart);

            string targetLine =
                $"{propertyName} = {value}";

            return section.IndexOf(
                       targetLine,
                       StringComparison.Ordinal) >= 0;
        }
        // =========================================================
        // COMPONENT-SPECIFIC REPLACEMENT
        // =========================================================

        private static string ReplaceInComponent(
            string text,
            int componentNumber,
            string propertyName,
            string oldValue,
            string newValue)
        {
            string header =
                $"[TextureOverrideComponent{componentNumber}]";

            int componentStart =
                text.IndexOf(
                    header,
                    StringComparison.OrdinalIgnoreCase);

            if (componentStart < 0)
                return text;

            int nextComponent =
                text.IndexOf(
                    "[TextureOverrideComponent",
                    componentStart + header.Length,
                    StringComparison.OrdinalIgnoreCase);

            int sectionEnd =
                nextComponent >= 0
                    ? nextComponent
                    : text.Length;

            string section =
                text.Substring(
                    componentStart,
                    sectionEnd - componentStart);

            string oldLine =
                $"{propertyName} = {oldValue}";

            string newLine =
                $"{propertyName} = {newValue}";

            if (section.IndexOf(
                    oldLine,
                    StringComparison.Ordinal) < 0)
            {
                return text;
            }

            section =
                section.Replace(
                    oldLine,
                    newLine,
                    StringComparison.Ordinal);

            return text.Substring(0, componentStart)
                 + section
                 + text.Substring(sectionEnd);
        }

        // =========================================================
        // NORMAL HASH REPLACEMENT
        // =========================================================

        private static (
            string UpdatedText,
            int Replacements,
            List<(string OldValue, string NewValue)> Mappings)
            ReplaceOnceTopToBottom(
                string text,
                IReadOnlyList<Replacement> replacements)
        {
            int total = 0;
            var current = text;

            var mappings =
                new List<(string OldValue, string NewValue)>();

            // Apply each rule once, in order.
            foreach (var r in replacements)
            {
                int count =
                    CountOccurrences(
                        current,
                        r.From);

                if (count <= 0)
                    continue;

                current =
                    current.Replace(
                        r.From,
                        r.To,
                        StringComparison.Ordinal);

                total += count;

                // Record the mapping that actually changed the file.
                if (!string.Equals(
                        r.From,
                        r.To,
                        StringComparison.Ordinal))
                {
                    mappings.Add(
                        (r.From, r.To));
                }
            }

            return (
                current,
                total,
                mappings);
        }

        private static int CountOccurrences(
            string haystack,
            string needle)
        {
            if (string.IsNullOrEmpty(haystack) ||
                string.IsNullOrEmpty(needle))
            {
                return 0;
            }

            int count = 0;
            int index = 0;

            while (true)
            {
                index =
                    haystack.IndexOf(
                        needle,
                        index,
                        StringComparison.Ordinal);

                if (index < 0)
                    break;

                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}