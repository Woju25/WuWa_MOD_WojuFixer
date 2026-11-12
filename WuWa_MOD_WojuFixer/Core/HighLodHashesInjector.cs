using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed class HighLodInjectionSummary
    {
        public int ModIniFound { get; set; }
        public int SupportedFound { get; set; } // count
        public int ModIniChanged { get; set; }
        public int FilesAlreadyHadLodHigh { get; set; }

        public bool AnyChange => ModIniChanged > 0;
    }

    public sealed class HighLodInjectionPlan
    {
        public string FilePath { get; set; } = "";
        public string NewContent { get; set; } = "";
        public bool WouldChange { get; set; }
        public bool AlreadyHadLodHigh { get; set; }
    }

    public sealed class HighLodHashesInjector
    {
        private const string InjectionMarker = "; LOD HIGH -------------------------";

        private readonly HighLodHashesRoot _hashes;

        public HighLodHashesInjector(HighLodHashesRoot hashes)
        {
            _hashes = hashes;
        }

        public (HighLodInjectionSummary summary, List<HighLodInjectionPlan> plans) Preview(string folder)
        {
            var summary = new HighLodInjectionSummary();
            var plans = new List<HighLodInjectionPlan>();

            var files = Directory.EnumerateFiles(folder, "mod.ini", SearchOption.AllDirectories).ToList();
            summary.ModIniFound = files.Count;

            foreach (var file in files)
            {
                var text = File.ReadAllText(file);

                var character = FindMatchingCharacter(text);
                if (character is null)
                {
                    // skip other characters
                    plans.Add(new HighLodInjectionPlan { FilePath = file, NewContent = text, WouldChange = false });
                    continue;
                }

                summary.SupportedFound++;

                var alreadyHad = text.IndexOf(InjectionMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                if (alreadyHad) summary.FilesAlreadyHadLodHigh++;

                var injectedBlock = BuildInjectedLodHighBlock(text, character);
                if (string.IsNullOrWhiteSpace(injectedBlock))
                {
                    plans.Add(new HighLodInjectionPlan { FilePath = file, NewContent = text, WouldChange = false });
                    continue;
                }

                // Remove old LOD HIGH (if present), then append new
                var baseText = RemoveOldLodHighBlock(text);

                var newText = baseText.TrimEnd()
                             + Environment.NewLine + Environment.NewLine
                             + injectedBlock.TrimEnd()
                             + Environment.NewLine;

                var wouldChange = !string.Equals(text, newText, StringComparison.Ordinal);

                plans.Add(new HighLodInjectionPlan
                {
                    FilePath = file,
                    NewContent = newText,
                    WouldChange = wouldChange,
                    AlreadyHadLodHigh = alreadyHad
                });

                if (wouldChange) summary.ModIniChanged++;
            }

            return (summary, plans);
        }

        public void ExecutePlans(List<HighLodInjectionPlan> plans)
        {
            foreach (var plan in plans.Where(p => p.WouldChange))
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = plan.FilePath + ".BAK.woju.LOD." + timestamp;

                BackupManager.CreateBackupAndRegister(plan.FilePath, backupPath);
                File.WriteAllText(plan.FilePath, plan.NewContent);
            }
        }

        private HighLodHashesCharacter? FindMatchingCharacter(string modIniText)
        {
            // detection uses first (unique) hash in detect_any_hashes
            foreach (var c in _hashes.Characters.Where(x => x.Enabled))
            {
                if (c.DetectAnyHashes is null || c.DetectAnyHashes.Count == 0) continue;

                var detect = c.DetectAnyHashes[0];
                if (string.IsNullOrWhiteSpace(detect)) continue;

                if (modIniText.IndexOf($"hash = {detect}", StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            }

            return null;
        }

        // Remove everything from marker to EOF
        private static string RemoveOldLodHighBlock(string modIniText)
        {
            var idx = modIniText.IndexOf(InjectionMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return modIniText;
            return modIniText.Substring(0, idx).TrimEnd();
        }

        private static void CreateBackup(string filePath)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var backupPath = filePath + ".BAK.woju.LOD." + timestamp;

            File.Copy(filePath, backupPath, overwrite: false);
        }

        private static string BuildInjectedLodHighBlock(string originalModIni, HighLodHashesCharacter character)
        {
            var sb = new StringBuilder();
            sb.AppendLine(InjectionMarker);

            foreach (var comp in character.Components.OrderBy(c => c.ComponentIndex))
            {
                sb.AppendLine($"; COMPONENT {comp.ComponentIndex}");
                AppendOverridesFromObject(sb, originalModIni, comp, $"Texture{comp.ComponentIndex}");
                sb.AppendLine();
            }

            // Optional: convene_screen (Lynae)
            if (character.ConveneScreen is not null && character.ConveneScreen.Count > 0)
            {
                sb.AppendLine("; CONVENE SCREEN");

                foreach (var kvp in character.ConveneScreen)
                {
                    var pair = kvp.Value;
                    if (pair is null) continue;
                    if (string.IsNullOrWhiteSpace(pair.Base) || string.IsNullOrWhiteSpace(pair.LodHigh)) continue;

                    AppendOneOverride(
                        sb,
                        originalModIni,
                        sectionName: $"TextureOverride{UpperFirst(kvp.Key)}",
                        lodHighHash: pair.LodHigh,
                        baseHash: pair.Base
                    );
                }
            }

            var result = sb.ToString().TrimEnd();
            if (string.Equals(result, InjectionMarker, StringComparison.OrdinalIgnoreCase))
                return "";

            return result;
        }

        // Auto-generate based on available maps in JSON: reflect all HighLodHashesPair properties on the component
        private static void AppendOverridesFromObject(StringBuilder sb, string originalIni, object obj, string sectionPrefix)
        {
            var props = obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var prop in props)
            {
                if (!typeof(HighLodHashesPair).IsAssignableFrom(Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType))
                    continue;

                var pair = prop.GetValue(obj) as HighLodHashesPair;
                if (pair is null) continue;
                if (string.IsNullOrWhiteSpace(pair.Base) || string.IsNullOrWhiteSpace(pair.LodHigh)) continue;

                var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                var key = jsonName ?? prop.Name;

                // ex: [TextureOverrideTexture3Diffuse]
                var sectionName = $"TextureOverride{sectionPrefix}{UpperFirst(key)}";

                AppendOneOverride(sb, originalIni, sectionName, pair.LodHigh, pair.Base);
            }
        }

        private static void AppendOneOverride(StringBuilder sb, string originalIni, string sectionName, string lodHighHash, string baseHash)
        {
            sb.AppendLine($"[{sectionName}]");
            sb.AppendLine($"hash = {lodHighHash}");
            sb.AppendLine("match_priority = 0");

            var block = ExtractObjectDetectedBlock(originalIni, baseHash);
            if (block is null)
            {
                sb.AppendLine("if $object_detected");
                sb.AppendLine("    ; TODO: base block not found for this hash");
                sb.AppendLine("endif");
            }
            else
            {
                sb.AppendLine(block);
            }

            sb.AppendLine();
        }

        // Preserve full toggle block: copy exact "if $object_detected" ... matching "endif", supports nesting
        private static string? ExtractObjectDetectedBlock(string ini, string baseHash)
        {
            var idx = ini.IndexOf($"hash = {baseHash}", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var afterHash = ini.Substring(idx);
            var ifIdx = afterHash.IndexOf("if $object_detected", StringComparison.OrdinalIgnoreCase);
            if (ifIdx < 0) return null;

            var fromIf = afterHash.Substring(ifIdx);

            var lines = SplitLines(fromIf);

            var depth = 0;
            var collected = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
                    depth++;

                if (depth > 0)
                    collected.Add(line);

                if (trimmed.Equals("endif", StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
            }

            if (collected.Count == 0) return null;
            if (!collected.Last().Trim().Equals("endif", StringComparison.OrdinalIgnoreCase)) return null;

            return string.Join(Environment.NewLine, collected).TrimEnd();
        }

        private static List<string> SplitLines(string s)
        {
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            return s.Split('\n').ToList();
        }

        private static string UpperFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return s.ToUpperInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}