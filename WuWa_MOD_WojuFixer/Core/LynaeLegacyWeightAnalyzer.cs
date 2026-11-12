using System;
using System.IO;

namespace WuWa_MOD_WojuFixer.Core
{
    public static class LynaeLegacyWeightAnalyzer
    {
        private const int BytesPerVertex = 16;
        private const int IndexOffset = 0;
        private const int WeightOffset = 8;
        private const int InfluenceCount = 8;

        public static void Analyze(
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
                "Starting Legacy Lynae Weight Analysis...");

            string meshesFolder = Path.Combine(
                rootFolder,
                "Meshes");

            if (!Directory.Exists(meshesFolder))
            {
                Log(
                    log,
                    "Legacy Weight Analysis: Meshes folder not found.");

                return;
            }

            // IMPORTANT:
            // Only analyze actual Blend.buf files.
            string[] blendFiles = Directory.GetFiles(
                meshesFolder,
                "Blend.buf",
                SearchOption.AllDirectories);

            Log(
                log,
                $"Found {blendFiles.Length} Blend.buf file(s).");

            foreach (string file in blendFiles)
            {
                AnalyzeFile(file, log);
            }

            Log(
                log,
                "Legacy Lynae Weight Analysis completed.");
        }

        private static void AnalyzeFile(
            string filePath,
            Action<string>? log)
        {
            byte[] data = File.ReadAllBytes(filePath);

            if (data.Length % BytesPerVertex != 0)
            {
                Log(
                    log,
                    $"  Skipped: {Path.GetFileName(filePath)} " +
                    $"size {data.Length:N0} is not divisible by " +
                    $"{BytesPerVertex}.");

                return;
            }

            int vertexCount =
                data.Length / BytesPerVertex;

            Log(
                log,
                $"  File: {Path.GetFileName(filePath)}");

            Log(
                log,
                $"  Vertices: {vertexCount:N0}");

            int candidateCount = 0;

            for (int vertex = 0;
                 vertex < vertexCount;
                 vertex++)
            {
                int baseOffset =
                    vertex * BytesPerVertex;

                ReadOnlySpan<byte> indices =
                    data.AsSpan(
                        baseOffset + IndexOffset,
                        InfluenceCount);

                ReadOnlySpan<byte> weights =
                    data.AsSpan(
                        baseOffset + WeightOffset,
                        InfluenceCount);

                int nonZeroCount =
                    CountNonZero(weights);

                if (nonZeroCount < 2)
                    continue;

                int weightSum =
                    SumWeights(weights);

                if (weightSum != 255)
                    continue;

                if (!LooksLikeLegacyNormalizationCandidate(
                        weights))
                {
                    continue;
                }

                candidateCount++;

                LogCandidate(
                    log,
                    vertex,
                    indices,
                    weights);
            }

            Log(
                log,
                $"  Candidate vertices: {candidateCount:N0}");
        }

        private static bool LooksLikeLegacyNormalizationCandidate(
            ReadOnlySpan<byte> weights)
        {
            /*
             * Experimental diagnostic rule.
             *
             * We are NOT modifying anything yet.
             *
             * We are looking for active weights where at least
             * two values differ by exactly 1.
             *
             * Example:
             *
             *     84 84 85 2
             *
             * or:
             *
             *     123 114 7 5 6
             *
             * These are interesting because our OLD -> NEW
             * comparison showed exactly this type of change.
             */

            for (int i = 0;
                 i < InfluenceCount - 1;
                 i++)
            {
                byte a = weights[i];

                if (a == 0)
                    continue;

                for (int j = i + 1;
                     j < InfluenceCount;
                     j++)
                {
                    byte b = weights[j];

                    if (b == 0)
                        continue;

                    int difference =
                        a > b
                            ? a - b
                            : b - a;

                    if (difference == 1)
                        return true;
                }
            }

            return false;
        }

        private static void LogCandidate(
            Action<string>? log,
            int vertex,
            ReadOnlySpan<byte> indices,
            ReadOnlySpan<byte> weights)
        {
            Log(
                log,
                $"    Candidate vertex {vertex:N0}");

            Log(
                log,
                $"      Indices: {FormatBytes(indices)}");

            Log(
                log,
                $"      Weights: {FormatBytes(weights)}");

            Log(
                log,
                $"      Sum: {SumWeights(weights)}");
        }

        private static int CountNonZero(
            ReadOnlySpan<byte> values)
        {
            int count = 0;

            for (int i = 0;
                 i < values.Length;
                 i++)
            {
                if (values[i] != 0)
                    count++;
            }

            return count;
        }

        private static int SumWeights(
            ReadOnlySpan<byte> values)
        {
            int sum = 0;

            for (int i = 0;
                 i < values.Length;
                 i++)
            {
                sum += values[i];
            }

            return sum;
        }

        private static string FormatBytes(
            ReadOnlySpan<byte> values)
        {
            if (values.Length == 0)
                return string.Empty;

            return string.Join(
                ", ",
                values.ToArray());
        }

        private static void Log(
            Action<string>? log,
            string message)
        {
            log?.Invoke(message);
        }
    }
}