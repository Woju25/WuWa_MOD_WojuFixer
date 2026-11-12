using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WuWa_MOD_WojuFixer.Core
{
    public sealed class HighLodHashesRoot
    {
        [JsonPropertyName("characters")]
        public List<HighLodHashesCharacter> Characters { get; set; } = new();
    }

    public sealed class HighLodHashesCharacter
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("detect_any_hashes")]
        public List<string> DetectAnyHashes { get; set; } = new();

        [JsonPropertyName("components")]
        public List<HighLodHashesComponent> Components { get; set; } = new();

        // Optional extra section (Lynae etc.)
        [JsonPropertyName("convene_screen")]
        public Dictionary<string, HighLodHashesPair>? ConveneScreen { get; set; }
    }

    public sealed class HighLodHashesComponent
    {
        [JsonPropertyName("component_index")]
        public int ComponentIndex { get; set; }

        [JsonPropertyName("diffuse")]
        public HighLodHashesPair? Diffuse { get; set; }

        [JsonPropertyName("diffuse1")]
        public HighLodHashesPair? Diffuse1 { get; set; }

        [JsonPropertyName("diffuse2")]
        public HighLodHashesPair? Diffuse2 { get; set; }

        // NEW: needed for Luuk component_index 7
        [JsonPropertyName("diffuse3")]
        public HighLodHashesPair? Diffuse3 { get; set; }

        // NEW: needed for Luuk component_index 7
        [JsonPropertyName("diffuse4")]
        public HighLodHashesPair? Diffuse4 { get; set; }

        [JsonPropertyName("lightmap")]
        public HighLodHashesPair? Lightmap { get; set; }

        // NEW: seen in Luuk config
        [JsonPropertyName("lightmap2")]
        public HighLodHashesPair? Lightmap2 { get; set; }

        [JsonPropertyName("normalmap")]
        public HighLodHashesPair? Normalmap { get; set; }

        // NEW: seen in Luuk config
        [JsonPropertyName("normalmap2")]
        public HighLodHashesPair? Normalmap2 { get; set; }

        [JsonPropertyName("glow")]
        public HighLodHashesPair? Glow { get; set; }

        // NEW: seen in Luuk config
        [JsonPropertyName("glow2")]
        public HighLodHashesPair? Glow2 { get; set; }

        [JsonPropertyName("skin")]
        public HighLodHashesPair? Skin { get; set; }

        [JsonPropertyName("metal")]
        public HighLodHashesPair? Metal { get; set; }

        // NEW: seen in Luuk config
        [JsonPropertyName("metal2")]
        public HighLodHashesPair? Metal2 { get; set; }

        [JsonPropertyName("forte")]
        public HighLodHashesPair? Forte { get; set; }

        [JsonPropertyName("forteLightmap")]
        public HighLodHashesPair? ForteLightmap { get; set; }

        [JsonPropertyName("forteNormalmap")]
        public HighLodHashesPair? ForteNormalmap { get; set; }

        [JsonPropertyName("forteGlow")]
        public HighLodHashesPair? ForteGlow { get; set; }

        [JsonPropertyName("diffuseEyes")]
        public HighLodHashesPair? DiffuseEyes { get; set; }
    }

    public sealed class HighLodHashesPair
    {
        [JsonPropertyName("base")]
        public string Base { get; set; } = "";

        [JsonPropertyName("lod_high")]
        public string LodHigh { get; set; } = "";
    }
}