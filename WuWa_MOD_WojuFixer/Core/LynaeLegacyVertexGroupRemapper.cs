using System;
using System.IO;

namespace WuWa_MOD_WojuFixer.Core
{
    public static class LynaeLegacyVertexGroupRemapper
    {
        // ============================================================
        // PUBLIC ENTRY
        // ============================================================

        public static void ApplyLynaeLegacyFix(
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
                "Starting Legacy Lynae Vertex Group Fix...");

            /*
             * IMPORTANT
             *
             * Legacy Lynae requires a different preparation stage
             * before the existing Lynae VG mapping is applied.
             *
             * For the first implementation we deliberately DO NOT
             * modify Blend.buf weights here.
             *
             * The current VertexGroupRemapper already contains the
             * verified Lynae VG mapping for:
             *
             *   - Merged Skeleton
             *   - Per-Component Skeleton
             *
             * Therefore we reuse that implementation instead of
             * duplicating the mapping.
             */

            Log(
                log,
                "Legacy Lynae: Using current Lynae VG mapping.");

            try
            {
                VertexGroupRemapper.ApplyLynaeFix(
                    rootFolder,
                    log);
            }
            catch (Exception ex)
            {
                Log(
                    log,
                    $"Legacy Lynae VG Fix failed: {ex.Message}");

                throw;
            }

            Log(
                log,
                "Legacy Lynae Vertex Group Fix completed.");
        }


        // ============================================================
        // HELPERS
        // ============================================================

        private static void Log(
            Action<string>? log,
            string message)
        {
            log?.Invoke(message);
        }
    }
}