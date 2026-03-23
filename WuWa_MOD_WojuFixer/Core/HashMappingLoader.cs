using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed record Replacement(string From, string To);

    public static class HashMappingLoader
    {
        // Grouped JSON format expected:
        // {
        //   "Aalto26": { "hash = 3edd37c6": "hash = 4eba0ca3", ... },
        //   "Baizhi26": { ... }
        // }
        public static List<Replacement> LoadReplacementsFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Could not find hashes.json next to the app executable.", jsonPath);

            var json = File.ReadAllText(jsonPath, Encoding.UTF8);

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("hashes.json root must be an object.");

            var replacements = new List<Replacement>();

            foreach (var group in doc.RootElement.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Group '{group.Name}' must be an object of string-to-string mappings.");

                foreach (var prop in group.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException($"Group '{group.Name}' entry '{prop.Name}' must map to a string.");

                    var from = prop.Name;
                    var to = prop.Value.GetString() ?? "";

                    ValidatePair(from, to, group.Name);
                    replacements.Add(new Replacement(from, to));
                }
            }

            if (replacements.Count == 0)
                throw new InvalidDataException("hashes.json contains no replacement entries.");

            return replacements;
        }

        private static void ValidatePair(string from, string to, string groupName)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                throw new InvalidDataException($"Group '{groupName}' has an empty key/value.");

            // Requirement: exact format with spacing/case like "hash = 3edd37c6"
            if (!from.StartsWith("hash = ", StringComparison.Ordinal) ||
                !to.StartsWith("hash = ", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Group '{groupName}' has invalid entry. Expected 'hash = ...' format but got: '{from}' -> '{to}'.");
            }
        }
    }
}
