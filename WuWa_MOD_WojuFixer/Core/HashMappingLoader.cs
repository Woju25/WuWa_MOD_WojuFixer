using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed record Replacement(string From, string To);

    public static class HashMappingLoader
    {
        /// <summary>
        /// Load all replacements from hashes.json (global + character-specific)
        /// Format: "CharacterName = unique_hash" -> replacements
        /// </summary>
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

        /// <summary>
        /// Load character-specific replacements from hashes.json
        /// Returns dictionary: CharacterName -> list of replacements for that character
        /// Example: "Lynae" -> all Lynae31 hashes combined
        /// </summary>
        public static Dictionary<string, List<Replacement>> LoadCharacterReplacementsFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Could not find hashes.json next to the app executable.", jsonPath);

            var json = File.ReadAllText(jsonPath, Encoding.UTF8);

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("hashes.json root must be an object.");

            var characterReplacements = new Dictionary<string, List<Replacement>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in doc.RootElement.EnumerateObject())
            {
                var groupName = group.Name;

                // Check if this is a character-specific group (format: "CharacterName = unique_hash")
                if (!groupName.Contains("=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = groupName.Split('=');
                if (parts.Length != 2)
                    continue;

                var characterName = parts[0].Trim();

                if (group.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Group '{groupName}' must be an object of string-to-string mappings.");

                var replacements = new List<Replacement>();

                foreach (var prop in group.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException($"Group '{groupName}' entry '{prop.Name}' must map to a string.");

                    var from = prop.Name;
                    var to = prop.Value.GetString() ?? "";

                    ValidatePair(from, to, groupName);
                    replacements.Add(new Replacement(from, to));
                }

                if (replacements.Count > 0)
                {
                    // Merge with existing character replacements
                    // This handles multiple versions of the same character (e.g., ChangLiOld, ChangLi14)
                    if (!characterReplacements.ContainsKey(characterName))
                    {
                        characterReplacements[characterName] = new List<Replacement>();
                    }

                    characterReplacements[characterName].AddRange(replacements);
                }
            }

            return characterReplacements;
        }

        private static void ValidatePair(string from, string to, string groupName)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                throw new InvalidDataException($"Group '{groupName}' has an empty key/value.");

            if (!from.StartsWith("hash = ", StringComparison.Ordinal) ||
                !to.StartsWith("hash = ", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Group '{groupName}' has invalid entry. Expected 'hash = ...' format but got: '{from}' -> '{to}'.");
            }
        }
    }
}