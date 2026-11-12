using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed class DerivedHashRule
    {
        public string CharacterHash { get; set; } = "";

        public List<DerivedHashPair> DerivedHashes { get; set; } = new();
    }

    public sealed class DerivedHashPair
    {
        public string SourceHash { get; set; } = "";

        public string DerivedHash { get; set; } = "";
    }

    public static class DerivedHashFixer
    {
        private const string DerivedHeader =
            "; =====================WOJU DERIVED HASH=====================";

        // =========================================================
        // MAIN ENTRY
        // =========================================================

        public static void Apply(
            string rootFolder,
            string detectedCharacter,
            Action<string>? log)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) ||
                !Directory.Exists(rootFolder))
            {
                Log(
                    log,
                    "Derived Hash Fix skipped: invalid mod folder.");

                return;
            }

            if (string.IsNullOrWhiteSpace(detectedCharacter))
            {
                Log(
                    log,
                    "Derived Hash Fix skipped: character not detected.");

                return;
            }

            string jsonPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Derived.json");

            if (!File.Exists(jsonPath))
            {
                Log(
                    log,
                    "Derived Hash Fix skipped: Derived.json not found.");

                return;
            }

            Dictionary<string, DerivedHashRule>? rules;

            try
            {
                string json =
                    File.ReadAllText(
                        jsonPath,
                        Encoding.UTF8);

                rules =
                    JsonSerializer.Deserialize<
                        Dictionary<string, DerivedHashRule>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
            }
            catch (Exception ex)
            {
                Log(
                    log,
                    $"Derived Hash Fix failed to load Derived.json: {ex.Message}");

                return;
            }

            if (rules == null ||
                rules.Count == 0)
            {
                Log(
                    log,
                    "Derived Hash Fix skipped: no rules found.");

                return;
            }

            // -----------------------------------------------------
            // Find the rule for the detected character.
            // The JSON key is a label such as Aemeath35.
            // -----------------------------------------------------

            DerivedHashRule? rule = null;

            foreach (var entry in rules)
            {
                string key =
                    entry.Key;

                DerivedHashRule candidate =
                    entry.Value;

                if (string.Equals(
                    key,
                    detectedCharacter,
                    StringComparison.OrdinalIgnoreCase))
                {
                    rule = candidate;
                    break;
                }

                if (key.StartsWith(
                    detectedCharacter,
                    StringComparison.OrdinalIgnoreCase))
                {
                    rule = candidate;
                    break;
                }
            }

            if (rule == null)
            {
                Log(
                    log,
                    $"Derived Hash Fix skipped: no rule for {detectedCharacter}.");

                return;
            }

            if (rule.DerivedHashes == null ||
                rule.DerivedHashes.Count == 0)
            {
                Log(
                    log,
                    $"Derived Hash Fix skipped: no derived hashes configured for {detectedCharacter}.");

                return;
            }

            Log(
                log,
                $"Derived Hash Fix: {detectedCharacter}");

            // -----------------------------------------------------
            // Scan ALL eligible .ini files.
            // -----------------------------------------------------

            string[] iniFiles =
                Directory.GetFiles(
                        rootFolder,
                        "*.ini",
                        SearchOption.AllDirectories)
                    .Where(file =>
                        !Path.GetFileName(file)
                            .Contains(
                                "disabled",
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (iniFiles.Length == 0)
            {
                Log(
                    log,
                    "Derived Hash Fix skipped: no eligible .ini files found.");

                return;
            }

            // -----------------------------------------------------
            // Keep current text in memory.
            // -----------------------------------------------------

            var fileContents =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string iniFile in iniFiles)
            {
                fileContents[iniFile] =
                    File.ReadAllText(
                        iniFile,
                        Encoding.UTF8);
            }

            // -----------------------------------------------------
            // Track which files were actually modified.
            // -----------------------------------------------------

            var modifiedFiles =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            // -----------------------------------------------------
            // Store all derived blocks separately.
            //
            // Key   = .ini file
            // Value = all derived blocks that will be appended
            //         to the bottom of that file.
            // -----------------------------------------------------

            var derivedBlocks =
                new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase);

            // -----------------------------------------------------
            // Process every SourceHash -> DerivedHash pair.
            // -----------------------------------------------------

            foreach (DerivedHashPair pair in
                     rule.DerivedHashes)
            {
                ApplyPair(
                    fileContents,
                    modifiedFiles,
                    derivedBlocks,
                    pair,
                    log);
            }

            // -----------------------------------------------------
            // Append all derived blocks to the bottom of each file.
            // -----------------------------------------------------

            foreach (var entry in derivedBlocks)
            {
                string iniFile =
                    entry.Key;

                List<string> blocks =
                    entry.Value;

                if (blocks.Count == 0)
                    continue;

                string originalText =
                    fileContents[iniFile];

                string updatedText =
                    AppendDerivedBlocks(
                        originalText,
                        blocks);

                if (string.Equals(
                    originalText,
                    updatedText,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                fileContents[iniFile] =
                    updatedText;

                modifiedFiles.Add(
                    iniFile);
            }

            // -----------------------------------------------------
            // Create backups and write modified .ini files.
            //
            // IMPORTANT:
            // Backup is created BEFORE the first modification.
            // The backup uses the DERIVED naming type.
            // -----------------------------------------------------

            foreach (string iniFile in modifiedFiles)
            {
                string backupPath =
                    BackupManager.CreateBackupPath(
                        iniFile,
                        BackupType.DerivedHash);

                BackupManager.CreateBackupAndRegister(
                    iniFile,
                    backupPath);

                Log(
                    log,
                    $"  Backup created: {Path.GetFileName(backupPath)}");

                File.WriteAllText(
                    iniFile,
                    fileContents[iniFile],
                    Encoding.UTF8);
            }

            Log(
                log,
                "Derived Hash Fix completed.");
        }

        // =========================================================
        // APPLY ONE SOURCE -> DERIVED PAIR
        // =========================================================

        private static void ApplyPair(
            Dictionary<string, string> fileContents,
            HashSet<string> modifiedFiles,
            Dictionary<string, List<string>> derivedBlocks,
            DerivedHashPair pair,
            Action<string>? log)
        {
            string sourceHash =
                NormalizeHash(pair.SourceHash);

            string derivedHash =
                NormalizeHash(pair.DerivedHash);

            if (string.IsNullOrWhiteSpace(sourceHash) ||
                string.IsNullOrWhiteSpace(derivedHash))
            {
                return;
            }

            // -----------------------------------------------------
            // SAFEGUARD:
            //
            // If the DerivedHash already exists anywhere in the
            // current mod, skip this pair.
            // -----------------------------------------------------

            foreach (var entry in fileContents)
            {
                if (ContainsHash(
                    entry.Value,
                    derivedHash))
                {
                    Log(
                        log,
                        $"  Derived hash already exists: {derivedHash}");

                    return;
                }
            }

            // Also check derived blocks that have already been
            // generated during this run.
            foreach (var entry in derivedBlocks)
            {
                foreach (string block in entry.Value)
                {
                    if (ContainsHash(
                        block,
                        derivedHash))
                    {
                        Log(
                            log,
                            $"  Derived hash already generated: {derivedHash}");

                        return;
                    }
                }
            }

            // -----------------------------------------------------
            // Search for SourceHash.
            //
            // IMPORTANT:
            // fileContents itself is NOT modified here.
            // Therefore every derived block is generated from
            // the original source section.
            // -----------------------------------------------------

            foreach (var entry in fileContents)
            {
                string iniFile =
                    entry.Key;

                string text =
                    entry.Value;

                if (!ContainsHash(
                    text,
                    sourceHash))
                {
                    continue;
                }

                if (TryCreateDerivedSection(
                    text,
                    sourceHash,
                    derivedHash,
                    derivedBlocks,
                    iniFile,
                    out string derivedBlock,
                    out string sourceSectionName,
                    out string derivedSectionName))
                {
                    if (!derivedBlocks.TryGetValue(
                        iniFile,
                        out List<string>? blocks))
                    {
                        blocks =
                            new List<string>();

                        derivedBlocks[iniFile] =
                            blocks;
                    }

                    blocks.Add(
                        derivedBlock);

                    modifiedFiles.Add(
                        iniFile);

                    Log(
                        log,
                        $"  Derived: {sourceSectionName} -> {derivedSectionName}");

                    Log(
                        log,
                        $"  Hash: {sourceHash} -> {derivedHash}");

                    return;
                }
            }

            // -----------------------------------------------------
            // SourceHash was not found.
            // -----------------------------------------------------

            Log(
                log,
                $"  Source hash not found: {sourceHash}");
        }

        // =========================================================
        // FIND AND CLONE SOURCE SECTION
        // =========================================================

        private static bool TryCreateDerivedSection(
            string text,
            string sourceHash,
            string derivedHash,
            Dictionary<string, List<string>> derivedBlocks,
            string currentIniFile,
            out string derivedBlock,
            out string sourceSectionName,
            out string derivedSectionName)
        {
            derivedBlock =
                "";

            sourceSectionName =
                "";

            derivedSectionName =
                "";

            string normalizedText =
                text.Replace(
                        "\r\n",
                        "\n")
                    .Replace(
                        '\r',
                        '\n');

            string[] lines =
                normalizedText.Split(
                    '\n');

            for (int i = 0;
                 i < lines.Length;
                 i++)
            {
                string trimmed =
                    lines[i].Trim();

                // -------------------------------------------------
                // Section header
                // -------------------------------------------------

                if (!trimmed.StartsWith(
                        "[",
                        StringComparison.Ordinal) ||
                    !trimmed.EndsWith(
                        "]",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int sectionStart =
                    i;

                int sectionEnd =
                    lines.Length;

                // -------------------------------------------------
                // Find next section.
                // -------------------------------------------------

                for (int j = i + 1;
                     j < lines.Length;
                     j++)
                {
                    string next =
                        lines[j].Trim();

                    if (next.StartsWith(
                            "[",
                            StringComparison.Ordinal) &&
                        next.EndsWith(
                            "]",
                            StringComparison.Ordinal))
                    {
                        sectionEnd =
                            j;

                        break;
                    }
                }

                // -------------------------------------------------
                // Check whether section contains SourceHash.
                // -------------------------------------------------

                bool containsSourceHash =
                    false;

                for (int j = sectionStart;
                     j < sectionEnd;
                     j++)
                {
                    if (IsHashLine(
                        lines[j],
                        sourceHash))
                    {
                        containsSourceHash =
                            true;

                        break;
                    }
                }

                if (!containsSourceHash)
                {
                    continue;
                }

                // -------------------------------------------------
                // Get original section name.
                // -------------------------------------------------

                sourceSectionName =
                    trimmed.Substring(
                        1,
                        trimmed.Length - 2);

                // -------------------------------------------------
                // Determine next available derived name.
                //
                // Check both the original file and derived blocks
                // that have already been generated during this run.
                // -------------------------------------------------

                var existingSectionNames =
                    GetExistingSectionNames(
                        lines);

                if (derivedBlocks.TryGetValue(
                    currentIniFile,
                    out List<string>? existingBlocks))
                {
                    foreach (string block in existingBlocks)
                    {
                        AddSectionNames(
                            block,
                            existingSectionNames);
                    }
                }

                derivedSectionName =
                    GetNextDerivedSectionName(
                        existingSectionNames,
                        sourceSectionName);

                // -------------------------------------------------
                // Clone original section.
                // -------------------------------------------------

                var clonedLines =
                    new List<string>();

                for (int j = sectionStart;
                     j < sectionEnd;
                     j++)
                {
                    string line =
                        lines[j];

                    // -------------------------------------------------
                    // Replace section name.
                    // -------------------------------------------------

                    if (j == sectionStart)
                    {
                        clonedLines.Add(
                            $"[{derivedSectionName}]");

                        continue;
                    }

                    // -------------------------------------------------
                    // Replace ONLY the hash line.
                    //
                    // Everything else remains untouched.
                    // -------------------------------------------------

                    if (IsHashLine(
                        line,
                        sourceHash))
                    {
                        string indentation =
                            line.Substring(
                                0,
                                line.Length -
                                line.TrimStart().Length);

                        clonedLines.Add(
                            $"{indentation}hash = {derivedHash}");

                        continue;
                    }

                    clonedLines.Add(
                        line);
                }

                // -------------------------------------------------
                // The cloned section is returned as a standalone
                // block. It is NOT inserted beside the source.
                // -------------------------------------------------

                derivedBlock =
                    string.Join(
                        "\n",
                        clonedLines);

                return true;
            }

            return false;
        }

        // =========================================================
        // APPEND ALL DERIVED BLOCKS TO FILE BOTTOM
        // =========================================================

        private static string AppendDerivedBlocks(
            string originalText,
            List<string> blocks)
        {
            if (blocks.Count == 0)
                return originalText;

            string normalizedText =
                originalText.Replace(
                        "\r\n",
                        "\n")
                    .Replace(
                        '\r',
                        '\n');

            // -----------------------------------------------------
            // Remove only trailing blank lines temporarily.
            // -----------------------------------------------------

            string body =
                normalizedText.TrimEnd(
                    '\n');

            var builder =
                new StringBuilder();

            builder.Append(
                body);

            builder.Append(
                "\n\n");

            // -----------------------------------------------------
            // Add separator only once.
            // -----------------------------------------------------

            if (!ContainsDerivedHeader(
                normalizedText))
            {
                builder.Append(
                    DerivedHeader);

                builder.Append(
                    "\n");
            }

            // -----------------------------------------------------
            // Append all derived blocks.
            // -----------------------------------------------------

            foreach (string block in blocks)
            {
                builder.Append(
                    block);

                builder.Append(
                    "\n");
            }

            return builder
                .ToString()
                .TrimEnd('\n');
        }

        // =========================================================
        // EXISTING SECTION NAMES
        // =========================================================

        private static HashSet<string> GetExistingSectionNames(
            string[] lines)
        {
            var names =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string line in lines)
            {
                string trimmed =
                    line.Trim();

                if (!trimmed.StartsWith(
                        "[",
                        StringComparison.Ordinal) ||
                    !trimmed.EndsWith(
                        "]",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string name =
                    trimmed.Substring(
                        1,
                        trimmed.Length - 2);

                names.Add(
                    name);
            }

            return names;
        }

        private static void AddSectionNames(
            string block,
            HashSet<string> names)
        {
            string normalizedBlock =
                block.Replace(
                        "\r\n",
                        "\n")
                    .Replace(
                        '\r',
                        '\n');

            foreach (string line in
                     normalizedBlock.Split('\n'))
            {
                string trimmed =
                    line.Trim();

                if (!trimmed.StartsWith(
                        "[",
                        StringComparison.Ordinal) ||
                    !trimmed.EndsWith(
                        "]",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string name =
                    trimmed.Substring(
                        1,
                        trimmed.Length - 2);

                names.Add(
                    name);
            }
        }

        // =========================================================
        // DERIVED SECTION NAME
        // =========================================================

        private static string GetNextDerivedSectionName(
            HashSet<string> existingSectionNames,
            string sourceSectionName)
        {
            string baseName =
                sourceSectionName +
                "_Derived";

            // -----------------------------------------------------
            // First choice:
            //
            // TextureOverrideTexture10_Derived
            // -----------------------------------------------------

            if (!existingSectionNames.Contains(
                baseName))
            {
                return baseName;
            }

            // -----------------------------------------------------
            // Additional choices:
            //
            // TextureOverrideTexture10_Derived_2
            // TextureOverrideTexture10_Derived_3
            // ...
            // -----------------------------------------------------

            int number =
                2;

            while (true)
            {
                string candidate =
                    $"{baseName}_{number}";

                if (!existingSectionNames.Contains(
                    candidate))
                {
                    return candidate;
                }

                number++;
            }
        }

        // =========================================================
        // CHECK DERIVED HEADER
        // =========================================================

        private static bool ContainsDerivedHeader(
            string text)
        {
            return text.IndexOf(
                DerivedHeader,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // =========================================================
        // HASH CHECK
        // =========================================================

        private static bool ContainsHash(
            string text,
            string hash)
        {
            string normalizedHash =
                NormalizeHash(hash);

            string[] lines =
                text.Split(
                    new[]
                    {
                        '\r',
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                if (IsHashLine(
                    line,
                    normalizedHash))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHashLine(
            string line,
            string hash)
        {
            string trimmed =
                line.Trim();

            const string prefix =
                "hash =";

            if (!trimmed.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string value =
                trimmed.Substring(
                    prefix.Length)
                .Trim();

            return string.Equals(
                NormalizeHash(value),
                NormalizeHash(hash),
                StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // HASH NORMALIZATION
        // =========================================================

        private static string NormalizeHash(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            value =
                value.Trim();

            const string prefix =
                "hash =";

            if (value.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            {
                value =
                    value.Substring(
                        prefix.Length)
                    .Trim();
            }

            return value;
        }

        // =========================================================
        // LOG
        // =========================================================

        private static void Log(
            Action<string>? log,
            string message)
        {
            log?.Invoke(
                message);
        }
    }
}