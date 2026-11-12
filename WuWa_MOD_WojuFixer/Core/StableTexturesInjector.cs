using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WuWa_MOD_WojuFixer.Models;

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
        private const string Form2Const1 =
    "global $form2 = 0";

        private const string Form2Const2 =
            "global $form2Check = 0";

        private static readonly string[] PresentForm2Block =
        [
            "if $form2 != $form2Check",
    "  $form2 = $form2Check",
    "endif",
    "$form2Check = 0"
        ];
        // Aero eyes globals and Present-hook snippet
        private const string AeroEyesConst1 = "global $aeroeyes = 0";
        private const string AeroEyesConst2 = "global $aeroeyesCheck = 0";

        private static readonly string[] PresentAeroEyesBlock =
        [
            "if $aeroeyes != $aeroeyesCheck",
            "  $aeroeyes = $aeroeyesCheck",
            "endif",
            "$aeroeyesCheck = 0"
        ];

        // RoverM / RoverF normal eyes diffuse hashes (from your examples)
        private const string RoverMEyesDiffuseNormal = "6913dea1";
        private const string RoverFEyesDiffuseNormal = "52c18227";

        // UPDATED: Rover aero-eyes diffuse hashes
        private const string RoverMEyesDiffuseAero = "85700b55";
        private const string RoverFEyesDiffuseAero = "f46fdbb4";

        // UPDATED: AeroEyes trigger block
        private static readonly string[] AeroEyesTriggerBlock =
        [
            "[TextureOverrideAeroEyes_RoverM]",
            "hash = 85700b55",
            "match_priority=0",
            "if $mod_enabled",
            "\t$aeroeyesCheck = 1",
            "endif",
            "",
            "[TextureOverrideAeroEyes_RoverF]",
            "hash = f46fdbb4",
            "match_priority=0",
            "if $mod_enabled",
            "\t$aeroeyesCheck = 1",
            "endif",
            ""
        ];
        private static string[] BuildForm2TriggerBlock(string triggerHash)
        {
            return
            [
                "[ShaderOverrideForm2]",
        $"hash = {triggerHash}",
        "match_priority=0",
        "if $mod_enabled",
        "\t$form2Check = 1",
        "endif",
        ""
            ];
        }
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
            foreach (var plan in plans)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = plan.Path + ".BAK.woju.STABLE." + timestamp;

                BackupManager.CreateBackupAndRegister(plan.Path, backupPath);
                File.WriteAllText(plan.Path, plan.UpdatedText, Encoding.UTF8);
            }
        }

        private (string UpdatedText, int ComponentsApplied) InjectIntoIniText(string original)
        {
            var lines = SplitLines(original);

            // Build hash -> ResourceTextureN mapping based on TextureOverrideTextureN blocks
            var hashToResource = BuildHashToResourceTextureMapFromTextureOverrideHash(lines);

            // Detect rover aero-eyes by presence of their diffuse hashes in the override-hash map.
            bool isRoverMWithAeroEyes =
                hashToResource.ContainsKey(RoverMEyesDiffuseNormal) && hashToResource.ContainsKey(RoverMEyesDiffuseAero);

            bool isRoverFWithAeroEyes =
                hashToResource.ContainsKey(RoverFEyesDiffuseNormal) && hashToResource.ContainsKey(RoverFEyesDiffuseAero);

            bool needsAeroEyesSystem = isRoverMWithAeroEyes || isRoverFWithAeroEyes;

            if (needsAeroEyesSystem)
            {
                lines = InjectAeroEyesIntoConstants(lines);
                lines = InjectAeroEyesPresentHook(lines);
                lines = InjectAeroEyesTriggerBlock(lines);
            }
            // Detect present character hashes in the ini
            var characterHashesPresent = DetectPresentHashesFromHashEquals(lines);
            var stableCharacter = _stable.Characters
                .FirstOrDefault(c =>
                    c.Enabled &&
                    characterHashesPresent.Contains(
                        c.CharacterHash,
                        StringComparer.OrdinalIgnoreCase));

            bool needsForm2System =
                stableCharacter != null &&
                !string.IsNullOrWhiteSpace(
                    stableCharacter.FormTriggerHash);

            if (needsForm2System)
            {
                lines = InjectForm2IntoConstants(lines);
                lines = InjectForm2PresentHook(lines);

                lines = InjectForm2TriggerBlock(
                    lines,
                    stableCharacter!.FormTriggerHash!);
            }

            // Inject AFTER every "run = CommandListOverrideSharedResources"
            int componentsApplied = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                if (!LineContains(lines[i], AnchorOverrideSharedResources))
                    continue;

                // Prevent re-injecting if already injected RabbitFX lines right after this anchor
                if (i + 1 < lines.Count && LineContains(lines[i + 1], "Resource\\RabbitFX\\"))
                    continue;

                int? componentIndex = FindNearestComponentIndexUpwards(lines, i);
                if (componentIndex is null)
                    continue;


                if (stableCharacter is null)
                    continue;

                var compList = stableCharacter.Components.Where(c => c.ComponentIndex == componentIndex.Value).ToList();
                if (compList.Count == 0)
                    continue;

                var injectedLinesForThisAnchor = new List<string>();

                // Only Rover eyes component uses conditional toggle injection.
                // RoverM eyes componentIndex = 4, RoverF eyes componentIndex = 5
                bool isRoverMEyesComponent =
                    stableCharacter.CharacterHash.Equals("8b386efa", StringComparison.OrdinalIgnoreCase) && componentIndex.Value == 4;

                bool isRoverFEyesComponent =
                    stableCharacter.CharacterHash.Equals("4d3d06a6", StringComparison.OrdinalIgnoreCase) && componentIndex.Value == 5;

                if (needsAeroEyesSystem && (isRoverMEyesComponent || isRoverFEyesComponent) && compList.Count >= 2)
                {
                    string normalDiffuseHash = isRoverFEyesComponent ? RoverFEyesDiffuseNormal : RoverMEyesDiffuseNormal;
                    string aeroDiffuseHash = isRoverFEyesComponent ? RoverFEyesDiffuseAero : RoverMEyesDiffuseAero;

                    var normal = compList.FirstOrDefault(c => string.Equals(c.DiffuseHash, normalDiffuseHash, StringComparison.OrdinalIgnoreCase));
                    var aero = compList.FirstOrDefault(c => string.Equals(c.DiffuseHash, aeroDiffuseHash, StringComparison.OrdinalIgnoreCase));

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

                        // must run RabbitFX SetTextures after setting refs
                        injectedLinesForThisAnchor.Add(RunRabbitFxSetTextures);

                        componentsApplied++;
                    }
                }
                else
                {
                    // Standard injection - extract and transform the if $object_detected block
                    var form1Comp = compList.FirstOrDefault(c =>
                        string.Equals(c.State, "form1",
                            StringComparison.OrdinalIgnoreCase));

                    var form2Comp = compList.FirstOrDefault(c =>
                        string.Equals(c.State, "form2",
                            StringComparison.OrdinalIgnoreCase));

                    bool hasFormStates =
                        form1Comp != null &&
                        form2Comp != null;

                    var comp = hasFormStates
                        ? form1Comp!
                        : compList[0];
                    if (hasFormStates)
                    {
                        var form1Diffuse =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp!.DiffuseHash);

                        var form2Diffuse =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp!.DiffuseHash);

                        var form1DiffuseBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form1Comp.DiffuseHash);

                        var form2DiffuseBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form2Comp.DiffuseHash);

                        var form1Lightmap =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp.LightmapHash);

                        var form2Lightmap =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp.LightmapHash);

                        var form1LightmapBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form1Comp.LightmapHash);

                        var form2LightmapBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form2Comp.LightmapHash);

                        var form1Normalmap =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp.NormalmapHash);

                        var form2Normalmap =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp.NormalmapHash);

                        var form1NormalmapBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form1Comp.NormalmapHash);

                        var form2NormalmapBlock =
                            ExtractObjectDetectedBlock(
                                original,
                                form2Comp.NormalmapHash);

                        var form1Materialmap =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp.MaterialmapHash);

                        var form2Materialmap =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp.MaterialmapHash);

                        var form1Cutoutmap =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp.CutoutmapHash);

                        var form2Cutoutmap =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp.CutoutmapHash);

                        var form1Specialmap =
                            ResolveRefTarget(
                                hashToResource,
                                form1Comp.SpecialmapHash);

                        var form2Specialmap =
                            ResolveRefTarget(
                                hashToResource,
                                form2Comp.SpecialmapHash);

                        if (form1Diffuse != null &&
                            form2Diffuse != null)
                        {
                            injectedLinesForThisAnchor.Add(IfObjectDetected);

                            injectedLinesForThisAnchor.Add("if $form2 == 1");
                            if (form2DiffuseBlock != null)
                            {
                                var transformedForm2 =
                                    TransformThisToRabbitFxDirective(
                                        form2DiffuseBlock,
                                        form2Comp.DiffuseHash,
                                        "Diffuse",
                                        form2Diffuse);

                                injectedLinesForThisAnchor.AddRange(
                                    transformedForm2.Skip(1).Take(transformedForm2.Count - 2));
                            }
                            else
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Diffuse = ref {form2Diffuse}");
                            }


                            if (form2Lightmap != null)
                            {
                                if (form2LightmapBlock != null)
                                {
                                    var transformedForm2 =
                                        TransformThisToRabbitFxDirective(
                                            form2LightmapBlock,
                                            form2Comp.LightmapHash,
                                            "Lightmap",
                                            form2Lightmap);

                                    injectedLinesForThisAnchor.AddRange(
                                        transformedForm2.Skip(1).Take(transformedForm2.Count - 2));
                                }
                                else
                                {
                                    injectedLinesForThisAnchor.Add(
                                        $@"Resource\RabbitFX\Lightmap = ref {form2Lightmap}");
                                }
                            }

                            if (form2Normalmap != null)
                            {
                                if (form2NormalmapBlock != null)
                                {
                                    var transformedForm2 =
                                        TransformThisToRabbitFxDirective(
                                            form2NormalmapBlock,
                                            form2Comp.NormalmapHash,
                                            "Normalmap",
                                            form2Normalmap);

                                    injectedLinesForThisAnchor.AddRange(
                                        transformedForm2.Skip(1).Take(transformedForm2.Count - 2));
                                }
                                else
                                {
                                    injectedLinesForThisAnchor.Add(
                                        $@"Resource\RabbitFX\Normalmap = ref {form2Normalmap}");
                                }
                            }

                            // Handle Materialmap / Cutoutmap / Specialmap for Form 2
                            if (form2Materialmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Materialmap = ref {form2Materialmap}");
                            }

                            if (form2Cutoutmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Cutoutmap = ref {form2Cutoutmap}");
                            }

                            if (form2Specialmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Specialmap = ref {form2Specialmap}");
                            }

                            injectedLinesForThisAnchor.Add("else");
                            if (form1DiffuseBlock != null)
                            {
                                var transformedForm1 =
                                    TransformThisToRabbitFxDirective(
                                        form1DiffuseBlock,
                                        form1Comp.DiffuseHash,
                                        "Diffuse",
                                        form1Diffuse);

                                injectedLinesForThisAnchor.AddRange(
                                    transformedForm1.Skip(1).Take(transformedForm1.Count - 2));
                            }
                            else
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Diffuse = ref {form1Diffuse}");
                            }

                            if (form1Lightmap != null)
                            {
                                if (form1LightmapBlock != null)
                                {
                                    var transformedForm1 =
                                        TransformThisToRabbitFxDirective(
                                            form1LightmapBlock,
                                            form1Comp.LightmapHash,
                                            "Lightmap",
                                            form1Lightmap);

                                    injectedLinesForThisAnchor.AddRange(
                                        transformedForm1.Skip(1).Take(transformedForm1.Count - 2));
                                }
                                else
                                {
                                    injectedLinesForThisAnchor.Add(
                                        $@"Resource\RabbitFX\Lightmap = ref {form1Lightmap}");
                                }
                            }

                            if (form1Normalmap != null)
                            {
                                if (form1NormalmapBlock != null)
                                {
                                    var transformedForm1 =
                                        TransformThisToRabbitFxDirective(
                                            form1NormalmapBlock,
                                            form1Comp.NormalmapHash,
                                            "Normalmap",
                                            form1Normalmap);

                                    injectedLinesForThisAnchor.AddRange(
                                        transformedForm1.Skip(1).Take(transformedForm1.Count - 2));
                                }
                                else
                                {
                                    injectedLinesForThisAnchor.Add(
                                        $@"Resource\RabbitFX\Normalmap = ref {form1Normalmap}");
                                }
                            }

                            // Handle Materialmap / Cutoutmap / Specialmap for Form 1
                            if (form1Materialmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Materialmap = ref {form1Materialmap}");
                            }

                            if (form1Cutoutmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Cutoutmap = ref {form1Cutoutmap}");
                            }

                            if (form1Specialmap != null)
                            {
                                injectedLinesForThisAnchor.Add(
                                    $@"Resource\RabbitFX\Specialmap = ref {form1Specialmap}");
                            }

                            injectedLinesForThisAnchor.Add(EndIf);
                            injectedLinesForThisAnchor.Add(EndIf);

                            injectedLinesForThisAnchor.Add(
                                RunRabbitFxSetTextures);

                            componentsApplied++;
                            lines.InsertRange(i + 1, injectedLinesForThisAnchor);
                            i += injectedLinesForThisAnchor.Count;
                            continue;

                        }
                    }

                    var diffuse = ResolveRefTarget(hashToResource, comp.DiffuseHash);
                    if (diffuse == null)
                        continue; // can't do anything for this component without diffuse

                    // Extract the if $object_detected ... endif block from base hash
                    var objectDetectedBlock = ExtractObjectDetectedBlock(original, comp.DiffuseHash);

                    if (objectDetectedBlock != null)
                    {
                        // Transform the block: replace "this = " with "Resource\RabbitFX\Diffuse = ref "
                        var transformedBlock = TransformThisToRabbitFxDirective(objectDetectedBlock, comp.DiffuseHash, "Diffuse", diffuse);
                        injectedLinesForThisAnchor.AddRange(transformedBlock);
                    }
                    else
                    {
                        // No existing toggle, simple injection
                        injectedLinesForThisAnchor.Add(IfObjectDetected);
                        injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Diffuse = ref {diffuse}");
                        injectedLinesForThisAnchor.Add(EndIf);
                    }

                    // Handle lightmap if present
                    var lightmap = ResolveRefTarget(hashToResource, comp.LightmapHash);
                    if (lightmap != null)
                    {
                        var lightmapBlock = ExtractObjectDetectedBlock(original, comp.LightmapHash);
                        if (lightmapBlock != null)
                        {
                            var transformedBlock = TransformThisToRabbitFxDirective(lightmapBlock, comp.LightmapHash, "Lightmap", lightmap);
                            injectedLinesForThisAnchor.AddRange(transformedBlock);
                        }
                        else
                        {
                            injectedLinesForThisAnchor.Add(IfObjectDetected);
                            injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Lightmap = ref {lightmap}");
                            injectedLinesForThisAnchor.Add(EndIf);
                        }
                    }

                    // Handle normalmap if present
                    var normalmap = ResolveRefTarget(hashToResource, comp.NormalmapHash);
                    if (normalmap != null)
                    {
                        var normalmapBlock = ExtractObjectDetectedBlock(original, comp.NormalmapHash);
                        if (normalmapBlock != null)
                        {
                            var transformedBlock = TransformThisToRabbitFxDirective(normalmapBlock, comp.NormalmapHash, "Normalmap", normalmap);
                            injectedLinesForThisAnchor.AddRange(transformedBlock);
                        }
                        else
                        {
                            injectedLinesForThisAnchor.Add(IfObjectDetected);
                            injectedLinesForThisAnchor.Add($@"Resource\RabbitFX\Normalmap = ref {normalmap}");
                            injectedLinesForThisAnchor.Add(EndIf);
                        }
                    }

                    // Handle materialmap if present
                    var materialmap =
                        ResolveRefTarget(
                            hashToResource,
                            comp.MaterialmapHash);

                    if (materialmap != null)
                    {
                        injectedLinesForThisAnchor.Add(
                            IfObjectDetected);

                        injectedLinesForThisAnchor.Add(
                            $@"Resource\RabbitFX\Materialmap = ref {materialmap}");

                        injectedLinesForThisAnchor.Add(
                            EndIf);
                    }

                    // Handle cutoutmap if present
                    var cutoutmap =
                        ResolveRefTarget(
                            hashToResource,
                            comp.CutoutmapHash);

                    if (cutoutmap != null)
                    {
                        injectedLinesForThisAnchor.Add(
                            IfObjectDetected);

                        injectedLinesForThisAnchor.Add(
                            $@"Resource\RabbitFX\Cutoutmap = ref {cutoutmap}");

                        injectedLinesForThisAnchor.Add(
                            EndIf);
                    }

                    // Handle specialmap if present
                    var specialmap =
                        ResolveRefTarget(
                            hashToResource,
                            comp.SpecialmapHash);

                    if (specialmap != null)
                    {
                        injectedLinesForThisAnchor.Add(
                            IfObjectDetected);

                        injectedLinesForThisAnchor.Add(
                            $@"Resource\RabbitFX\Specialmap = ref {specialmap}");

                        injectedLinesForThisAnchor.Add(
                            EndIf);
                    }

                    // Must run RabbitFX SetTextures after all texture refs
                    injectedLinesForThisAnchor.Add(
                        RunRabbitFxSetTextures);

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
        private static List<string> WrapForm2Block(
            List<string> form1Lines,
            List<string> form2Lines)
        {
            var result = new List<string>();

            result.Add(IfObjectDetected);

            result.Add("if $form2 == 1");

            foreach (var line in form2Lines.Skip(1).Take(form2Lines.Count - 2))
                result.Add(line);

            result.Add("else");

            foreach (var line in form1Lines.Skip(1).Take(form1Lines.Count - 2))
                result.Add(line);

            result.Add("endif");
            result.Add(EndIf);

            return result;
        }
        /// <summary>
        /// Transform the if $object_detected block by replacing "this = " with RabbitFX resource directives
        /// Example: "this = ResourceTexture6" becomes "Resource\RabbitFX\Diffuse = ref ResourceTexture6"
        /// </summary>
        private static List<string> TransformThisToRabbitFxDirective(List<string> objectDetectedBlock, string baseHash, string resourceType, string refTarget)
        {
            var transformed = new List<string>();

            foreach (var line in objectDetectedBlock)
            {
                var trimmed = line.TrimStart();

                // Check if this line contains "this = "
                if (trimmed.StartsWith("this = ", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the resource name after "this = "
                    var resourceName = trimmed.Substring("this = ".Length).Trim();

                    // Get the indentation from the original line
                    var indent = line.Substring(0, line.Length - trimmed.Length);

                    // Replace with RabbitFX directive
                    transformed.Add($"{indent}Resource\\RabbitFX\\{resourceType} = ref {resourceName}");
                }
                else
                {
                    // Keep other lines as-is (if/elif/else/endif, etc.)
                    transformed.Add(line);
                }
            }

            return transformed;
        }

        private static List<string> InjectAeroEyesTriggerBlock(List<string> lines)
        {
            // Avoid double-inject (either of the new headers)
            if (lines.Any(l => l.Trim().Equals("[TextureOverrideAeroEyes_RoverM]", StringComparison.OrdinalIgnoreCase)) ||
                lines.Any(l => l.Trim().Equals("[TextureOverrideAeroEyes_RoverF]", StringComparison.OrdinalIgnoreCase)))
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
        private static List<string> InjectForm2IntoConstants(List<string> lines)
        {
            int constantsStart = FindSectionStart(lines, "Constants");
            if (constantsStart < 0)
                return lines;

            int constantsEnd = FindSectionEnd(lines, constantsStart);

            bool hasForm2 =
                lines.Skip(constantsStart)
                     .Take(constantsEnd - constantsStart)
                     .Any(l => LineContains(l, Form2Const1));

            bool hasForm2Check =
                lines.Skip(constantsStart)
                     .Take(constantsEnd - constantsStart)
                     .Any(l => LineContains(l, Form2Const2));

            if (hasForm2 && hasForm2Check)
                return lines;

            var insertLines = new List<string>();

            if (!hasForm2)
                insertLines.Add(Form2Const1);

            if (!hasForm2Check)
                insertLines.Add(Form2Const2);

            lines.InsertRange(constantsEnd, insertLines);
            return lines;
        }

        private static List<string> InjectForm2PresentHook(List<string> lines)
        {
            int presentStart = FindSectionStart(lines, "Present");
            if (presentStart < 0)
                return lines;

            int presentEnd = FindSectionEnd(lines, presentStart);

            bool alreadyHasHook = lines.Skip(presentStart).Take(presentEnd - presentStart)
                .Any(l => LineContains(l, "$form2Check = 0"));

            if (alreadyHasHook)
                return lines;

            lines.InsertRange(presentEnd, PresentForm2Block);
            return lines;
        }
        private static List<string> InjectForm2TriggerBlock(
            List<string> lines,
            string triggerHash)
        {
            // Hapus semua [ShaderOverrideForm2] lama.
            // Jangan menghapus [TextureOverrideForm2].
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].Trim().Equals(
                        "[ShaderOverrideForm2]",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                int sectionEnd = FindSectionEnd(lines, i);

                lines.RemoveRange(i, sectionEnd - i);
            }

            // Buat block shader trigger terbaru.
            var newBlock = BuildForm2TriggerBlock(triggerHash);

            // Sisipkan setelah header ShadingDrawCallStacks.
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IndexOf(
                        ShadingDrawCallStacksHeader,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lines.InsertRange(i + 1, newBlock);
                    return lines;
                }
            }

            // Fallback jika header tidak ditemukan.
            lines.Add("");
            lines.AddRange(newBlock);

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
            // map XXXXXXXX -> ResourceTextureK if "this =" exists, otherwise -> ResourceTextureN.
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

        // Preserve full toggle block: copy exact "if $object_detected" ... matching "endif", supports nesting
        private static List<string>? ExtractObjectDetectedBlock(
            string ini,
            string baseHash)
        {
            if (string.IsNullOrWhiteSpace(baseHash))
                return null;

            var lines = SplitLines(ini);

            for (int sectionStart = 0;
                 sectionStart < lines.Count;
                 sectionStart++)
            {
                var sectionHeader = lines[sectionStart].Trim();

                if (!IsSectionHeader(sectionHeader, out var sectionName))
                    continue;

                // Only inspect TextureOverride sections.
                if (!sectionName.StartsWith(
                        "TextureOverride",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int sectionEnd = FindSectionEnd(lines, sectionStart);

                // Find the hash inside THIS section.
                bool hashMatches = false;

                for (int i = sectionStart + 1;
                     i < sectionEnd;
                     i++)
                {
                    if (string.Equals(
                            lines[i].Trim(),
                            $"hash = {baseHash}",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        hashMatches = true;
                        break;
                    }
                }

                if (!hashMatches)
                    continue;

                // Now search for if $object_detected
                // ONLY inside this same section.
                int objectDetectedStart = -1;

                for (int i = sectionStart + 1;
                     i < sectionEnd;
                     i++)
                {
                    if (string.Equals(
                            lines[i].Trim(),
                            "if $object_detected",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        objectDetectedStart = i;
                        break;
                    }
                }

                if (objectDetectedStart < 0)
                    return null;

                // Extract the complete if $object_detected ... endif block.
                int depth = 0;
                var collected = new List<string>();

                for (int i = objectDetectedStart;
                     i < sectionEnd;
                     i++)
                {
                    var trimmed = lines[i].Trim();

                    if (trimmed.StartsWith(
                            "if ",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        depth++;
                    }
                    else if (string.Equals(
                                 trimmed,
                                 "endif",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        depth--;
                    }

                    collected.Add(lines[i]);

                    if (depth == 0)
                        break;
                }

                if (collected.Count > 0 &&
                    string.Equals(
                        collected[0].Trim(),
                        "if $object_detected",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return collected;
                }

                return null;
            }

            return null;
        }
    }
}