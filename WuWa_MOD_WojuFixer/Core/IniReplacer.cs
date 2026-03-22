using System;
using System.Collections.Generic;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed class ReplaceResult
    {
        public required string UpdatedText { get; init; }
        public required int TotalReplacements { get; init; }
        public required int Passes { get; init; }
        public required bool HitMaxPasses { get; init; }
    }

    public static class IniReplacer
    {
        // Applies rules in order, repeating full passes until stable (or maxPasses).
        public static ReplaceResult ReplaceStepByStepUntilStable(
            string originalText,
            IReadOnlyList<Replacement> replacements,
            int maxPasses = 50)
        {
            if (originalText is null) throw new ArgumentNullException(nameof(originalText));
            if (replacements is null) throw new ArgumentNullException(nameof(replacements));
            if (maxPasses < 1) throw new ArgumentOutOfRangeException(nameof(maxPasses));

            var text = originalText;
            int totalRepl = 0;
            int passes = 0;

            while (passes < maxPasses)
            {
                passes++;

                int passRepl = 0;

                for (int i = 0; i < replacements.Count; i++)
                {
                    var from = replacements[i].From;
                    var to = replacements[i].To;

                    int count = CountOccurrences(text, from);
                    if (count > 0)
                    {
                        text = text.Replace(from, to, StringComparison.Ordinal);
                        passRepl += count;
                    }
                }

                totalRepl += passRepl;

                if (passRepl == 0)
                {
                    return new ReplaceResult
                    {
                        UpdatedText = text,
                        TotalReplacements = totalRepl,
                        Passes = passes,
                        HitMaxPasses = false
                    };
                }
            }

            return new ReplaceResult
            {
                UpdatedText = text,
                TotalReplacements = totalRepl,
                Passes = passes,
                HitMaxPasses = true
            };
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (needle.Length == 0) return 0;

            int count = 0;
            int index = 0;

            while (true)
            {
                index = haystack.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0) break;

                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}