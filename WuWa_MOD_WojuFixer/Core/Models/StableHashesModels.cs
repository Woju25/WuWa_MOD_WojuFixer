using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WuWa_MOD_WojuFixer.Models
{
    public class StableHashesRoot
    {
        [JsonPropertyName("characters")]
        public List<StableCharacter> Characters { get; set; } = new();
    }

    public class StableCharacter
    {
        public string Name { get; set; }

        public string CharacterHash { get; set; }

        public string LastVerifiedGameVersion { get; set; }

        public string LastUpdatedDate { get; set; }

        public bool Enabled { get; set; }

        public List<StableComponent> Components { get; set; } = new();
    }

    public class StableComponent
    {
        public int ComponentIndex { get; set; }

        public string DiffuseHash { get; set; }

        public string LightmapHash { get; set; }

        public string NormalmapHash { get; set; }

        public string OptionalNotes { get; set; }
    }
}