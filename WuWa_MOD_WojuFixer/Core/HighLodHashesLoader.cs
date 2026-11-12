using System.IO;
using System.Text.Json;

namespace WuWa_MOD_WojuFixer.Core
{
    public static class HighLodHashesLoader
    {
        public static HighLodHashesRoot Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("HighLodHashes.json not found", jsonPath);

            var json = File.ReadAllText(jsonPath);

            var root = JsonSerializer.Deserialize<HighLodHashesRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return root ?? new HighLodHashesRoot();
        }
    }
}