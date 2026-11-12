using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WuWa_MOD_WojuFixer.Core
{
    public static class VertexGroupRemapper
    {
        // ============================================================
        // LYNAE COMPONENT 3 LOCAL VG MAPPING
        // OLD Component 3.005 -> NEW Component 3.006
        // ============================================================

        private static readonly IReadOnlyDictionary<byte, byte>
            LynaeComponentMapping =
                new Dictionary<byte, byte>
                {
                    [26] = 27,
                    [27] = 28,
                    [28] = 29,
                    [29] = 30,
                    [30] = 31,
                    [31] = 32,
                    [32] = 33,
                    [33] = 34,
                    [34] = 35,
                    [35] = 36,
                    [36] = 26,

                    [48] = 49,
                    [49] = 50,
                    [50] = 51,
                    [51] = 52,
                    [52] = 53,
                    [53] = 54,
                    [54] = 55,
                    [55] = 56,
                    [56] = 57,
                    [57] = 58,
                    [58] = 59,
                    [59] = 60,
                    [60] = 61,
                    [61] = 62,
                    [62] = 63,
                    [63] = 64,

                    [64] = 66,
                    [65] = 67,

                    [66] = 68,
                    [67] = 69,
                    [68] = 70,
                    [69] = 71,
                    [70] = 72,
                    [71] = 73,
                    [72] = 74,
                    [73] = 75,
                    [74] = 76,
                    [75] = 77,
                    [76] = 78,
                    [77] = 79,
                    [78] = 80,
                    [79] = 81,
                    [80] = 82,
                    [81] = 83,
                    [82] = 84,
                    [83] = 85,
                    [84] = 86,
                    [85] = 87,
                    [86] = 88,
                    [87] = 89,
                    [88] = 90,
                    [89] = 91,
                    [90] = 92,
                    [91] = 93,
                    [92] = 94,
                    [93] = 95,
                    [94] = 96,
                    [95] = 97,
                    [96] = 98,
                    [97] = 99,
                    [98] = 100,
                    [99] = 101,
                    [100] = 102,
                    [101] = 103,
                    [102] = 104,
                    [103] = 105,
                    [104] = 106,
                    [105] = 107,

                    [106] = 66,
                    [107] = 108,

                    // IMPORTANT:
                    // Verified manually by testing a per-component mod.
                    [108] = 48
                };


        // ============================================================
        // MERGED SKELETON CONSTANTS
        // ============================================================

        /*
         * Lynae 3.5 and 3.6 use the same merged VG offsets:
         *
         * Component 0 = 0
         * Component 1 = 45
         * Component 2 = 117
         * Component 3 = 118
         * Component 4 = 229
         * Component 5 = 322
         * Component 6 = 370
         * Component 7 = 400
         *
         * IMPORTANT:
         *
         * The Lynae VG changes we are fixing belong to Component 3.
         *
         * Therefore we MUST NOT apply the Component 3 local mapping
         * to every component offset.
         *
         * Only Component 3 is converted into global VG IDs.
         */

        private const ushort LynaeComponent3VgOffset = 118;


        // ============================================================
        // BLEND SETTINGS
        // ============================================================

        private const int BlendStride = 16;

        /*
         * 16-byte Blend vertex:
         *
         * bytes 0..7 = 8 VG indices
         * bytes 8..15 = weights
         */
        private const int BlendIndexBytes = 8;


        // ============================================================
        // PUBLIC ENTRY
        // ============================================================

        public static void ApplyLynaeFix(
            string rootFolder,
            Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) ||
                !Directory.Exists(rootFolder))
            {
                throw new DirectoryNotFoundException(
                    "Selected mod folder does not exist.");
            }

            Log(
                log,
                "Starting Lynae Vertex Group Fix...");


            string[] iniFiles =
                Directory.GetFiles(
                    rootFolder,
                    "*.ini",
                    SearchOption.AllDirectories)
                .Where(IsEligibleIni)
                .ToArray();


            if (iniFiles.Length == 0)
            {
                Log(
                    log,
                    "VG Fix skipped: no eligible .ini files found.");

                return;
            }


            bool isMerged =
                DetectMergedSkeleton(iniFiles);


            Log(
                log,
                isMerged
                    ? "Detected skeleton type: Merged Skeleton."
                    : "Detected skeleton type: Per-Component Skeleton.");


            Log(
                log,
                isMerged
                    ? "Using Lynae Merged VG mapping."
                    : "Using Lynae Per-Component VG mapping.");


            if (isMerged)
            {
                ApplyMergedFix(
                    rootFolder,
                    iniFiles,
                    log);
            }
            else
            {
                ApplyPerComponentFix(
                    rootFolder,
                    iniFiles,
                    log);
            }
        }


        // ============================================================
        // MERGED FIX
        // ============================================================

        private static void ApplyMergedFix(
            string rootFolder,
            string[] iniFiles,
            Action<string>? log)
        {
            var changedMappings =
                new HashSet<(ushort OldValue, ushort NewValue)>();
            /*
             * DO NOT derive the merged mapping from arbitrary INI
             * vg_offset values.
             *
             * Lynae's merged skeleton has a fixed global layout.
             *
             * Only Component 3 changed.
             *
             * Therefore:
             *
             * local old VG + 118 -> local new VG + 118
             */

            Dictionary<ushort, ushort> globalMapping =
                BuildLynaeMergedMapping();



            // ========================================================
            // BlendRemapForward.buf
            // ========================================================

            string[] forwardFiles =
                Directory.GetFiles(
                    rootFolder,
                    "*.buf",
                    SearchOption.AllDirectories)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(path),
                        "BlendRemapForward.buf",
                        StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsBackupFile(path))
                .ToArray();


            // ========================================================
            // Blend.buf
            // ========================================================

            string[] blendFiles =
                Directory.GetFiles(
                    rootFolder,
                    "*.buf",
                    SearchOption.AllDirectories)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(path),
                        "Blend.buf",
                        StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsBackupFile(path))
                .ToArray();


            Log(
                log,
                $"Found {blendFiles.Length} Blend.buf file(s).");


            Log(
                log,
                $"Found {forwardFiles.Length} " +
                "BlendRemapForward.buf file(s).");


            /*
             * IMPORTANT:
             *
             * For merged skeletons Moonholder's implementation
             * modifies:
             *
             *   BlendRemapForward.buf
             *   Blend.buf
             *
             * BlendRemapVertexVG.buf is NOT touched here.
             */


            foreach (string forwardFile in forwardFiles)
            {
                    ProcessMergedForward(
                        forwardFile,
                        globalMapping,
                        changedMappings,
                        log);
                }


            foreach (string blendFile in blendFiles)
            {
                    ProcessMergedBlend(
                        blendFile,
                        globalMapping,
                        changedMappings,
                        log);
                }
                // ---------------------------------------------------------
                // Log only VG mappings that were actually applied.
                // ---------------------------------------------------------
                foreach (var mapping in changedMappings.OrderBy(x => x.OldValue))
                {
                    Log(
                        log,
                        $"VG: {mapping.OldValue} -> {mapping.NewValue}");
                }

                Log(
                log,
                "Lynae Merged Vertex Group Fix completed.");
        }


        // ============================================================
        // BUILD LYNAE MERGED GLOBAL MAPPING
        // ============================================================

        private static Dictionary<ushort, ushort>
            BuildLynaeMergedMapping()
        {
            Dictionary<ushort, ushort> result =
                new();


            foreach (var pair in
                     LynaeComponentMapping)
            {
                int oldGlobal =
                    pair.Key +
                    LynaeComponent3VgOffset;

                int newGlobal =
                    pair.Value +
                    LynaeComponent3VgOffset;


                if (oldGlobal < 0 ||
                    oldGlobal > ushort.MaxValue ||
                    newGlobal < 0 ||
                    newGlobal > ushort.MaxValue)
                {
                    continue;
                }


                result[(ushort)oldGlobal] =
                    (ushort)newGlobal;
            }


            return result;
        }


        // ============================================================
        // MERGED FORWARD
        // ============================================================

        private static void ProcessMergedForward(
            string path,
            IReadOnlyDictionary<ushort, ushort> mapping,
            HashSet<(ushort OldValue, ushort NewValue)> changedMappings,
            Action<string>? log)
        {
            byte[] forwardData =
                File.ReadAllBytes(path);


            const int BlockEntries = 512;
            const int EntrySize = 2;
            const int BlockSize =
                BlockEntries * EntrySize;


            int numBlocks =
                forwardData.Length /
                BlockSize;


            int entryCount =
                forwardData.Length /
                EntrySize;


            int changedEntries = 0;


            for (int block = 0;
                 block < numBlocks;
                 block++)
            {
                int blockOffset =
                    block *
                    BlockSize;


                ushort[] newForward =
                    new ushort[BlockEntries];


                // Read entire block first.
                for (int i = 0;
                     i < BlockEntries;
                     i++)
                {
                    int offset =
                        blockOffset +
                        i * EntrySize;


                    ushort oldValue =
                        BitConverter.ToUInt16(
                            forwardData,
                            offset);


                    newForward[i] =
                        mapping.TryGetValue(
                            oldValue,
                            out ushort newValue)
                            ? newValue
                            : oldValue;
                }


                // Write entire block.
                for (int i = 0;
                     i < BlockEntries;
                     i++)
                {
                    int offset =
                        blockOffset +
                        i * EntrySize;


                    ushort oldValue =
                        BitConverter.ToUInt16(
                            forwardData,
                            offset);


                    ushort newValue =
                        newForward[i];


                    if (oldValue != newValue)
                    {
                        changedEntries++;

                        changedMappings.Add(
                            (oldValue, newValue));
                    }


                    byte[] bytes =
                        BitConverter.GetBytes(
                            newValue);


                    forwardData[offset] =
                        bytes[0];

                    forwardData[offset + 1] =
                        bytes[1];
                }
            }


            Log(
                log,
                $"VG Fix: {Path.GetFileName(path)}");


            Log(
                log,
                $"  Forward entries: " +
                $"{entryCount:N0}");


            Log(
                log,
                $"  Changed Forward entries: " +
                $"{changedEntries:N0}");


            if (changedEntries == 0)
            {
                return;
            }


            string backupPath =
                BackupManager.CreateBackupPath(
                    path,
                    BackupType.VertexGroupFix);


            BackupManager.CreateBackupAndRegister(
                path,
                backupPath);


            File.WriteAllBytes(
                path,
                forwardData);


            Log(
                log,
                $"  Backup created: " +
                $"{Path.GetFileName(backupPath)}");
        }


        // ============================================================
        // MERGED BLEND
        // ============================================================

        private static void ProcessMergedBlend(
            string path,
            IReadOnlyDictionary<ushort, ushort> mapping,
            HashSet<(ushort OldValue, ushort NewValue)> changedMappings,
            Action<string>? log)
        {
            byte[] blendData =
                File.ReadAllBytes(path);


            if (blendData.Length %
                BlendStride != 0)
            {
                throw new InvalidDataException(
                    $"Invalid Blend.buf size: " +
                    $"{blendData.Length}.");
            }


            int vertexCount =
                blendData.Length /
                BlendStride;


            int changedVertices = 0;
            int changedIndices = 0;


            /*
             * For merged skeleton:
             *
             * Process the ENTIRE Blend.buf.
             *
             * Only the first 8 bytes of each 16-byte vertex
             * are VG indices.
             */

            for (int vertex = 0;
                 vertex < vertexCount;
                 vertex++)
            {
                int vertexOffset =
                    vertex *
                    BlendStride;


                bool vertexChanged =
                    false;


                for (int slot = 0;
                     slot < BlendIndexBytes;
                     slot++)
                {
                    int offset =
                        vertexOffset +
                        slot;


                    byte oldValue =
                        blendData[offset];


                    ushort oldGlobal =
                        oldValue;


                    if (!mapping.TryGetValue(
                            oldGlobal,
                            out ushort newGlobal))
                    {
                        continue;
                    }


                    byte newValue =
                        (byte)newGlobal;


                    if (oldValue ==
                        newValue)
                    {
                        continue;
                    }

                    blendData[offset] =
                        newValue;

                    changedMappings.Add(
                        (oldGlobal, newGlobal));

                    changedIndices++;
                    vertexChanged = true;
                }


                if (vertexChanged)
                {
                    changedVertices++;
                }
            }


            Log(
                log,
                $"VG Fix: {Path.GetFileName(path)}");


            Log(
                log,
                $"  Merged Blend vertices: " +
                $"{vertexCount:N0}");


            Log(
                log,
                $"  Changed vertices: " +
                $"{changedVertices:N0}");


            Log(
                log,
                $"  Changed VG indices: " +
                $"{changedIndices:N0}");


            if (changedIndices == 0)
            {
                return;
            }


            string backupPath =
                BackupManager.CreateBackupPath(
                    path,
                    BackupType.VertexGroupFix);


            BackupManager.CreateBackupAndRegister(
                path,
                backupPath);


            File.WriteAllBytes(
                path,
                blendData);


            Log(
                log,
                $"  Backup created: " +
                $"{Path.GetFileName(backupPath)}");
        }


        // ============================================================
        // PER-COMPONENT FIX
        // ============================================================

        private static void ApplyPerComponentFix(
            string rootFolder,
            string[] iniFiles,
            Action<string>? log)
        {
            var changedMappings =
                new HashSet<(byte OldValue, byte NewValue)>();

            List<Component3Info> ranges =
                new();


            foreach (string iniFile in iniFiles)
            {
                string text =
                    File.ReadAllText(
                        iniFile,
                        Encoding.UTF8);


                ranges.AddRange(
                    ParseComponent3Info(
                        text,
                        iniFile));
            }


            if (ranges.Count == 0)
            {
                Log(
                    log,
                    "VG Fix skipped: Component 3 range not found.");

                return;
            }


            Log(
                log,
                $"Found {ranges.Count} " +
                "Component 3 index range(s).");


            foreach (Component3Info range in ranges)
            {
                Log(
                    log,
                    $"  Component 3: first index = " +
                    $"{range.FirstIndex:N0}, count = " +
                    $"{range.IndexCount:N0}");
            }


            string[] blendFiles =
                Directory.GetFiles(
                    rootFolder,
                    "*.buf",
                    SearchOption.AllDirectories)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(path),
                        "Blend.buf",
                        StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsBackupFile(path))
                .ToArray();


            string[] indexFiles =
                Directory.GetFiles(
                    rootFolder,
                    "*.buf",
                    SearchOption.AllDirectories)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(path),
                        "Index.buf",
                        StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsBackupFile(path))
                .ToArray();


            Log(
                log,
                $"Found {blendFiles.Length} Blend.buf file(s).");


            Log(
                log,
                $"Found {indexFiles.Length} Index.buf file(s).");


            foreach (string blendPath in blendFiles)
            {
                string? indexPath =
                    indexFiles.FirstOrDefault(
                        p =>
                            string.Equals(
                                Path.GetDirectoryName(p),
                                Path.GetDirectoryName(
                                    blendPath),
                                StringComparison.OrdinalIgnoreCase));


                if (indexPath == null)
                {
                    continue;
                }


                ProcessPerComponentBlend(
                    blendPath,
                    indexPath,
                    ranges,
                    changedMappings,
                    log);
            }

            // ---------------------------------------------------------
            // Log only VG mappings that were actually applied.
            // ---------------------------------------------------------
            foreach (var mapping in changedMappings.OrderBy(x => x.OldValue))
            {
                Log(
                    log,
                    $"VG: {mapping.OldValue} -> {mapping.NewValue}");
            }
            Log(
                log,
                "Lynae Per-Component Vertex Group Fix completed.");
        }


        private sealed class Component3Info
        {
            public long FirstIndex { get; init; }
            public long IndexCount { get; init; }
            public long VgOffset { get; init; }
            public string SourceIni { get; init; } = "";
        }


        private static void ProcessPerComponentBlend(
            string blendPath,
            string indexPath,
            IReadOnlyList<Component3Info> ranges,
            HashSet<(byte OldValue, byte NewValue)> changedMappings,
            Action<string>? log)
        {
            byte[] blend =
                File.ReadAllBytes(blendPath);


            byte[] index =
                File.ReadAllBytes(indexPath);


            if (blend.Length %
                BlendStride != 0)
            {
                throw new InvalidDataException(
                    "Invalid Blend.buf size.");
            }


            if (index.Length %
                4 != 0)
            {
                throw new InvalidDataException(
                    "Invalid Index.buf size.");
            }


            int vertexCount =
                blend.Length /
                BlendStride;


            long indexCount =
                index.Length / 4;


            HashSet<int> affectedVertices =
                new();


            foreach (Component3Info range in ranges)
            {
                long start =
                    range.FirstIndex;


                long end =
                    start +
                    range.IndexCount;


                if (start < 0 ||
                    end > indexCount)
                {
                    continue;
                }


                for (long i = start;
                     i < end;
                     i++)
                {
                    int offset =
                        checked(
                            (int)(
                                i * 4));


                    uint vertexIndex =
                        BitConverter.ToUInt32(
                            index,
                            offset);


                    if (vertexIndex <
                        vertexCount)
                    {
                        affectedVertices.Add(
                            (int)vertexIndex);
                    }
                }
            }


            byte[] modified =
                (byte[])blend.Clone();


            int changedVertices = 0;
            int changedIndices = 0;


            foreach (int vertexIndex in
                     affectedVertices)
            {
                int vertexOffset =
                    vertexIndex *
                    BlendStride;


                bool changed =
                    false;


                for (int slot = 0;
                     slot < BlendIndexBytes;
                     slot++)
                {
                    int offset =
                        vertexOffset +
                        slot;


                    byte oldValue =
                        blend[offset];


                    if (!LynaeComponentMapping.TryGetValue(
                            oldValue,
                            out byte newValue))
                    {
                        continue;
                    }


if (oldValue == newValue)
{
    continue;
}

modified[offset] =
    newValue;

changedMappings.Add(
    (oldValue, newValue));

changedIndices++;
changed = true;
                }


                if (changed)
                {
                    changedVertices++;
                }
            }


            Log(
                log,
                $"VG Fix: {Path.GetFileName(blendPath)}");


            Log(
                log,
                $"  Component 3 vertices: " +
                $"{affectedVertices.Count:N0}");


            Log(
                log,
                $"  Changed vertices: " +
                $"{changedVertices:N0}");


            Log(
                log,
                $"  Changed VG indices: " +
                $"{changedIndices:N0}");


            if (changedIndices == 0)
            {
                return;
            }


            string backupPath =
                BackupManager.CreateBackupPath(
                    blendPath,
                    BackupType.VertexGroupFix);


            BackupManager.CreateBackupAndRegister(
                blendPath,
                backupPath);


            File.WriteAllBytes(
                blendPath,
                modified);


            Log(
                log,
                $"  Backup created: " +
                $"{Path.GetFileName(backupPath)}");
        }


        // ============================================================
        // COMPONENT 3 PARSER
        // ============================================================

        private static IEnumerable<Component3Info>
            ParseComponent3Info(
                string text,
                string iniPath)
        {
            MatchCollection sections =
                Regex.Matches(
                    text,
                    @"(?ms)^\[TextureOverrideComponent3\]" +
                    @"\s*(?<body>.*?)(?=^\[|\z)",
                    RegexOptions.IgnoreCase |
                    RegexOptions.Multiline |
                    RegexOptions.Singleline);


            foreach (Match section in sections)
            {
                string body =
                    section.Groups["body"].Value;


                Match firstIndexMatch =
                    Regex.Match(
                        body,
                        @"(?im)^\s*" +
                        @"match_first_index\s*=\s*(\d+)");


                Match countMatch =
                    Regex.Match(
                        body,
                        @"(?im)^\s*" +
                        @"match_index_count\s*=\s*(\d+)");


                if (!firstIndexMatch.Success ||
                    !countMatch.Success)
                {
                    continue;
                }


                if (!long.TryParse(
                        firstIndexMatch.Groups[1].Value,
                        out long firstIndex))
                {
                    continue;
                }


                if (!long.TryParse(
                        countMatch.Groups[1].Value,
                        out long indexCount))
                {
                    continue;
                }


                yield return new Component3Info
                {
                    FirstIndex = firstIndex,
                    IndexCount = indexCount,
                    VgOffset = ParseVgOffset(body),
                    SourceIni = iniPath
                };
            }
        }


        private static long ParseVgOffset(
            string body)
        {
            Match match =
                Regex.Match(
                    body,
                    @"(?im)" +
                    @"^\s*" +
                    @"(?:\\?\$\\?WWMIv1\\?)?" +
                    @"vg_offset\s*=\s*(\d+)");


            if (match.Success &&
                long.TryParse(
                    match.Groups[1].Value,
                    out long value))
            {
                return value;
            }


            match =
                Regex.Match(
                    body,
                    @"(?im)" +
                    @"vg_offset\s*=\s*(\d+)");


            if (match.Success &&
                long.TryParse(
                    match.Groups[1].Value,
                    out value))
            {
                return value;
            }


            return -1;
        }


        // ============================================================
        // SKELETON DETECTION
        // ============================================================

        private static bool DetectMergedSkeleton(
            IEnumerable<string> iniFiles)
        {
            foreach (string iniFile in iniFiles)
            {
                string text =
                    File.ReadAllText(
                        iniFile,
                        Encoding.UTF8);


                if (Regex.IsMatch(
                        text,
                        @"(?im)" +
                        @"^\s*(?:global\s+)?" +
                        @"\$merge_status_id\s*="))
                {
                    return true;
                }


                if (Regex.IsMatch(
                        text,
                        @"(?im)" +
                        @"BlendRemapForward\.buf"))
                {
                    return true;
                }


                if (Regex.IsMatch(
                        text,
                        @"(?im)" +
                        @"BlendRemapVertexVG\.buf"))
                {
                    return true;
                }
            }


            return false;
        }


        // ============================================================
        // HELPERS
        // ============================================================

        private static bool IsEligibleIni(
            string path)
        {
            return
                !Path.GetFileName(path)
                    .Contains(
                        "disabled",
                        StringComparison.OrdinalIgnoreCase);
        }


        private static bool IsBackupFile(
            string path)
        {
            return
                Path.GetFileName(path)
                    .Contains(
                        ".BAK.woju.",
                        StringComparison.OrdinalIgnoreCase);
        }


        private static void Log(
            Action<string>? log,
            string message)
        {
            log?.Invoke(message);
        }
    }
}