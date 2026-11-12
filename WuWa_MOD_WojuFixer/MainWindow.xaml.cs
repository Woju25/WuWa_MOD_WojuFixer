using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WuWa_MOD_WojuFixer.Core;
using WinForms = System.Windows.Forms;
using System.Windows.Documents;
using System.Windows.Media;

namespace WuWa_MOD_WojuFixer
{
    public partial class MainWindow : Window
    {
        private void TopUpWuWaButton_Click(object sender, RoutedEventArgs e)
        {
            var topUpWindow = new TopUpWindow
            {
                Owner = this
            };

            topUpWindow.ShowDialog();
        }

        private Dictionary<string, string> _uniqueHashToCharacter = new();

        public MainWindow()
        {
            InitializeComponent();
            PatchOldIniButton.IsEnabled = false;
            PatchOldIniButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 69, 69));
            PatchOldIniButton.Foreground = System.Windows.Media.Brushes.White;
            FolderTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            System.Windows.MessageBox.Show(
                "This version of Woju Fixer is intended for Wuthering Waves up to Game Version 3.8.\n\n" +
                "Future game updates may introduce new issues that this version cannot fix.\n\n" +
                "If a mod remains broken after using this fixer, please check YouTube: Woju for the latest fixer version.\n\n" +
                "Press OK to continue.",
                "Woju - WuWa MOD Fixer v3.8.1.0",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Log("Ready (Woju).");

            // Load unique hashes from Assets JSON/hashes.json at startup
            try
            {
                var assetsJsonPath = Path.Combine(AppContext.BaseDirectory, "Assets JSON");
                var hashesPath = Path.Combine(assetsJsonPath, "hashes.json");
                _uniqueHashToCharacter = CharacterDetector.LoadUniqueHashesFromJson(hashesPath);
                Log($"Loaded {_uniqueHashToCharacter.Count} unique character hashes from Assets JSON/hashes.json");
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not load unique hashes: {ex.Message}");
            }

            // Add F11 hotkey for Developer Mode
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F11)
                {
                    e.Handled = true;
                    OpenDeveloperMode();
                }
            };
        }

        private void StableTexturesRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Optional
        }

        private void HighLodStableRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Optional
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select mod folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            var result = dialog.ShowDialog();
            if (result == WinForms.DialogResult.OK && Directory.Exists(dialog.SelectedPath))
            {
                FolderTextBox.Text = dialog.SelectedPath;
                FolderTextBox.Foreground = System.Windows.Media.Brushes.Black;

                // Reset patch approval whenever a different folder is selected.
                SetPatchOldIniButtonEnabled(false);
                Log($"Selected folder: {dialog.SelectedPath}");

                // Detect and log character as usual, then check for an old WWMI template.
                DetectAndLogCharacter(dialog.SelectedPath);
                CheckForOldIniTemplateAndAsk(dialog.SelectedPath);
            }
        }

        private string? _approvedPatchFolder;

        private void SetPatchOldIniButtonEnabled(bool enabled)
        {
            PatchOldIniButton.IsEnabled = enabled;
            PatchOldIniButton.Background = enabled
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 18, 18))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 69, 69));
            PatchOldIniButton.Foreground = System.Windows.Media.Brushes.White;
            _approvedPatchFolder = enabled ? FolderTextBox.Text?.Trim() : null;
        }

        private static string[] GetEligibleIniFiles(string folder)
        {
            return Directory.GetFiles(folder, "*.ini", SearchOption.AllDirectories)
                .Where(file => !Path.GetFileName(file)
                    .Contains("disabled", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static bool UsesOldWwmiTemplate(string iniContent)
        {
            // Only WWMI ALPHA-2, BETA-1, and BETA-2 templates require the legacy template patch.
            // BETA-5 and newer are already compatible and must not be offered for patching.
            // Match the version header as a complete comment line, ignoring case and whitespace.
            return Regex.IsMatch(
                iniContent,
                @"^[ \t]*;[ \t]*WWMI[ \t]+(?:ALPHA-2|BETA-1|BETA-2)[ \t]+INI[ \t]*\r?$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        private void CheckForOldIniTemplateAndAsk(string folder)
        {
            try
            {
                var iniFiles = GetEligibleIniFiles(folder);
                if (iniFiles.Length == 0)
                    return;

                var oldTemplateFiles = iniFiles
                    .Where(file => UsesOldWwmiTemplate(File.ReadAllText(file)))
                    .ToArray();

                // Per request, do not log anything when no old template is detected.
                if (oldTemplateFiles.Length == 0)
                    return;

                var answer = System.Windows.MessageBox.Show(
                    "The mod.ini file appears to use an Ancient WWMI template.\n\n" +
                    "Updating to the newer template may improve compatibility with Stable Textures injection.\n\n" +
                    "This is optional. Only patch if textures still look messy after applying Stable Textures.\n\n" +
                    "Would you like to enable the 'PATCH OLD .ini' button?",
                    "Woju - WWMI Template Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    SetPatchOldIniButtonEnabled(true);
                    Log("User chose to PATCH the OLD .ini");
                    Log($"Old WWMI template detected in {oldTemplateFiles.Length} .ini file(s). Ready to patch.");
                }
                else
                {
                    SetPatchOldIniButtonEnabled(false);
                    Log("User chose to NOT patch the OLD .ini");
                }
            }
            catch (Exception ex)
            {
                SetPatchOldIniButtonEnabled(false);
                Log($"Warning: Could not check WWMI template: {ex.Message}");
            }
        }

        private async void PatchOldIniButton_Click(object sender, RoutedEventArgs e)
        {
            string folder = FolderTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder) ||
                !string.Equals(folder, _approvedPatchFolder, StringComparison.OrdinalIgnoreCase))
            {
                SetPatchOldIniButtonEnabled(false);
                return;
            }

            // Disable immediately to prevent repeated clicks. The button will remain disabled
            // after this attempt; a new folder selection is required for a new approval.
            SetPatchOldIniButtonEnabled(false);
            PatchOldIniButton.IsEnabled = false;

            try
            {
                var oldTemplateFiles = GetEligibleIniFiles(folder)
                    .Where(file => UsesOldWwmiTemplate(File.ReadAllText(file)))
                    .ToArray();

                if (oldTemplateFiles.Length == 0)
                {
                    Log("Patch OLD .ini: no eligible old-template files remain to patch.");
                    return;
                }

                int patchedFiles = 0;
                int skippedFiles = 0;
                int failedFiles = 0;

                foreach (string iniFile in oldTemplateFiles)
                {
                    try
                    {
                        string originalContent = File.ReadAllText(iniFile);
                        string updatedContent = originalContent;
                        var patchSummaries = new List<string>();

                        // Keep this migration focused: Constants + the two skeleton command lists.
                        // Validate the source contains the values needed by the Constants migration.
                        if (!Regex.IsMatch(
                                originalContent,
                                @"(?im)^\s*\[Constants\]\s*$") ||
                            !Regex.IsMatch(
                                originalContent,
                                @"(?im)^\s*global\s+\$shapekey_vertex_count\s*=\s*\d+"))
                        {
                            skippedFiles++;
                            Log($"Skipped {Path.GetFileName(iniFile)}: missing [Constants] or $shapekey_vertex_count.");
                            continue;
                        }

                        bool constantsPatched = TryPatchConstantsBlock(
                            updatedContent,
                            out string constantsUpdated,
                            out string constantsSummary);

                        if (constantsPatched)
                        {
                            updatedContent = constantsUpdated;
                            patchSummaries.Add($"[Constants]: {constantsSummary}");
                        }
                        else
                        {
                            // Even when the patcher reports no change, inspect the content it returned.
                            updatedContent = constantsUpdated;
                        }

                        TryPatchUpdateMergedSkeleton(
                            updatedContent,
                            out string updateSkeletonUpdated,
                            out string updateSkeletonSummary);

                        if (!string.Equals(updatedContent, updateSkeletonUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"[CommandListUpdateMergedSkeleton]: {updateSkeletonSummary}");
                        updatedContent = updateSkeletonUpdated;

                        TryPatchMergeSkeleton(
                            updatedContent,
                            out string mergeSkeletonUpdated,
                            out string mergeSkeletonSummary);

                        if (!string.Equals(updatedContent, mergeSkeletonUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"[CommandListMergeSkeleton]: {mergeSkeletonSummary}");
                        updatedContent = mergeSkeletonUpdated;

                        TryPatchTriggerResourceOverrides(
                            updatedContent,
                            out string triggerOverridesUpdated,
                            out string triggerOverridesSummary);
                        if (!string.Equals(updatedContent, triggerOverridesUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"[CommandListTriggerResourceOverrides]: {triggerOverridesSummary}");
                        updatedContent = triggerOverridesUpdated;

                        TryPatchOverrideSharedResources(
                            updatedContent,
                            out string sharedResourcesUpdated,
                            out string sharedResourcesSummary);
                        if (!string.Equals(updatedContent, sharedResourcesUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"[CommandListOverrideSharedResources]: {sharedResourcesSummary}");
                        updatedContent = sharedResourcesUpdated;

                        TryPatchComponentSections(
                            updatedContent,
                            out string componentsUpdated,
                            out string componentsSummary);

                        if (!string.Equals(updatedContent, componentsUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"[TextureOverrideComponentX]: {componentsSummary}");
                        updatedContent = componentsUpdated;

                        // Capture the original count before any other patcher changes the INI.
                        // This avoids depending on a later search through the mutated content.
                        var originalShapeCountMatch = Regex.Match(
                            originalContent,
                            @"(?im)^[ \t]*global[ \t]+\$shapekey_vertex_count[ \t]*=[ \t]*(\d+)[ \t]*(?:;[^\r\n]*)?\r?$");
                        if (!originalShapeCountMatch.Success)
                            throw new InvalidDataException("Cannot patch shape keys: original global $shapekey_vertex_count was not found. See DEBUG lines above.");

                        int originalShapeCount = int.Parse(originalShapeCountMatch.Groups[1].Value);

                        TryPatchShapeKeySections(
                            updatedContent,
                            originalShapeCount,
                            out string shapeKeysUpdated,
                            out string shapeKeysSummary);

                        if (!string.Equals(updatedContent, shapeKeysUpdated, StringComparison.Ordinal))
                            patchSummaries.Add($"Shape-key sections/callbacks: {shapeKeysSummary}");
                        updatedContent = shapeKeysUpdated;

                        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
                        {
                            skippedFiles++;
                            Log($"Skipped {Path.GetFileName(iniFile)}: all targeted sections already match the new template.");
                            continue;
                        }

                        string backupPath = iniFile + ".BAK.woju.TEMPLATE." +
                            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff");

                        BackupManager.CreateBackupAndRegister(iniFile, backupPath);

                        File.WriteAllText(
                            iniFile,
                            updatedContent,
                            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                        patchedFiles++;
                        Log($"PATCH: {Path.GetFileName(iniFile)} updated");
                        foreach (string patchSummary in patchSummaries)
                        {
                            string sectionName = patchSummary.Split(':', 2)[0].Trim();
                            Log($"PATCH: {sectionName}");
                        }
                    }
                    catch (Exception fileEx)
                    {
                        failedFiles++;
                        Log($"ERROR patching OLD .ini in {Path.GetFileName(iniFile)}: {fileEx.Message}");
                    }
                }

                Log($"Patch OLD .ini finished — Updated: {patchedFiles}, Skipped: {skippedFiles}, Failed: {failedFiles}.");

                if (patchedFiles > 0)
                {
                    System.Windows.MessageBox.Show(
                        $"Template patch completed.\n\nUpdated files: {patchedFiles}\nSkipped files: {skippedFiles}\nFailed files: {failedFiles}\n\nUpdated sections: [Constants], skeleton command lists, [CommandListTriggerResourceOverrides], [CommandListOverrideSharedResources], [TextureOverrideComponentX], and batch shape-key sections/callbacks.",
                        "Woju - Patch OLD .ini",
                        MessageBoxButton.OK,
                        failedFiles > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"No files were updated.\nSkipped: {skippedFiles}\nFailed: {failedFiles}",
                        "Woju - Patch OLD .ini",
                        MessageBoxButton.OK,
                        failedFiles > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR while preparing Patch OLD .ini: {ex.Message}");
                System.Windows.MessageBox.Show(
                    "Failed to prepare the template patch.\n\n" + ex.Message,
                    "Woju - Patch OLD .ini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string GetConstantsDebugPreview(string content)
        {
            var match = Regex.Match(
                content,
                @"(?ims)^[ \t]*\[Constants\][ \t]*\r?\n(?<body>.*?)(?=^[ \t]*\[|\z)");

            if (!match.Success)
                return "<[Constants] section not found>";

            string body = match.Groups["body"].Value;
            var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.IndexOf("shapekey_vertex_count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               line.IndexOf("shapekey_vertex_offset", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(10)
                .ToArray();

            return lines.Length == 0 ? "<no shape-key count declarations in Constants>" : string.Join(" | ", lines);
        }

        private static bool TryPatchConstantsBlock(string content, out string updatedContent, out string summary)
        {
            updatedContent = content;
            summary = string.Empty;

            var constantsMatch = Regex.Match(
                content,
                @"(?ims)^(?<header>[ \t]*\[Constants\][ \t]*\r?\n)(?<body>.*?)(?=^[ \t]*\[|\z)");

            if (!constantsMatch.Success)
                return false;

            string body = constantsMatch.Groups["body"].Value;
            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            // Read the legacy value from the whole file, then mirror it into batch0.
            var shapeCountMatch = Regex.Match(
                content,
                @"(?im)^[ \t]*global[ \t]+\$shapekey_vertex_count[ \t]*=[ \t]*(\d+)[ \t]*(?:;[^\r\n]*)?\r?$");

            if (!shapeCountMatch.Success)
                return false;

            string shapeCount = shapeCountMatch.Groups[1].Value;

            var componentIds = Regex.Matches(
                    content,
                    @"(?im)^[ \t]*\[TextureOverrideComponent(\d+)\][ \t]*\r?$")
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            // Remove existing managed declarations first so repeated runs do not duplicate them.
            body = Regex.Replace(body,
                @"(?im)^[ \t]*global[ \t]+\$shapekey_vertex_offset_batch\d+[ \t]*=.*(?:\r?\n|$)", string.Empty);
            body = Regex.Replace(body,
                @"(?im)^[ \t]*global[ \t]+\$shapekey_vertex_count_batch\d+[ \t]*=.*(?:\r?\n|$)", string.Empty);
            body = Regex.Replace(body,
                @"(?im)^[ \t]*global[ \t]+\$merge_status_id(?:_\d+)?[ \t]*=.*(?:\r?\n|$)", string.Empty);

            var declarations = new List<string>
            {
                "global $shapekey_vertex_offset_batch0 = 0",
                $"global $shapekey_vertex_count_batch0 = {shapeCount}",
                "global $merge_status_id = 0"
            };
            declarations.AddRange(componentIds.Select(id => $"global $merge_status_id_{id} = 0"));
            string managedDeclarations = string.Join(newline, declarations);

            // Insert after object_detected if present; otherwise append to [Constants].
            var objectDetectedMatch = Regex.Match(
                body,
                @"(?im)^[ \t]*global[ \t]+\$object_detected[ \t]*=.*(?:\r?\n|$)");

            if (objectDetectedMatch.Success)
            {
                int insertAt = objectDetectedMatch.Index + objectDetectedMatch.Length;
                body = body.Insert(insertAt, managedDeclarations + newline);
            }
            else
            {
                body = body.TrimEnd('\r', '\n') + newline + managedDeclarations + newline;
            }

            string updatedConstants = constantsMatch.Groups["header"].Value + body;
            updatedContent = content.Substring(0, constantsMatch.Index) + updatedConstants +
                content.Substring(constantsMatch.Index + constantsMatch.Length);

            if (string.Equals(updatedContent, content, StringComparison.Ordinal))
                return false;

            summary = $"batch0 shape-key count set to {shapeCount}; merge status declarations generated for {componentIds.Length} component(s)";
            return true;
        }

        private static bool TryPatchUpdateMergedSkeleton(
            string content,
            out string updatedContent,
            out string summary)
        {
            updatedContent = content;
            summary = string.Empty;

            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            var componentIds = Regex.Matches(
                    content,
                    @"(?im)^\s*\[TextureOverrideComponent(\d+)\]\s*$")
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            if (componentIds.Length == 0)
                throw new InvalidDataException("No [TextureOverrideComponentX] sections were found.");

            string replacementBody = string.Join(newline,
                componentIds.Select(id => $"$merge_status_id_{id} = 0")
                    .Concat(new[]
                    {
                        "ResourceMergedSkeleton = copy ResourceMergedSkeletonRW",
                        "ResourceExtraMergedSkeleton = copy ResourceExtraMergedSkeletonRW"
                    }));

            updatedContent = ReplaceIniSectionBody(
                content,
                "CommandListUpdateMergedSkeleton",
                replacementBody,
                newline,
                out bool changed);

            summary = $"reset merge status for {componentIds.Length} component(s) and preserved skeleton resource copies";
            return changed;
        }

        private static bool TryPatchMergeSkeleton(
            string content,
            out string updatedContent,
            out string summary)
        {
            updatedContent = content;
            summary = string.Empty;

            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            string replacementBody = string.Join(newline, new[]
            {
                "if $merge_status_id == 0",
                "\tif vs-cb4 == 3381.7777",
                "\t\tcs-cb8 = ref vs-cb4",
                "\t\tcs-u6 = ResourceMergedSkeletonRW",
                "\t\t$\\WWMIv1\\custom_mesh_scale = 1.00",
                "\t\trun = CustomShader\\WWMIv1\\SkeletonMerger",
                "\t\t$merge_status_id = 1",
                "\tendif",
                "endif",
                "if $merge_status_id == 1",
                "\tif vs-cb4 == 3381.7777 && vs-cb3 == 3381.7777",
                "\t\tcs-cb8 = ref vs-cb3",
                "\t\tcs-u6 = ResourceExtraMergedSkeletonRW",
                "\t\t$\\WWMIv1\\custom_mesh_scale = 1.00",
                "\t\trun = CustomShader\\WWMIv1\\SkeletonMerger",
                "\t\t$merge_status_id = 2",
                "\tendif",
                "endif"
            });

            updatedContent = ReplaceIniSectionBody(
                content,
                "CommandListMergeSkeleton",
                replacementBody,
                newline,
                out bool changed);

            summary = "migrated to the WWMI 1.10 merge-status flow";
            return changed;
        }

        /// <summary>
        /// Replaces only the body of an existing INI section. Trailing blank lines and
        /// trailing comment separators are retained so unrelated surrounding content
        /// is not accidentally removed.
        /// </summary>
        private static string ReplaceIniSectionBody(
            string content,
            string sectionName,
            string replacementBody,
            string newline,
            out bool changed)
        {
            var headerMatch = Regex.Match(
                content,
                $@"(?im)^[ \t]*\[{Regex.Escape(sectionName)}\][ \t]*(?:\r?\n|$)");

            if (!headerMatch.Success)
                throw new InvalidDataException($"Section [{sectionName}] was not found.");

            int bodyStart = headerMatch.Index + headerMatch.Length;
            var nextSectionMatch = Regex.Match(
                content.Substring(bodyStart),
                @"(?im)^[ \t]*\[[^\]\r\n]+\][ \t]*(?:\r?$)");

            int bodyEnd = nextSectionMatch.Success
                ? bodyStart + nextSectionMatch.Index
                : content.Length;

            string originalBody = content.Substring(bodyStart, bodyEnd - bodyStart);

            // Keep trailing blank lines and separator comments, e.g. "; Resources: ...".
            var suffixMatch = Regex.Match(
                originalBody,
                @"(?ms)(?:^[ \t]*;[^\r\n]*(?:\r?\n|$)|^[ \t]*(?:\r?\n|$))*\z");

            string suffix = suffixMatch.Success ? suffixMatch.Value : string.Empty;
            string normalizedReplacement = replacementBody.TrimEnd('\r', '\n');
            string newBody = normalizedReplacement + newline + suffix;

            changed = !string.Equals(originalBody, newBody, StringComparison.Ordinal);
            if (!changed)
                return content;

            return content.Substring(0, bodyStart) +
                   newBody +
                   content.Substring(bodyEnd);
        }

        private static bool TryPatchTriggerResourceOverrides(
            string content,
            out string updatedContent,
            out string summary)
        {
            updatedContent = content;
            summary = string.Empty;

            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var sectionMatch = Regex.Match(
                content,
                @"(?im)^[ \t]*\[CommandListTriggerResourceOverrides\][ \t]*\r?$\r?\n(?<body>.*?)(?=^[ \t]*\[[^\]\r\n]+\][ \t]*\r?$|\z)",
                RegexOptions.Singleline);

            if (!sectionMatch.Success)
            {
                summary = "section [CommandListTriggerResourceOverrides] not found";
                return false;
            }

            string body = sectionMatch.Groups["body"].Value;

            // Stable Textures intentionally removes ps-t0 through ps-t5.
            // Do not recreate those entries here. The template patch only needs
            // to ensure ps-t8 exists, while preserving all other existing lines.
            bool hasPsT8 = Regex.IsMatch(
                body,
                @"(?im)^[ \t]*CheckTextureOverride[ \t]*=[ \t]*ps-t8[ \t]*\r?$");

            if (hasPsT8)
            {
                summary = "ps-t8 already present; existing checks preserved";
                return false;
            }

            int insertAt;
            var psT7Match = Regex.Match(
                body,
                @"(?im)^[ \t]*CheckTextureOverride[ \t]*=[ \t]*ps-t7[ \t]*(?:\r?\n|$)");

            if (psT7Match.Success)
            {
                // Prefer placing ps-t8 directly after ps-t7.
                insertAt = psT7Match.Index + psT7Match.Length;
            }
            else
            {
                // If ps-t7 is absent, place ps-t8 before vs-cb3/vs-cb4.
                var firstVsCheck = Regex.Match(
                    body,
                    @"(?im)^[ \t]*CheckTextureOverride[ \t]*=[ \t]*vs-cb[34][ \t]*\r?$");

                insertAt = firstVsCheck.Success ? firstVsCheck.Index : body.Length;
            }

            string prefix = body.Substring(0, insertAt);
            string suffix = body.Substring(insertAt);

            if (prefix.Length > 0 &&
                !prefix.EndsWith("\n", StringComparison.Ordinal) &&
                !prefix.EndsWith("\r", StringComparison.Ordinal))
            {
                prefix += newline;
            }

            string addition = "CheckTextureOverride = ps-t8" + newline;
            string updatedBody = prefix + addition + suffix;

            updatedContent =
                content.Substring(0, sectionMatch.Groups["body"].Index) +
                updatedBody +
                content.Substring(sectionMatch.Groups["body"].Index + body.Length);

            summary = "added missing check: ps-t8; preserved existing checks including vs-cb3/vs-cb4";
            return true;
        }

        private static bool TryPatchOverrideSharedResources(
            string content,
            out string updatedContent,
            out string summary)
        {
            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string replacementBody = string.Join(newline, new[]
            {
                "ResourceBypassVB0 = ref vb0",
                "ib = ResourceIndexBuffer",
                "vb0 = ResourcePositionBuffer",
                "vb1 = ResourceVectorBuffer",
                "vb2 = ResourceTexcoordBuffer",
                "vb3 = ResourceColorBuffer",
                "vb4 = ResourceBlendBuffer",
                "if vs-cb4 == 3381.7777",
                "    vs-cb4 = ref ResourceMergedSkeleton",
                "    if vs-cb3 == 3381.7777",
                "        vs-cb3 = ref ResourceExtraMergedSkeleton",
                "    endif",
                "elif vs-cb3 == 3381.7777",
                "    vs-cb3 = ref ResourceMergedSkeleton",
                "endif"
            });

            updatedContent = ReplaceIniSectionBody(
                content,
                "CommandListOverrideSharedResources",
                replacementBody,
                newline,
                out bool changed);

            summary = changed
                ? "updated skeleton resource references to the new nested vs-cb4/vs-cb3 flow"
                : "shared skeleton resource references already use the new flow";
            return changed;
        }

        private static bool TryPatchComponentSections(
            string content,
            out string updatedContent,
            out string summary)
        {
            updatedContent = content;
            summary = string.Empty;
            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            var componentMatches = Regex.Matches(
                content,
                @"(?im)^[ \t]*\[TextureOverrideComponent(?<id>\d+)\][ \t]*(?:\r?\n|$)");

            if (componentMatches.Count == 0)
                throw new InvalidDataException("No [TextureOverrideComponentX] sections were found.");

            int updatedComponents = 0;
            foreach (Match componentMatch in componentMatches.Cast<Match>().Reverse())
            {
                int componentId = int.Parse(componentMatch.Groups["id"].Value);
                int bodyStart = componentMatch.Index + componentMatch.Length;
                var nextSection = Regex.Match(
                    content.Substring(bodyStart),
                    @"(?im)^[ \t]*\[[^\]\r\n]+\][ \t]*(?:\r?$)");
                int bodyEnd = nextSection.Success ? bodyStart + nextSection.Index : content.Length;
                string body = content.Substring(bodyStart, bodyEnd - bodyStart);
                string originalBody = body;

                // Modern template marks each component as detected, which is required by [Present].
                if (!Regex.IsMatch(body, @"(?im)^[ \t]*\$object_detected\s*=\s*1\s*(?:;.*)?$"))
                {
                    var indexCountLine = Regex.Match(
                        body,
                        @"(?im)^[ \t]*match_index_count\s*=\s*[^\r\n]*(?:\r?\n|$)");
                    if (!indexCountLine.Success)
                        throw new InvalidDataException($"[TextureOverrideComponent{componentId}] has no match_index_count line; refusing to guess where to insert $object_detected.");

                    int insertAt = indexCountLine.Index + indexCountLine.Length;
                    body = body.Insert(insertAt, "$object_detected = 1" + newline);
                }

                // Convert the old local state guard into the per-component merge-status flow.
                var legacyGuard = Regex.Match(
                    body,
                    $@"(?ims)^[ \t]*local[ \t]+\$state_id_{componentId}[ \t]*\r?\n[ \t]*if[ \t]+\$state_id_{componentId}[ \t]*!=[ \t]*\$state_id[ \t]*\r?\n(?<block>.*?^[ \t]*endif[ \t]*(?:\r?\n|$))");

                if (legacyGuard.Success)
                {
                    string legacyBlock = legacyGuard.Groups["block"].Value;
                    var offsetMatch = Regex.Match(legacyBlock, @"(?im)^[ \t]*\$\\WWMIv1\\vg_offset[ \t]*=[ \t]*([^\r\n]+)");
                    var countMatch = Regex.Match(legacyBlock, @"(?im)^[ \t]*\$\\WWMIv1\\vg_count[ \t]*=[ \t]*([^\r\n]+)");
                    if (!offsetMatch.Success || !countMatch.Success)
                        throw new InvalidDataException($"[TextureOverrideComponent{componentId}] has an old state guard but no recognizable vg_offset/vg_count assignments.");

                    string replacement = string.Join(newline, new[]
                    {
                        $"if $merge_status_id_{componentId} != 2",
                        $"    $\\WWMIv1\\vg_offset = {offsetMatch.Groups[1].Value.Trim()}",
                        $"    $\\WWMIv1\\vg_count = {countMatch.Groups[1].Value.Trim()}",
                        $"    $merge_status_id = $merge_status_id_{componentId}",
                        "    run = CommandListMergeSkeleton",
                        $"    $merge_status_id_{componentId} = $merge_status_id",
                        "endif"
                    }) + newline;

                    body = body.Substring(0, legacyGuard.Index) + replacement +
                           body.Substring(legacyGuard.Index + legacyGuard.Length);
                }
                else
                {
                    // If this component is already migrated, leave its merge block untouched.
                    bool hasModernGuard = Regex.IsMatch(
                        body,
                        $@"(?im)^[ \t]*if[ \t]+\$merge_status_id_{componentId}[ \t]*!=[ \t]*2[ \t]*$");
                    bool stillHasLegacyGuard = Regex.IsMatch(
                        body,
                        $@"(?im)^[ \t]*local[ \t]+\$state_id_{componentId}[ \t]*$");
                    if (stillHasLegacyGuard && !hasModernGuard)
                        throw new InvalidDataException($"Could not safely migrate [TextureOverrideComponent{componentId}].");
                }

                if (!string.Equals(body, originalBody, StringComparison.Ordinal))
                {
                    content = content.Substring(0, bodyStart) + body + content.Substring(bodyEnd);
                    updatedComponents++;
                }
            }

            updatedContent = content;
            summary = updatedComponents > 0
                ? $"migrated {updatedComponents} component section(s) to per-component merge status and ensured $object_detected = 1"
                : "component sections already use the new merge-status flow";
            return updatedComponents > 0;
        }

        private static bool TryPatchShapeKeySections(
            string content,
            int originalShapeCount,
            out string updatedContent,
            out string summary)
        {
            updatedContent = content;
            summary = string.Empty;
            string originalContent = content;
            string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            bool changed = false;

            // Capture the mod-specific checksum before renaming/rebuilding the setup command.
            var checksumMatch = Regex.Match(
                content,
                @"(?im)^[ \t]*\$\\WWMIv1\\shapekey_checksum(?:_batch0)?[ \t]*=[ \t]*(\d+)[ \t]*(?:;[^\r\n]*)?\r?$");
            string checksum = checksumMatch.Success ? checksumMatch.Groups[1].Value : "2305";

            // Use the value captured from the original INI before any patch steps ran.
            // TryPatchConstantsBlock mirrors this exact value into batch0.
            int shapeCount = originalShapeCount;

            // The supplied new-template example maps 31805 vertices to dispatch size 1274.
            // Use ceil(vertexCount / 25) + 1 so the dispatch count scales with each mod.
            int dispatchSizeY = (int)Math.Ceiling(shapeCount / 25.0) + 1;

            string setupBody = string.Join(newline, new[]
            {
                $"$\\WWMIv1\\shapekey_checksum_batch0 = {checksum}",
                "$\\WWMIv1\\shapekey_vertex_offset_original_batch0 = 0",
                "$\\WWMIv1\\shapekey_vertex_offset_custom_batch0 = $shapekey_vertex_offset_batch0",
                "cs-t33 = ResourceShapeKeyOffsetBuffer",
                "cs-u5 = ResourceCustomShapeKeyValuesRW",
                "cs-u6 = ResourceShapeKeyCBRW",
                "run = CustomShader\\WWMIv1\\ShapeKeyBatchOverrider"
            });

            string loadBody = string.Join(newline, new[]
            {
                $"$\\WWMIv1\\shapekey_dispatch_size_y_original_batch0 = {dispatchSizeY}",
                "$\\WWMIv1\\shapekey_vertex_count_batch0 = $shapekey_vertex_count_batch0",
                "cs-t0 = ResourceShapeKeyVertexIdBuffer",
                "cs-t1 = ResourceShapeKeyVertexOffsetBuffer",
                "cs-u6 = ResourceShapeKeyCBRW",
                "run = CommandList\\WWMIv1\\LoadShapeKeysBatch"
            });

            // Rename legacy command-list section headers. Avoid duplicate headers if a file is partly migrated.
            if (Regex.IsMatch(content, @"(?im)^\s*\[CommandListSetupShapeKeys\]\s*$"))
            {
                content = Regex.Replace(content,
                    @"(?im)^([ \t]*)\[CommandListSetupShapeKeys\]([ \t]*)\r?$",
                    "$1[CommandListSetupShapeKeysBatch]$2");
                changed = true;
            }
            if (Regex.IsMatch(content, @"(?im)^\s*\[CommandListLoadShapeKeys\]\s*$"))
            {
                content = Regex.Replace(content,
                    @"(?im)^([ \t]*)\[CommandListLoadShapeKeys\]([ \t]*)\r?$",
                    "$1[CommandListLoadShapeKeysBatch]$2");
                changed = true;
            }

            // Replace the bodies in the new-named sections, retaining surrounding sections/comments.
            if (Regex.IsMatch(content, @"(?im)^\s*\[CommandListSetupShapeKeysBatch\]\s*$"))
            {
                content = ReplaceIniSectionBody(content, "CommandListSetupShapeKeysBatch", setupBody, newline, out bool setupChanged);
                changed |= setupChanged;
            }
            else
            {
                throw new InvalidDataException("Section [CommandListSetupShapeKeys] or [CommandListSetupShapeKeysBatch] was not found.");
            }

            if (Regex.IsMatch(content, @"(?im)^\s*\[CommandListLoadShapeKeysBatch\]\s*$"))
            {
                content = ReplaceIniSectionBody(content, "CommandListLoadShapeKeysBatch", loadBody, newline, out bool loadChanged);
                changed |= loadChanged;
            }
            else
            {
                throw new InvalidDataException("Section [CommandListLoadShapeKeys] or [CommandListLoadShapeKeysBatch] was not found.");
            }

            // Update callback references to the renamed batch command lists.
            content = Regex.Replace(
                content,
                @"(?im)^[ \t]*run[ \t]*=[ \t]*CommandListSetupShapeKeys[ \t]*(?:\r?$)",
                "        run = CommandListSetupShapeKeysBatch");
            content = Regex.Replace(
                content,
                @"(?im)^[ \t]*run[ \t]*=[ \t]*CommandListLoadShapeKeys[ \t]*(?:\r?$)",
                "        run = CommandListLoadShapeKeysBatch");

            // A legacy variable assignment should not remain anywhere after conversion.
            content = Regex.Replace(
                content,
                @"(?im)^[ \t]*\$\\WWMIv1\\shapekey_checksum[ \t]*=.*(?:\r?\n|$)",
                string.Empty);

            updatedContent = content;
            changed = !string.Equals(originalContent, updatedContent, StringComparison.Ordinal);
            summary = changed
                ? $"converted setup/load commands and callback references to batch0 (checksum {checksum}, dispatch Y {dispatchSizeY})"
                : "shape-key sections and callback references already use batch format";
            return changed;
        }

        private void DetectAndLogCharacter(string folder)
        {
            try
            {
                var modIniFiles = Directory.GetFiles(folder, "*.ini", SearchOption.AllDirectories)
                    .Where(file => !Path.GetFileName(file)
                        .Contains("disabled", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (modIniFiles.Count == 0)
                {
                    Log("Warning: No eligible .ini files found in selected folder.");
                    return;
                }

                var firstModIni = File.ReadAllText(modIniFiles[0]);
                var detectedCharacter = CharacterDetector.DetectCharacterFromModIni(firstModIni, _uniqueHashToCharacter);

                if (!string.IsNullOrWhiteSpace(detectedCharacter))
                {
                    Log($"Mod Detected: {detectedCharacter}");
                }
                else
                {
                    Log("Mod Detected: Unknown character");
                }
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not detect character: {ex.Message}");
            }
        }
        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            RunButton.IsEnabled = false;
            RevertButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            bool success = false;

            try
            {
                var folder = FolderTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show("Please select a valid folder first.", "Woju");
                    return;
                }

                var modIniFiles = Directory.GetFiles(folder, "*.ini", SearchOption.AllDirectories)
                    .Where(file => !Path.GetFileName(file)
                        .Contains("disabled", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (modIniFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show("No eligible .ini files found in selected folder.", "Woju");
                    return;
                }

                // Detect character from first mod.ini
                var firstModIni = File.ReadAllText(modIniFiles[0]);
                var detectedCharacter = CharacterDetector.DetectCharacterFromModIni(firstModIni, _uniqueHashToCharacter);
                var isSpecialCharacter = CharacterDetector.IsSpecialCharacter(detectedCharacter);

                // Lynae OLD detection.
                // 0c33d628 is the Lynae hash used from game version 3.1 through 3.5.
                // IMPORTANT: This is checked against the ORIGINAL mod.ini,
                // before Hash Replacement runs.
                bool isLynaeOldHash =
                    string.Equals(
                        detectedCharacter,
                        "Lynae",
                        StringComparison.OrdinalIgnoreCase)
                    && firstModIni.Contains(
                        "0c33d628",
                        StringComparison.OrdinalIgnoreCase);

                bool isLynaeModernWWMI = false;
                bool isLynaeLegacyWWMI = false;

                if (isLynaeOldHash)
                {
                    isLynaeModernWWMI = IsModernWWMITools(firstModIni);
                    isLynaeLegacyWWMI = !isLynaeModernWWMI;

                    if (isLynaeModernWWMI)
                    {
                        Log("Lynae OLD detected (Modern WWMI Tools).");
                    }
                    else
                    {
                        Log("Lynae OLD detected (Legacy WWMI Tools).");
                    }
                }
                else if (string.Equals(
                             detectedCharacter,
                             "Lynae",
                             StringComparison.OrdinalIgnoreCase))
                {
                    Log("Lynae NEW detected. VG Fix will be skipped.");
                }

                // STEP 2: Lynae Vertex Group Fix
                if (isLynaeOldHash)
                {
                    if (isLynaeModernWWMI)
                    {
                        Log("Lynae OLD / Modern WWMI: Starting Vertex Group Fix...");

                        VertexGroupRemapper.ApplyLynaeFix(
                            folder,
                            Log);
                    }
                    else if (isLynaeLegacyWWMI)
                    {
                        Log("Lynae OLD / Legacy WWMI detected.");
                        Log("WARNING: This mod was created with an older WWMI Tools version.");
                        Log("WARNING: Its weight data cannot be fixed perfectly by the fixer.");
                        Log("WARNING: Please ask the original mod author to re-export/update the mod.");
                        Log("Lynae OLD / Legacy WWMI: Starting Vertex Group Fix...");

                        VertexGroupRemapper.ApplyLynaeFix(
                            folder,
                            Log);
                    }
                }
                else if (string.Equals(
                             detectedCharacter,
                             "Lynae",
                             StringComparison.OrdinalIgnoreCase))
                {
                    Log("Lynae VG Fix skipped: mod is not Lynae OLD.");
                }
                else
                {
                    Log("VG Fix skipped");
                }
                // Determine what to run based on radio button selection
                bool runHashReplacement = true;
                bool runInvisibleFix = false;
                bool runStableTextures = false;
                bool runHighLod = false;

                if (InjectionNoneRadio.IsChecked == true)
                {
                    runHashReplacement = true;
                    runInvisibleFix = false;
                    runStableTextures = false;
                    runHighLod = false;

                    Log("Mode: Hash Replacement Only");
                }
                else if (InvisibleFixRadio.IsChecked == true)
                {
                    runHashReplacement = true;
                    runInvisibleFix = true;
                    runStableTextures = false;
                    runHighLod = false;

                    Log("Mode: Hash Replacement + Invisible Fix");
                }
                else if (StableTexturesRadio.IsChecked == true)
                {
                    runHashReplacement = true;
                    runInvisibleFix = false;
                    runStableTextures = true;
                    runHighLod = false;

                    Log("Mode: Hash Replacement + StableTextures Injection");
                }
                else if (HighLodStableRadio.IsChecked == true)
                {
                    runHashReplacement = true;
                    runInvisibleFix = false;
                    runStableTextures = true;
                    runHighLod = true;

                    Log("Mode: Hash Replacement + StableTextures + High LOD Injection");
                }
                // STEP 1: Hash Replacement (always runs if enabled)
                if (runHashReplacement)
                {
                    await RunHashReplacement(folder);
                }


                // STEP 3: StableTextures Injection (if enabled)
                if (runStableTextures)
                {
                    await RunStableTexturesInjection(folder);
                }

                // STEP 4: High LOD Injection (if enabled)
                if (runHighLod)
                {
                    await RunHighLodInjection(folder);
                }

                // STEP 5: Invisible Fix
                if (runInvisibleFix)
                {
                    RunInvisibleFix(folder);
                }

                System.Windows.MessageBox.Show("Completed successfully.", "Woju");
                Log("Completed successfully.");
                success = true;

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Woju");
                Log("ERROR: " + ex);
            }
            finally
            {
                RunButton.IsEnabled = true;
                RevertButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
            }
        }
        private void AddDerivedHashButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AddDerivedHashButton.IsEnabled = false;
            RunButton.IsEnabled = false;
            RevertButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;

            try
            {
                string folder = FolderTextBox.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(folder) ||
                    !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show(
                        "Please select a valid mod folder first.",
                        "Woju");

                    return;
                }

                var modIniFiles = Directory
                    .GetFiles(
                        folder,
                        "*.ini",
                        SearchOption.AllDirectories)
                    .Where(file =>
                        !Path.GetFileName(file)
                            .Contains(
                                "disabled",
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (modIniFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show(
                        "No eligible .ini files found in the selected folder.",
                        "Woju");

                    return;
                }

                string firstModIni =
                    File.ReadAllText(modIniFiles[0]);

                string detectedCharacter =
                    CharacterDetector.DetectCharacterFromModIni(
                        firstModIni,
                        _uniqueHashToCharacter);

                if (string.IsNullOrWhiteSpace(detectedCharacter))
                {
                    detectedCharacter = "Unknown";
                }

                Log("========================================");
                Log("Add Derived Hash");
                Log($"Mod Detected: {detectedCharacter}");
                Log("Running Derived Hash Fix...");

                // =========================================================
                // DERIVED HASH ONLY
                // =========================================================

                DerivedHashFixer.Apply(
                    folder,
                    detectedCharacter,
                    Log);

                Log("Derived Hash Fix completed.");
                Log("Add Derived Hash only for Certain Character, Support Woju to update this feature for more Characters");

                System.Windows.MessageBox.Show(
                    "Add Derived Hash completed successfully.",
                    "Woju");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);

                System.Windows.MessageBox.Show(
                    "Error: " + ex.Message,
                    "Woju");
            }
            finally
            {
                AddDerivedHashButton.IsEnabled = true;
                RunButton.IsEnabled = true;
                RevertButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
            }
        }
        private async Task RunHashReplacement(string folder)
        {
            var hashesPath = Path.Combine(AppContext.BaseDirectory, "Assets JSON", "hashes.json");
            Log($"Loading hashes: {hashesPath}");

            var globalReplacements = HashMappingLoader.LoadReplacementsFromJson(hashesPath);
            var characterReplacements = HashMappingLoader.LoadCharacterReplacementsFromJson(hashesPath);

            Log($"Loaded {globalReplacements.Count} global replacement rules.");
            Log($"Loaded character-specific replacements for {characterReplacements.Count} characters.");

            var processor = new ModIniProcessor(globalReplacements, characterReplacements);

            Log("Scanning .ini files...");
            var (summary, plans) = await Task.Run(() => processor.Preview(folder, _uniqueHashToCharacter));

            Log($"Found eligible .ini files: {summary.IniFilesFound}");
            Log($"Files needing changes: {summary.IniFilesChanged}");
            Log($"Total replacements (estimated): {summary.TotalReplacements}");

            if (summary.AnyChange)
            {
                // ---------------------------------------------------------
                // Log every unique hash mapping that actually changed.
                // ---------------------------------------------------------
                var hashMappings = plans
                    .SelectMany(p => p.HashMappings)
                    .Distinct()
                    .ToList();

                foreach (var mapping in hashMappings)
                {
                    string oldHash = ExtractHashForLog(mapping.OldValue);
                    string newHash = ExtractHashForLog(mapping.NewValue);

                    if (string.IsNullOrWhiteSpace(oldHash) ||
                        string.IsNullOrWhiteSpace(newHash))
                    {
                        continue;
                    }

                    if (string.Equals(
                            oldHash,
                            newHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Log(
                        $"Hash: {oldHash} -> {newHash}");
                }

                Log("Applying hash replacements (creating backups first)...");

                var indexMappings =
                    await Task.Run(() =>
                        processor.ExecutePlans(
                            plans,
                            BackupType.HashReplacement));

                // ---------------------------------------------------------
                // Log YangyangSP index changes.
                // ---------------------------------------------------------
                foreach (var mapping in indexMappings)
                {
                    Log(
                        $"Index: Component {mapping.Component} | " +
                        $"{mapping.Property} | " +
                        $"{mapping.OldValue} -> {mapping.NewValue}");
                }

                Log("Hash replacement completed.");
            }
            else
            {
                Log("Hash replacement: no changes needed.");
            }
        }

        private async Task RunStableTexturesInjection(string folder)
        {
            var stableHashesPath = Path.Combine(AppContext.BaseDirectory, "Assets JSON", "StableHashes.json");
            Log($"Loading StableHashes: {stableHashesPath}");

            var stable = StableHashesLoader.Load(stableHashesPath);
            var injector = new StableTexturesInjector(stable);

            Log("Scanning mod.ini files for StableTextures injection...");
            var (injSummary, injPlans) = await Task.Run(() => injector.Preview(folder));

            Log($"StableTextures - mod.ini scanned: {injSummary.ModIniFound}");
            Log($"StableTextures - files changed: {injSummary.ModIniChanged}");
            Log($"StableTextures - components applied: {injSummary.TotalComponentsApplied}");
            Log($"StableTextures - files skipped (already applied): {injSummary.FilesSkippedAlreadyApplied}");

            if (injSummary.AnyChange)
            {
                Log("Applying StableTextures injection (creating backups first)...");

                await Task.Run(() => injector.ExecutePlans(injPlans));

                Log("StableTextures injection completed.");

                // Remove CheckTextureOverride = ps-t0 through ps-t5 only.
                // Keep ps-t6 and ps-t7, and keep ps-t8 if present.
                // This runs only after StableTextures was successfully applied.
                int removedCheckTextureOverrideLines =
                    RemoveStableTextureCheckOverrides(folder);

                if (removedCheckTextureOverrideLines > 0)
                {
                    Log(
                        $"Removed {removedCheckTextureOverrideLines} " +
                        "CheckTextureOverride = ps-t0 through ps-t5 lines; preserved ps-t6, ps-t7, and ps-t8.");
                }
                else
                {
                    Log("No removable CheckTextureOverride = ps-t0 through ps-t5 lines found.");
                }
            }
            else
            {
                Log("StableTextures injection: no changes needed.");
            }
        }

        private int RemoveStableTextureCheckOverrides(string folder)
        {
            int totalRemoved = 0;

            var iniFiles = Directory
                .GetFiles(
                    folder,
                    "*.ini",
                    SearchOption.AllDirectories)
                .Where(file =>
                    !Path.GetFileName(file)
                        .Contains(
                            "disabled",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string iniFile in iniFiles)
            {
                string[] lines = File.ReadAllLines(iniFile);

                var filteredLines = lines
                    .Where(line =>
                    {
                        string trimmed = line.Trim();

                        // Remove only ps-t0 through ps-t5.
                        // Preserve ps-t6, ps-t7, and ps-t8 (if present).
                        for (int i = 0; i <= 5; i++)
                        {
                            if (string.Equals(
                                trimmed,
                                $"CheckTextureOverride = ps-t{i}",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                totalRemoved++;
                                return false;
                            }
                        }

                        return true;
                    })
                    .ToArray();

                if (filteredLines.Length != lines.Length)
                {
                    File.WriteAllLines(
                        iniFile,
                        filteredLines);
                }
            }

            return totalRemoved;
        }
        private async Task RunHighLodInjection(string folder)
        {
            var highLodHashesPath = Path.Combine(AppContext.BaseDirectory, "Assets JSON", "HighLodHashes.json");
            Log($"Loading HighLodHashes: {highLodHashesPath}");

            var highLodHashes = HighLodHashesLoader.Load(highLodHashesPath);
            var injector = new HighLodHashesInjector(highLodHashes);

            Log("High LOD Fix enabled. Scanning mod.ini files for supported characters...");
            var (lodSummary, lodPlans) = await Task.Run(() => injector.Preview(folder));

            Log($"High LOD Fix - mod.ini scanned: {lodSummary.ModIniFound}");
            Log($"High LOD Fix - supported mod.ini found: {lodSummary.SupportedFound}");
            Log($"High LOD Fix - files changed (planned): {lodSummary.ModIniChanged}");
            Log($"High LOD Fix - files already had LOD HIGH: {lodSummary.FilesAlreadyHadLodHigh}");

            if (lodSummary.SupportedFound <= 0)
            {
                System.Windows.MessageBox.Show(
                    "This LOD Fix is only for Aemeath, Lynae, Mornye & Luuk",
                    "Woju");
                Log("High LOD Fix canceled: no supported characters found.");
                return;
            }

            if (lodSummary.AnyChange)
            {
                Log("Applying High LOD Fix (creating backups first)...");
                await Task.Run(() => injector.ExecutePlans(lodPlans));
                Log("High LOD Fix completed.");
            }
            else
            {
                Log("High LOD Fix: no changes needed.");
            }
        }

        private async void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            RevertButton.IsEnabled = false;
            RunButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;

            try
            {
                var folder = FolderTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show("Please select a valid folder first.", "Woju");
                    return;
                }

                Log("Starting revert process...");

                var manifests = Directory.GetFiles(folder, "backup.txt", SearchOption.AllDirectories);
                if (manifests.Length == 0)
                {
                    Log("No backup.txt manifests found. Nothing to revert.");
                    return;
                }

                int reverted = 0;
                int failedManifests = 0;

                // Each manifest preserves backup creation order. The FIRST backup for
                // each original filename is the state before any fixer operation.
                // Restore that one only, then remove the other listed backups.
                foreach (var manifest in manifests)
                {
                    try
                    {
                        reverted += RestoreManifestFiles(manifest);
                    }
                    catch (Exception ex)
                    {
                        failedManifests++;
                        Log($"Error processing manifest {Path.GetFileName(Path.GetDirectoryName(manifest))}: {ex.Message}");
                    }
                }

                Log($"Revert completed. {reverted} .ini file(s) reverted; {failedManifests} manifest(s) failed.");
                System.Windows.MessageBox.Show(
                    $"Revert completed.\\n{reverted} .ini file(s) reverted.\\nFailed manifests: {failedManifests}",
                    "Woju",
                    MessageBoxButton.OK,
                    failedManifests > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Woju");
                Log("ERROR during revert: " + ex);
            }
            finally
            {
                RevertButton.IsEnabled = true;
                RunButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Document.Blocks.Clear();
            Log("Log cleared.");
        }
        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logText = new TextRange(
                    LogTextBox.Document.ContentStart,
                    LogTextBox.Document.ContentEnd).Text;

                if (string.IsNullOrWhiteSpace(logText))
                {
                    System.Windows.MessageBox.Show(
                        "There are no logs to copy.",
                        "Woju");

                    return;
                }

                System.Windows.Clipboard.SetText(logText);

                System.Windows.MessageBox.Show(
                    "Logs copied to clipboard.",
                    "Woju",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to copy logs.\n\n{ex.Message}",
                    "Woju",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void BrowseWojuModsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://www.youtube.com/@Woju25");
        }

        private void DownloadRabbitFxButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://gamebanana.com/mods/527815");
        }

        private void WojuKofiButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ko-fi.com/wo_ju");
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        private void RunInvisibleFix(string folder)
        {
            try
            {
                Log("Running Invisible Mod Fix...");

                foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file).ToLowerInvariant();

                    if (name == "color.buf")
                    {
                        FixColorBuf(file);
                    }
                    else if (name == "texcoord.buf")
                    {
                        FixTexcoordBuf(file);
                    }
                }

                Log("Invisible Mod Fix completed.");
            }
            catch (Exception ex)
            {
                Log($"Invisible Mod Fix failed: {ex.Message}");
            }
        }

        private void FixColorBuf(string filePath)
        {
            byte[] original = File.ReadAllBytes(filePath);
            byte[] data = (byte[])original.Clone();

            string backupPath =
                BackupManager.CreateBackupPath(filePath, BackupType.InvisibleFix);

            BackupManager.CreateBackupAndRegister(filePath, backupPath);

            byte[] replacement = { 0xFF, 0xBC, 0xBC, 0x33 };

            for (int i = 0; i <= data.Length - 4; i += 4)
            {
                if (data[i] == 0xFF &&
                    data[i + 1] == 0xFF &&
                    data[i + 2] == 0xFF &&
                    data[i + 3] == 0xFF)
                {
                    data[i] = replacement[0];
                    data[i + 1] = replacement[1];
                    data[i + 2] = replacement[2];
                    data[i + 3] = replacement[3];
                }
            }

            File.WriteAllBytes(filePath, data);
        }

        private void FixTexcoordBuf(string filePath)
        {
            byte[] original = File.ReadAllBytes(filePath);
            byte[] data = (byte[])original.Clone();

            string backupPath =
                BackupManager.CreateBackupPath(filePath, BackupType.InvisibleFix);

            BackupManager.CreateBackupAndRegister(filePath, backupPath);

            for (int i = 0; i <= data.Length - 4; i += 4)
            {
                if (data[i] == 0xFF &&
                    data[i + 1] == 0xFF &&
                    data[i + 2] == 0xFF &&
                    data[i + 3] == 0xFF)
                {
                    data[i] = 0x00;
                    data[i + 1] = 0x00;
                    data[i + 2] = 0x00;
                    data[i + 3] = 0x00;
                }
            }

            File.WriteAllBytes(filePath, data);


        }
        private int RestoreManifestFiles(string manifestPath)
        {
            string directory = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException("Backup manifest directory could not be resolved.");

            if (!File.Exists(manifestPath))
                return 0;

            var backupEntries = File.ReadAllLines(manifestPath)
                .Skip(2)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            if (backupEntries.Count == 0)
            {
                File.Delete(manifestPath);
                Log($"Revert: empty backup manifest removed in {Path.GetFileName(directory)}.");
                return 0;
            }

            // Group manifest entries by their original filename while preserving
            // the manifest order. The first entry for each original is the
            // pre-fix backup and must be the one restored.
            var orderedGroups = new List<(string OriginalName, List<string> BackupNames)>();
            var groupLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (string backupName in backupEntries)
            {
                int markerIndex = backupName.IndexOf(
                    ".BAK.woju",
                    StringComparison.OrdinalIgnoreCase);

                if (markerIndex <= 0)
                    continue;

                string originalName = backupName.Substring(0, markerIndex);

                if (!groupLookup.TryGetValue(originalName, out int groupIndex))
                {
                    groupIndex = orderedGroups.Count;
                    groupLookup.Add(originalName, groupIndex);
                    orderedGroups.Add((originalName, new List<string>()));
                }

                orderedGroups[groupIndex].BackupNames.Add(backupName);
            }

            if (orderedGroups.Count == 0)
                throw new InvalidDataException("No valid backup filenames were found in backup.txt. Backups were left untouched.");

            // Validate every first/original backup before changing any target.
            foreach (var group in orderedGroups)
            {
                string firstBackupName = group.BackupNames[0];
                string firstBackupPath = Path.Combine(directory, firstBackupName);

                if (!File.Exists(firstBackupPath))
                {
                    throw new FileNotFoundException(
                        $"The first (original) backup listed in backup.txt is missing: {firstBackupName}. No backups were deleted.");
                }
            }

            int restoredIniCount = 0;

            // Restore only the FIRST backup for each original file.
            foreach (var group in orderedGroups)
            {
                string firstBackupPath = Path.Combine(directory, group.BackupNames[0]);
                string originalPath = Path.Combine(directory, group.OriginalName);

                string tempPath = originalPath + ".revert.tmp";

                try
                {
                    File.Copy(firstBackupPath, tempPath, overwrite: true);

                    // Verify the temporary restored copy before replacing the target.
                    var sourceInfo = new FileInfo(firstBackupPath);
                    var tempInfo = new FileInfo(tempPath);

                    if (sourceInfo.Length != tempInfo.Length)
                        throw new IOException($"Verification failed while restoring {group.OriginalName}: file sizes differ.");

                    File.Copy(tempPath, originalPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }

                if (string.Equals(Path.GetExtension(group.OriginalName), ".ini", StringComparison.OrdinalIgnoreCase))
                    restoredIniCount++;

                Log($"Reverted original: {group.OriginalName}");
            }

            // Only clean up after every original file has been restored successfully.
            // Delete all backups listed in the manifest, not just the first ones.
            foreach (string backupName in backupEntries)
            {
                int markerIndex = backupName.IndexOf(
                    ".BAK.woju",
                    StringComparison.OrdinalIgnoreCase);

                if (markerIndex <= 0)
                    continue;

                string backupPath = Path.Combine(directory, backupName);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }

            // Remove manifest last, after successful restore and backup cleanup.
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);

            Log($"Revert cleanup completed: backups and backup.txt removed in {Path.GetFileName(directory)}.");
            return restoredIniCount;
        }

        private void Log(string message)
        {
            var paragraph = new System.Windows.Documents.Paragraph
            {
                Margin = new System.Windows.Thickness(0)
            };

            // Timestamp - white
            paragraph.Inlines.Add(
                new System.Windows.Documents.Run(
                    $"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = System.Windows.Media.Brushes.White
                });

            // ---------------------------------------------------------
            // Detected Character
            // "Detected Character:" = white
            // Character name       = yellow
            // ---------------------------------------------------------
            const string modDetectedPrefix = "Mod Detected:";

            if (string.Equals(
                    message,
                    "User chose to NOT patch the OLD .ini",
                    StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new System.Windows.Documents.Run("User chose to "));
                paragraph.Inlines.Add(new System.Windows.Documents.Run("NOT")
                {
                    Foreground = System.Windows.Media.Brushes.Red,
                    FontWeight = FontWeights.Bold
                });
                paragraph.Inlines.Add(new System.Windows.Documents.Run(" patch the OLD .ini")
                {
                    Foreground = System.Windows.Media.Brushes.White
                });
            }
            else if (string.Equals(
                    message,
                    "User chose to PATCH the OLD .ini",
                    StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new System.Windows.Documents.Run("User chose to "));
                paragraph.Inlines.Add(new System.Windows.Documents.Run("PATCH")
                {
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 211, 102)),
                    FontWeight = FontWeights.Bold
                });
                paragraph.Inlines.Add(new System.Windows.Documents.Run(" the OLD .ini")
                {
                    Foreground = System.Windows.Media.Brushes.White
                });
            }
            else if (message.StartsWith(
                    modDetectedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                paragraph.Inlines.Add(
                    new System.Windows.Documents.Run(
                        modDetectedPrefix + " ")
                    {
                        Foreground = System.Windows.Media.Brushes.White
                    });

                string modName =
                    message.Substring(modDetectedPrefix.Length).Trim();

                paragraph.Inlines.Add(
                    new System.Windows.Documents.Run(modName)
                    {
                        Foreground = System.Windows.Media.Brushes.Yellow
                    });
            }
            else
            {
                System.Windows.Media.Brush messageColor =
                    System.Windows.Media.Brushes.White;

                // -----------------------------------------------------
                // Patch changes = green
                // -----------------------------------------------------
                if (message.StartsWith("PATCH:", StringComparison.OrdinalIgnoreCase))
                {
                    messageColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 211, 102));
                }
                // -----------------------------------------------------
                // Hash = Cyan
                // -----------------------------------------------------
                else if (message.StartsWith(
                        "Hash:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    messageColor =
                        System.Windows.Media.Brushes.Cyan;
                }

                // -----------------------------------------------------
                // Index = Hot Pink
                // -----------------------------------------------------
                else if (message.StartsWith(
                             "Index:",
                             StringComparison.OrdinalIgnoreCase))
                {
                    messageColor =
                        System.Windows.Media.Brushes.HotPink;
                }

                // -----------------------------------------------------
                // VG = WhatsApp Green
                // -----------------------------------------------------
                else if (message.StartsWith(
                             "VG:",
                             StringComparison.OrdinalIgnoreCase))
                {
                    messageColor =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                37,
                                211,
                                102));
                }

                paragraph.Inlines.Add(
                    new System.Windows.Documents.Run(message)
                    {
                        Foreground = messageColor
                    });
            }

            LogTextBox.Document.Blocks.Add(paragraph);
            LogTextBox.ScrollToEnd();
        }
        private void OpenDeveloperMode()
        {
            var devWindow = new DeveloperModeWindow();
            devWindow.Owner = this;
            devWindow.ShowDialog();
        }

        private static bool IsModernWWMITools(string iniContent)
        {
            string[] modernVersions =
            {
        "WWMI Tools Blender addon v1.5.1",
        "WWMI Tools Blender addon v1.5.3",
        "WWMI Tools Blender addon v1.6.0",
        "WWMI Tools Blender addon v1.7.1",
        "WWMI Tools Blender addon v1.7.2",
        "WWMI Tools Blender addon v1.7.3"
    };

            return modernVersions.Any(version =>
                iniContent.Contains(
                    version,
                    StringComparison.OrdinalIgnoreCase));
        }
        private static string ExtractHashForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();

            const string prefix = "hash =";

            if (value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(prefix.Length).Trim();
            }

            return value;
        }
    }
}