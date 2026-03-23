using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed record StableInjectPlan(string Path, string UpdatedText, bool AlreadyApplied, int ComponentsApplied);

    public sealed record StableInjectSummary(
        int ModIniFound,
        int ModIniChanged,
        int TotalComponentsApplied,
        int FilesSkippedAlreadyApplied,
        bool AnyChange);

    public sealed class StableTexturesInjector
    {
        // Markers used to detect StableTextures already present
        private static readonly string[] AlreadyAppliedMarkers =
        [
            "Resource\\RabbitFX\\Diffuse",
            "Resource\\RabbitFX\\Lightmap",
            "Resource\\RabbitFX\\Normalmap",
            "run = Commandlist\\RabbitFX\\SetTextures",
            "run = CommandList\\RabbitFX\\SetTextures"
        ];

        // Inject ONLY after this line, inside the draw override blocks.
        private const string AnchorOverrideSharedResources = "run = CommandListOverrideSharedResources";

        private const string IfObjectDetected = "if $object_detected";
        private const string EndIf = "endif";

        // Needed to actually apply the selected RabbitFX textures
        private const string RunRabbitFxSetTextures = @"run = Commandlist\RabbitFX\SetTextures";

        // Aero eyes globals and Present-hook snippet (exactly like your desired output)
        private const string AeroEyesConst1 = "global $aeroeyes = 0";
        private const string AeroEyesConst2 = "global $aeroeyesCheck = 0";

        private static readonly string[] PresentAeroEyesBlock =
        [
            "if $aeroeyes != $aeroeyesCheck",
            "  $aeroeyes = $aeroeyesCheck",
            "endif",
            "$aeroeyesCheck = 0"
        ];

        // RoverM / RoverF aero-eyes diffuse hashes (from your examples)
        private const string RoverMEyesDiffuseNormal = "6913dea1";
        private const string RoverFEyesDiffuseNormal = "52c18227";
        private const string RoverEyesDiffuseAero = "29304593";

        // AeroEyes trigger block (inserted under Shading: Draw Call Stacks Processing)
        private static readonly string[] AeroEyesTriggerBlock =
        [
            "[TextureOverrideAeroEyes]",
            "hash = 29304593",
            "match_priority=0",
            "if $mod_enabled",
            "\t$aeroeyesCheck = 1",
            "endif",
            ""
        ];

        private const string ShadingDrawCallStacksHeader =
            "; Shading: Draw Call Stacks Processing -------------------------";

        private readonly StableHashesRoot _stable;

        public StableTexturesInjector(StableHashesRoot stable)
        {
            _stable = stable ?? throw new ArgumentNullException(nameof(stable));
        }

        public (StableInjectSummary Summary, List<StableInjectPlan> Plans) Preview(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                throw new DirectoryNotFoundException("Selected folder does not exist.");

            var files = Directory.GetFiles(rootFolder, "mod.ini", SearchOption.AllDirectories);

            var plans = new List<StableInjectPlan>();
            int changed = 0;
            int totalApplied = 0;
            int skippedAlreadyApplied = 0;

            foreach (var file in files)
            {
                var original = File.ReadAllText(file, Encoding.UTF8);

                bool alreadyApplied = ContainsAny(original, AlreadyAppliedMarkers);
                if (alreadyApplied)
                {
                    skippedAlreadyApplied++;
                    continue;
                }

                var (updated, applied) = InjectIntoIniText(original);

                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    changed++;
                    totalApplied += applied;
                    plans.Add(new StableInjectPlan(file, updated, AlreadyApplied: false, ComponentsApplied: applied));
                }
            }

            var summary = new StableInjectSummary(
                ModIniFound: files.Length,
                ModIniChanged: changed,
                TotalComponentsApplied: totalApplied,
                FilesSkippedAlreadyApplied: skippedAlreadyApplied,
                AnyChange: changed > 0);

            return (summary, plans);
        }

        public void ExecutePlans(List<StableInjectPlan> plans)
        {
            if (plans is null) throw new ArgumentNullException(nameof(plans));

            foreach (var plan in plans)
            {
                // Backup naming requirement:
                // - StableTextures injection done => mod.ini.STABLE.datetime
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = plan.Path + ".STABLE." + timestamp;

                File.Copy(plan.Path, backupPath, overwrite: false);
                File.WriteAllText(plan.Path, plan.UpdatedText, Encoding.UTF8);
            }
        }

        private (string UpdatedText, int ComponentsApplied) InjectIntoIniText(string original)
        {
            var lines = SplitLines(original);

            // Build hash -> ResourceTextureN mapping based on TextureOverrideTextureN blocks
            var hashToResource = BuildHashToResourceTextureMapFromTextureOverrideHash(lines);

            // Detect rover aero-eyes by presence of their diffuse hashes in the override-hash map.
            bool isRoverMWithAeroEyes = hashToResource.ContainsKey(RoverMEyesDiffuseNormal) && hashToResource.ContainsKey(RoverEyesDiffuseAero);
            bool isRoverFWithAeroEyes = hashToResource.ContainsKey(RoverFEyesDiffuseNormal) && hashToResource.ContainsKey(RoverEyesDiffuseAero);
            bool needsAeroEyesSystem = isRoverMWithAeroEyes || isRoverFWithAeroEyes;

            if (needsAeroEyesSystem)
            {
                lines = InjectAeroEyesIntoConstants(lines);
                lines = InjectAeroEyesPresentHook(lines);
                lines = InjectAeroEyesTriggerBlock(lines);
            }

            // Detect present character hashes in the ini (from any "hash = XXXXXXXX" lines)
            var characterHashesPresent = DetectPresentHashesFromHashEquals(lines);

            // Inject AFTER every "run = CommandListOverrideSharedResources"
            int componentsApplied = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                if (!LineContains(lines[i], AnchorOverrideSharedResources))
                    continue;

                // Prevent re-injecting if we've already injected RabbitFX lines right after this anchor
                if (i + 1 < lines.Count && LineContains(lines[i + 1], "Resource\\RabbitFX\\"))
                    continue;

                int? componentIndex = FindNearestComponentIndexUpwards(lines, i);
                if (componentIndex is null)
                    continue;

                var stableCharacter = _stable.Characters
                    .FirstOrDefault(c => c.Enabled && characterHashesPresent.Contains(c.CharacterHash, StringComparer.OrdinalIgnoreCase));

                if (stableCharacter is null)
                    continue;

                var compList = stableCharacter.Components.Where(c => c.ComponentIndex == componentIndex.Value).ToList();
                if (compList.Count == 0)
                    continue;

                var injectedLinesForThisAnchor = new List<string>();

                // Only Rover eyes component uses conditional toggle injection.
                // RoverM eyes componentIndex = 4, RoverF eyes componentIndex = 5 (based on your examples).
                bool isRoverMEyesComponent = stableCharacter.CharacterHash.Equals("8b386efa", StringComparison.OrdinalIgnoreCase) && componentIndex.Value == 4;
                bool isRoverFEyesComponent = stableCharacter.CharacterHash.Equals("4d3d06a6", StringComparison.OrdinalIgnoreCase) && componentIndex.Value == 5;

                if (needsAeroEyesSystem && (isRoverMEyesComponent || isRoverFEyesComponent) && compList.Count >= 2)
                {
                    string normalDiffuseHash = isRoverFEyesComponent ? RoverFEyesDiffuseNormal : RoverMEyesDiffuseNormal;

                    var normal = compList.FirstOrDefault(c => string.Equals(c.DiffuseHash, normalDiffuseHash, StringComparison.OrdinalIgnoreCase));
                    var aero = compList.FirstOrDefault(c => string.Equals(c.DiffuseHash, RoverEyesDiffuseAero, StringComparison.OrdinalIgnoreCase));

                    normal ??= compList[0];
                    aero ??= compList.Count > 1 ? compList[1] : compList[0];

                    var normalDiffuse = ResolveRefTarget(hashToResource, normal.DiffuseHash);
                    var aeroDiffuse = ResolveRefTarget(hashToResource, aero.DiffuseHash);

                    var normalLightmap = ResolveRefTarget(hashToResource, normal.LightmapHash);
                    var aeroLightmap = ResolveRefTarget(hashToResource, aero.LightmapHash);

                    if (normalDiffuse != null && aeroDiffuse != null)
                    {
                        injectedLinesForThisAnchor.Add(IfObjectDetected);
                        injectedLinesForThisAnchor.Add("if $aeroeyes == 0");
                        injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Diffuse = ref {normalDiffuse}");
                        injectedLinesForThisAnchor.Add("else");
                        injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Diffuse = ref {aeroDiffuse}");
                        injectedLinesForThisAnchor.Add(EndIf);
                        injectedLinesForThisAnchor.Add(EndIf);

                        if (normalLightmap != null && aeroLightmap != null)
                        {
                            injectedLinesForThisAnchor.Add(IfObjectDetected);
                            injectedLinesForThisAnchor.Add("if $aeroeyes == 0");
                            injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Lightmap = ref {normalLightmap}");
                            injectedLinesForThisAnchor.Add("else");
                            injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Lightmap = ref {aeroLightmap}");
                            injectedLinesForThisAnchor.Add(EndIf);
                            injectedLinesForThisAnchor.Add(EndIf);
                        }

                        // NEW: must run RabbitFX SetTextures after setting refs
                        injectedLinesForThisAnchor.Add(RunRabbitFxSetTextures);

                        componentsApplied++;
                    }
                }
                else
                {
                    // Standard injection
                    var comp = compList[0];

                    var diffuse = ResolveRefTarget(hashToResource, comp.DiffuseHash);
                    if (diffuse == null)
                        continue; // can't do anything for this component without diffuse

                    injectedLinesForThisAnchor.Add(IfObjectDetected);
                    injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Diffuse = ref {diffuse}");
                    injectedLinesForThisAnchor.Add(EndIf);

                    var lightmap = ResolveRefTarget(hashToResource, comp.LightmapHash);
                    if (lightmap != null)
                    {
                        injectedLinesForThisAnchor.Add(IfObjectDetected);
                        injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Lightmap = ref {lightmap}");
                        injectedLinesForThisAnchor.Add(EndIf);
                    }

                    var normalmap = ResolveRefTarget(hashToResource, comp.NormalmapHash);
                    if (normalmap != null)
                    {
                        injectedLinesForThisAnchor.Add(IfObjectDetected);
                        injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Normalmap = ref {normalmap}");
                        injectedLinesForThisAnchor.Add(EndIf);
                    }

                    // NEW: must run RabbitFX SetTextures after setting refs
                    injectedLinesForThisAnchor.Add(RunRabbitFxSetTextures);

                    componentsApplied++;
                }

                if (injectedLinesForThisAnchor.Count == 0)
                    continue;

                lines.InsertRange(i + 1, injectedLinesForThisAnchor);
                i += injectedLinesForThisAnchor.Count;
            }

            if (componentsApplied == 0 && !needsAeroEyesSystem)
                return (original, 0);

            return (JoinLines(lines), componentsApplied);
        }

        private static List<string> InjectAeroEyesTriggerBlock(List<string> lines)
        {
            // Avoid double-inject
            if (lines.Any(l => l.Trim().Equals("[TextureOverrideAeroEyes]", StringComparison.OrdinalIgnoreCase)))
                return lines;

            // Find the header comment and insert immediately after it
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IndexOf(ShadingDrawCallStacksHeader, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lines.InsertRange(i + 1, AeroEyesTriggerBlock);
                    return lines;
                }
            }

            return lines;
        }

        private static List<string> InjectAeroEyesIntoConstants(List<string> lines)
        {
            int constantsStart = FindSectionStart(lines, "Constants");
            if (constantsStart < 0)
                return lines;

            int constantsEnd = FindSectionEnd(lines, constantsStart);

            bool hasAeroEyes = lines.Skip(constantsStart).Take(constantsEnd - constantsStart).Any(l => LineContains(l, AeroEyesConst1));
            bool hasAeroEyesCheck = lines.Skip(constantsStart).Take(constantsEnd - constantsStart).Any(l => LineContains(l, AeroEyesConst2));

            if (hasAeroEyes && hasAeroEyesCheck)
                return lines;

            var insertLines = new List<string>();
            if (!hasAeroEyes) insertLines.Add(AeroEyesConst1);
            if (!hasAeroEyesCheck) insertLines.Add(AeroEyesConst2);

            lines.InsertRange(constantsEnd, insertLines);
            return lines;
        }

        private static List<string> InjectAeroEyesPresentHook(List<string> lines)
        {
            int presentStart = FindSectionStart(lines, "Present");
            if (presentStart < 0)
                return lines;

            int presentEnd = FindSectionEnd(lines, presentStart);

            bool alreadyHasHook = lines.Skip(presentStart).Take(presentEnd - presentStart)
                .Any(l => LineContains(l, "$aeroeyesCheck = 0"));

            if (alreadyHasHook)
                return lines;

            lines.InsertRange(presentEnd, PresentAeroEyesBlock);
            return lines;
        }

        private static Dictionary<string, string> BuildHashToResourceTextureMapFromTextureOverrideHash(List<string> lines)
        {
            // Parse:
            // [TextureOverrideTextureN]
            // hash = XXXXXXXX
            // ...
            // (optional) this = ResourceTextureK
            //
            // We map XXXXXXXX -> ResourceTextureK if "this =" exists, otherwise -> ResourceTextureN.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (!IsSectionHeader(line, out var sectionName))
                    continue;

                if (!sectionName.StartsWith("TextureOverrideTexture", StringComparison.OrdinalIgnoreCase))
                    continue;

                var suffix = sectionName.Substring("TextureOverrideTexture".Length); // N
                var inferredResName = "ResourceTexture" + suffix;

                int end = FindSectionEnd(lines, i);

                string? foundHash = null;
                string? foundThis = null;

                for (int j = i + 1; j < end; j++)
                {
                    var t = lines[j].Trim();

                    if (foundHash == null && t.StartsWith("hash = ", StringComparison.OrdinalIgnoreCase))
                    {
                        foundHash = t.Substring("hash = ".Length).Trim();
                        continue;
                    }

                    // Accept "this =" anywhere in the section (even inside if blocks)
                    if (foundThis == null && t.StartsWith("this = ", StringComparison.OrdinalIgnoreCase))
                    {
                        foundThis = t.Substring("this = ".Length).Trim();
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(foundHash))
                {
                    map[foundHash!] = !string.IsNullOrWhiteSpace(foundThis) ? foundThis! : inferredResName;
                }

                i = end - 1;
            }

            return map;
        }

        private static HashSet<string> DetectPresentHashesFromHashEquals(List<string> lines)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in lines)
            {
                var t = ch.Trim();
                if (t.StartsWith("hash = ", StringComparison.OrdinalIgnoreCase))
                {
                    var hash = t.Substring("hash = ".Length).Trim();
                    if (hash.Length == 8)
                        set.Add(hash);
                }
            }

            return set;
        }

        private static string? ResolveRefTarget(Dictionary<string, string> hashToResourceTexture, string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            if (hashToResourceTexture.TryGetValue(hash.Trim(), out var res))
                return res;

            return null;
        }

        private static int? FindNearestComponentIndexUpwards(List<string> lines, int startIndexInclusive)
        {
            for (int i = startIndexInclusive; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (!IsSectionHeader(line, out var sectionName))
                    continue;

                if (!sectionName.StartsWith("TextureOverrideComponent", StringComparison.OrdinalIgnoreCase))
                    continue;

                var suffix = sectionName.Substring("TextureOverrideComponent".Length);
                if (int.TryParse(suffix, out var idx))
                    return idx;

                return null;
            }

            return null;
        }

        private static int FindSectionStart(List<string> lines, string sectionName)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (IsSectionHeader(t, out var name) && string.Equals(name, sectionName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static int FindSectionEnd(List<string> lines, int sectionHeaderIndex)
        {
            for (int i = sectionHeaderIndex + 1; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    return i;
            }
            return lines.Count;
        }

        private static bool IsSectionHeader(string trimmedLine, out string sectionName)
        {
            sectionName = "";
            if (trimmedLine.Length < 3) return false;
            if (!trimmedLine.StartsWith("[", StringComparison.Ordinal) || !trimmedLine.EndsWith("]", StringComparison.Ordinal))
                return false;

            sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
            return sectionName.Length > 0;
        }

        private static bool LineContains(string line, string needle)
            => line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool ContainsAny(string text, string[] needles)
        {
            foreach (var n in needles)
            {
                if (text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static List<string> SplitLines(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return normalized.Split('\n').ToList();
        }

        private static string JoinLines(List<string> lines)
        {
            return string.Join(Environment.NewLine, lines);
        }
    }
}