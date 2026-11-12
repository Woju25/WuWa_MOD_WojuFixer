using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public static class CharacterDetector
    {
        private static readonly string[] SpecialCharacters = { "Aemeath", "Lynae", "Mornye", "Luuk" };

        /// <summary>
        /// Dynamically build unique hash to character name mapping from hashes.json
        /// Format: "CharacterName = unique_hash" -> extract character name and hash
        /// </summary>
        public static Dictionary<string, string> LoadUniqueHashesFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Could not find hashes.json at the configured path: {jsonPath}", jsonPath);

            var json = File.ReadAllText(jsonPath);

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("hashes.json root must be an object.");

            var uniqueHashToCharacter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                var uniqueHash = parts[1].Trim();

                // Store mapping: unique_hash -> character_name
                // If same hash exists for multiple character variants, they'll all map to the base character name
                // (e.g., both "ChangLiOld = 5f8aac45" and "ChangLi14 = d14bed8b" map to "Changli")
                if (!string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(uniqueHash))
                {
                    // Extract base character name (remove version suffix like "Old", "14", "31", etc.)
                    var baseCharacterName = ExtractBaseCharacterName(characterName);
                    uniqueHashToCharacter[uniqueHash] = baseCharacterName;
                }
            }

            return uniqueHashToCharacter;
        }

        /// <summary>
        /// Extract base character name from versioned name
        /// Examples:
        ///   "Aemeath31" -> "Aemeath"
        ///   "ChangLiOld" -> "Changli"
        ///   "ChangLi14" -> "Changli"
        ///   "RoverM31" -> "RoverM"
        ///   "RoverF32" -> "RoverF"
        ///   "Zani27Menu" -> "Zani"
        ///   "Carlotta_IceHair26" -> "Carlotta"
        /// </summary>
        private static string ExtractBaseCharacterName(string versionedName)
        {
            if (string.IsNullOrWhiteSpace(versionedName))
                return versionedName;

            // Handle special cases with underscores (Carlotta_IceHair26, Carlotta_NormalHair26, etc.)
            if (versionedName.Contains("_", StringComparison.OrdinalIgnoreCase))
            {
                var beforeUnderscore = versionedName.Split('_')[0];

                // Remove trailing numbers from the part before underscore
                return RemoveTrailingNumbers(beforeUnderscore);
            }

            // Default: remove trailing numbers and suffixes
            return RemoveTrailingVersionSuffixes(versionedName);
        }

        /// <summary>
        /// Remove version numbers and suffixes from character names
        /// Examples:
        ///   "Aemeath31" -> "Aemeath"
        ///   "ChangLi14" -> "Changli"
        ///   "ChangLiOld" -> "Changli"
        ///   "RoverM31" -> "RoverM"
        ///   "RoverF32" -> "RoverF"
        /// </summary>
        private static string RemoveTrailingVersionSuffixes(string name)
        {
            // Remove trailing numbers first
            var withoutNumbers = RemoveTrailingNumbers(name);

            // Remove known suffixes
            var suffixes = new[]
            {
                "Old",
                "Menu",
                "Extra",
                "Chainsaw",
                "Sig",
                "Rifle",
                "Ulti",
                "UltiTorso",
                "Torso",
                "Lower",
                "Upper",
                "Liberation",
                "Wet"
            };

            foreach (var suffix in suffixes)
            {
                if (withoutNumbers.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return withoutNumbers.Substring(
                        0,
                        withoutNumbers.Length - suffix.Length);
                }
            }

            return withoutNumbers;
        }

        private static string RemoveTrailingNumbers(string name)
        {
            int i = name.Length - 1;

            while (i >= 0 && char.IsDigit(name[i]))
            {
                i--;
            }

            return name.Substring(0, i + 1);
        }

        /// <summary>
        /// Detect character using ONLY the hash inside [TextureOverrideComponent0].
        ///
        /// This prevents hashes from other sections such as:
        /// [TextureOverrideBody]
        /// [TextureOverrideWeapon]
        /// [TextureOverridePosition]
        /// etc. from being incorrectly detected as the character hash.
        ///
        /// [TextureOverrideComponent0] is used as the authoritative
        /// character detection section.
        /// </summary>
        public static string? DetectCharacterFromModIni(
            string iniText,
            Dictionary<string, string> uniqueHashToCharacter)
        {
            if (string.IsNullOrWhiteSpace(iniText))
                return null;

            if (uniqueHashToCharacter == null ||
                uniqueHashToCharacter.Count == 0)
            {
                return null;
            }

            var lines = iniText
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            bool insideComponent0 = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // =========================================================
                // Start of [TextureOverrideComponent0]
                // =========================================================
                if (string.Equals(
                        trimmed,
                        "[TextureOverrideComponent0]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    insideComponent0 = true;
                    continue;
                }

                // =========================================================
                // Another section begins.
                // Stop searching once Component0 has ended.
                // =========================================================
                if (insideComponent0 &&
                    trimmed.StartsWith("[") &&
                    trimmed.EndsWith("]"))
                {
                    break;
                }

                if (!insideComponent0)
                    continue;

                // =========================================================
                // Only process "hash = XXXXXXXX"
                // inside TextureOverrideComponent0.
                // =========================================================
                if (!trimmed.StartsWith(
                        "hash = ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = trimmed
                    .Substring("hash = ".Length)
                    .Trim();

                if (hash.Length != 8)
                    continue;

                // =========================================================
                // Match Component0 hash against hashes.json
                // =========================================================
                if (uniqueHashToCharacter.TryGetValue(
                        hash,
                        out var characterName))
                {
                    return characterName;
                }

                // Component0 exists, but its hash is not known
                // in hashes.json.
                return null;
            }

            return null;
        }

        public static bool IsSpecialCharacter(string? characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                return false;

            return SpecialCharacters.Contains(
                characterName,
                StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> ExtractAllHashes(string iniText)
        {
            var hashes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var lines = iniText
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (!trimmed.StartsWith(
                        "hash = ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = trimmed
                    .Substring("hash = ".Length)
                    .Trim();

                if (hash.Length == 8)
                    hashes.Add(hash);
            }

            return hashes;
        }
    }
}