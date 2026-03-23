using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed class StableHashesRoot
    {
        public int SchemaVersion { get; set; }
        public List<StableCharacter> Characters { get; set; } = new();
    }

    public sealed class StableCharacter
    {
        public string Name { get; set; } = "";
        public string CharacterHash { get; set; } = "";
        public string? LastVerifiedGameVersion { get; set; }
        public string? LastUpdatedDate { get; set; }
        public bool Enabled { get; set; } = true;
        public List<StableComponent> Components { get; set; } = new();
    }

    public sealed class StableComponent
    {
        public int ComponentIndex { get; set; }
        public string? DiffuseHash { get; set; }
        public string? LightmapHash { get; set; }
        public string? NormalmapHash { get; set; }
        public string? OptionalNotes { get; set; }
    }

    public static class StableHashesLoader
    {
        public static StableHashesRoot Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Could not find StableHashes.json next to the app executable.", jsonPath);

            var json = File.ReadAllText(jsonPath, Encoding.UTF8);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var root = JsonSerializer.Deserialize<StableHashesRoot>(json, options)
                       ?? throw new InvalidDataException("StableHashes.json could not be parsed.");

            if (root.Characters == null || root.Characters.Count == 0)
                throw new InvalidDataException("StableHashes.json contains no characters.");

            // Normalize: ignore disabled
            root.Characters.RemoveAll(c => c is null || !c.Enabled);

            return root;
        }
    }
}