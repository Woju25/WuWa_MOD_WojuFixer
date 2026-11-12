using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WuWa_MOD_WojuFixer.Core
{
    public enum BackupType
    {
        HashReplacement,    // mod.ini.BAK.woju.YYYY-MM-DD_HH-mm-ss
        LodInjection,       // mod.ini.BAK.woju.LOD.YYYY-MM-DD_HH-mm-ss
        StableTextures,     // mod.ini.BAK.woju.STABLE.YYYY-MM-DD_HH-mm-ss
        InvisibleFix,       // Color.buf.BAK.woju.INVISIBLE.YYYY-MM-DD_HH-mm-ss
        VertexGroupFix,     // Blend.buf.BAK.woju.VG.YYYY-MM-DD_HH-mm-ss
        DerivedHash         // mod.ini.BAK.woju.DERIVED.YYYY-MM-DD_HH-mm-ss
    }

    public sealed record BackupFile(
        string FilePath,
        DateTime CreatedTime,
        BackupType Type);

    public static class BackupManager
    {
        private const string BackupPrefix = ".BAK.woju";
        private const string BackupManifestFile = "backup.txt";

        public static string CreateBackupPath(
            string originalFilePath,
            BackupType type)
        {
            var timestamp =
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            return type switch
            {
                BackupType.HashReplacement =>
                    $"{originalFilePath}{BackupPrefix}.{timestamp}",

                BackupType.LodInjection =>
                    $"{originalFilePath}{BackupPrefix}.LOD.{timestamp}",

                BackupType.StableTextures =>
                    $"{originalFilePath}{BackupPrefix}.STABLE.{timestamp}",

                BackupType.InvisibleFix =>
                    $"{originalFilePath}{BackupPrefix}.INVISIBLE.{timestamp}",

                BackupType.VertexGroupFix =>
                    $"{originalFilePath}{BackupPrefix}.VG.{timestamp}",

                BackupType.DerivedHash =>
                    $"{originalFilePath}{BackupPrefix}.DERIVED.{timestamp}",

                _ => throw new ArgumentException(
                    "Unknown backup type",
                    nameof(type))
            };
        }

        public static void SaveBackup(
            string filePath,
            string content)
        {
            if (File.Exists(filePath))
            {
                throw new InvalidOperationException(
                    $"Backup file already exists: {filePath}");
            }

            File.WriteAllText(
                filePath,
                content);
        }

        /// <summary>
        /// Create a backup file and register it in backup.txt manifest.
        /// </summary>
        public static void CreateBackupAndRegister(
            string originalFilePath,
            string backupFilePath)
        {
            if (!File.Exists(originalFilePath))
            {
                throw new FileNotFoundException(
                    "Original file not found",
                    originalFilePath);
            }

            // Copy the original file to backup location.
            File.Copy(
                originalFilePath,
                backupFilePath,
                overwrite: false);

            // Register in manifest.
            var directory =
                Path.GetDirectoryName(originalFilePath);

            var backupFileName =
                Path.GetFileName(backupFilePath);

            RegisterBackupInManifest(
                directory,
                backupFileName);
        }

        /// <summary>
        /// Register a backup filename in the backup.txt manifest.
        /// </summary>
        private static void RegisterBackupInManifest(
            string? directory,
            string backupFileName)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }

            var manifestPath =
                Path.Combine(
                    directory,
                    BackupManifestFile);

            var backupList =
                new List<string>();

            // Read existing backups if manifest exists.
            if (File.Exists(manifestPath))
            {
                var lines =
                    File.ReadAllLines(manifestPath);

                // Skip header and blank line.
                backupList.AddRange(
                    lines
                        .Skip(2)
                        .Where(l =>
                            !string.IsNullOrWhiteSpace(l)));
            }

            // Add new backup if not already present.
            if (!backupList.Contains(backupFileName))
            {
                backupList.Add(backupFileName);
            }

            // Write manifest with header.
            var manifestLines =
                new List<string>
                {
                    "This file is critical for the revert function. Do not alter anything, including the backup filenames.",
                    ""
                };

            manifestLines.AddRange(
                backupList);

            File.WriteAllLines(
                manifestPath,
                manifestLines);
        }

        public static List<BackupFile> FindAllBackups(
            string modIniPath)
        {
            var backups =
                new List<BackupFile>();

            if (!File.Exists(modIniPath))
            {
                return backups;
            }

            var directory =
                Path.GetDirectoryName(modIniPath);

            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return backups;
            }

            var manifestPath =
                Path.Combine(
                    directory,
                    BackupManifestFile);

            // If manifest doesn't exist, no backups.
            if (!File.Exists(manifestPath))
            {
                return backups;
            }

            // Read backup filenames from manifest.
            // Skip first 2 lines: header + blank line.
            var backupFileNames =
                File.ReadAllLines(manifestPath)
                    .Skip(2)
                    .Where(l =>
                        !string.IsNullOrWhiteSpace(l))
                    .ToList();

            foreach (var backupFileName in backupFileNames)
            {
                var fullBackupPath =
                    Path.Combine(
                        directory,
                        backupFileName);

                // Skip if file doesn't exist.
                if (!File.Exists(fullBackupPath))
                {
                    continue;
                }

                BackupType type =
                    DetermineBackupType(
                        backupFileName);

                var fileInfo =
                    new FileInfo(fullBackupPath);

                backups.Add(
                    new BackupFile(
                        fullBackupPath,
                        fileInfo.LastWriteTime,
                        type));
            }

            return backups;
        }

        public static BackupFile? FindOriginalBackup(
            string originalFilePath)
        {
            var allBackups =
                FindAllBackups(
                    originalFilePath);

            if (allBackups.Count == 0)
            {
                return null;
            }

            var originalFileName =
                Path.GetFileName(
                    originalFilePath);

            // Find backups belonging to this specific original file.
            var matchingBackups =
                allBackups
                    .Where(backup =>
                    {
                        var backupFileName =
                            Path.GetFileName(
                                backup.FilePath);

                        return backupFileName.StartsWith(
                            originalFileName + BackupPrefix,
                            StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(
                        backup => backup.CreatedTime)
                    .ToList();

            if (matchingBackups.Count == 0)
            {
                return null;
            }

            // Return the oldest backup for this specific file.
            return matchingBackups.First();
        }

        public static void RestoreFromBackup(
            string backupPath,
            string targetPath)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException(
                    "Backup file not found.",
                    backupPath);
            }

            File.Copy(
                backupPath,
                targetPath,
                overwrite: true);
        }

        public static void CleanupBackups(
            string modIniPath)
        {
            var allBackups =
                FindAllBackups(
                    modIniPath);

            foreach (var backup in allBackups)
            {
                try
                {
                    File.Delete(
                        backup.FilePath);
                }
                catch
                {
                    // Ignore errors during cleanup.
                }
            }

            // Delete manifest file.
            var directory =
                Path.GetDirectoryName(
                    modIniPath);

            var manifestPath =
                Path.Combine(
                    directory ?? "",
                    BackupManifestFile);

            try
            {
                if (File.Exists(manifestPath))
                {
                    File.Delete(
                        manifestPath);
                }
            }
            catch
            {
                // Ignore errors.
            }
        }

        private static BackupType DetermineBackupType(
            string backupFileName)
        {
            if (backupFileName.Contains(
                    ".STABLE.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupType.StableTextures;
            }

            if (backupFileName.Contains(
                    ".LOD.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupType.LodInjection;
            }

            if (backupFileName.Contains(
                    ".INVISIBLE.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupType.InvisibleFix;
            }

            if (backupFileName.Contains(
                    ".VG.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupType.VertexGroupFix;
            }

            if (backupFileName.Contains(
                    ".DERIVED.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BackupType.DerivedHash;
            }

            return BackupType.HashReplacement;
        }
    }
}