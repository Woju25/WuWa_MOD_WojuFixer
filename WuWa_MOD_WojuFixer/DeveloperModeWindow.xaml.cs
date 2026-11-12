using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using WuWa_MOD_WojuFixer.Core;

namespace WuWa_MOD_WojuFixer
{
    public partial class DeveloperModeWindow : Window
    {
        private readonly string _appDataPath;

        public DeveloperModeWindow()
        {
            InitializeComponent();
            _appDataPath = AppContext.BaseDirectory;

            // Allow ESC to close
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                }
            };
        }

        private void UpdateStableHashesButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateStableHashesButton.IsEnabled = false;
            UpdateHighLodHashesButton.IsEnabled = false;

            try
            {
                LogLine("=== Updating Stable Hashes ===\n");
                UpdateStableHashes();
                LogLine("\n=== Update Complete ===");
            }
            catch (Exception ex)
            {
                LogLine($"\n[ERROR] {ex.Message}");
            }
            finally
            {
                UpdateStableHashesButton.IsEnabled = true;
                UpdateHighLodHashesButton.IsEnabled = true;
            }
        }

        private void UpdateHighLodHashesButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateStableHashesButton.IsEnabled = false;
            UpdateHighLodHashesButton.IsEnabled = false;

            try
            {
                LogLine("=== Updating High LOD Hashes ===\n");
                UpdateHighLodHashes();
                LogLine("\n=== Update Complete ===");
            }
            catch (Exception ex)
            {
                LogLine($"\n[ERROR] {ex.Message}");
            }
            finally
            {
                UpdateStableHashesButton.IsEnabled = true;
                UpdateHighLodHashesButton.IsEnabled = true;
            }
        }

        private void UpdateStableHashes()
        {
            var hashesPath = Path.Combine(_appDataPath, "hashes.json");
            var stableHashesPath = Path.Combine(_appDataPath, "StableHashes.json");
            LogLine($"StableHashes Path:");
            LogLine(stableHashesPath);
            LogLine("");
            if (!File.Exists(hashesPath))
            {
                LogLine("[ERROR] hashes.json not found!");
                return;
            }

            if (!File.Exists(stableHashesPath))
            {
                LogLine("[ERROR] StableHashes.json not found!");
                return;
            }

            LogLine("Loading hashes.json...");
            LogLine("Loading StableHashes.json...\n");

            using var hashesDoc =
                JsonDocument.Parse(File.ReadAllText(hashesPath));

            var hashMap =
                BuildHashMap(hashesDoc.RootElement);

            LogLine($"Loaded {hashMap.Count} hash mappings.\n");

            var stableJson =
                File.ReadAllText(stableHashesPath);

            using var stableDoc =
                JsonDocument.Parse(stableJson);

            var root =
                JsonSerializer.Deserialize<Dictionary<string, object>>(
                    stableJson);

            if (root == null)
            {
                LogLine("[ERROR] Failed to parse StableHashes.json");
                return;
            }

            int updatedCount = 0;

            string updatedJson = stableJson;

            foreach (var oldHash in hashMap.Keys)
            {
                string newHash = hashMap[oldHash];

                if (updatedJson.Contains($"\"{oldHash}\""))
                {
                    
                    updatedJson =
                        updatedJson.Replace(
                            $"\"{oldHash}\"",
                            $"\"{newHash}\"");

                    updatedCount++;
                }
            }
            string backupPath =
                Path.ChangeExtension(
                    stableHashesPath,
                    ".bak");

            File.Copy(
                stableHashesPath,
                backupPath,
                true);
            File.WriteAllText(
                stableHashesPath,
                updatedJson);

            LogLine($"Updated {updatedCount} hashes.");
        }

        private void UpdateHighLodHashes()
        {
            var hashesPath = Path.Combine(_appDataPath, "hashes.json");
            var highLodHashesPath = Path.Combine(_appDataPath, "HighLodHashes.json");

            if (!File.Exists(hashesPath))
            {
                LogLine("[ERROR] hashes.json not found!");
                return;
            }

            if (!File.Exists(highLodHashesPath))
            {
                LogLine("[ERROR] HighLodHashes.json not found!");
                return;
            }
            LogLine("Loading hashes.json...");
            LogLine("Loading HighLodHashes.json...\n");

            using var hashesDoc =
                JsonDocument.Parse(File.ReadAllText(hashesPath));

            var hashMap =
                BuildHashMap(hashesDoc.RootElement);

            LogLine($"Loaded {hashMap.Count} hash mappings.\n");

            string highLodJson =
                File.ReadAllText(highLodHashesPath);

            using var highLodDoc =
                JsonDocument.Parse(highLodJson);

            int updatedCount = 0;

            foreach (var oldHash in hashMap.Keys)
            {
                string newHash = hashMap[oldHash];

                // detect_any_hashes
                string detectPattern =
                    $"\"{oldHash}\"";



                // base hashes only
                string basePattern =
                    $"\"base\": \"{oldHash}\"";

                if (highLodJson.Contains(basePattern))
                {


                    highLodJson =
                        highLodJson.Replace(
                            basePattern,
                            $"\"base\": \"{newHash}\"");

                    updatedCount++;
                }
            }
            string backupPath =
    Path.ChangeExtension(
        highLodHashesPath,
        ".bak");

            File.Copy(
                highLodHashesPath,
                backupPath,
                true);

            File.WriteAllText(
    highLodHashesPath,
    highLodJson);

            LogLine($"Updated {updatedCount} hashes.");
        }

        private List<string> GetHashesFromStableChar(JsonElement charElement)
        {
            var hashes = new List<string>();
            if (charElement.TryGetProperty("detectAnyHashes", out var detectElement))
            {
                foreach (var hash in detectElement.EnumerateArray())
                {
                    hashes.Add(hash.GetString());
                }
            }
            return hashes;
        }

        private List<string> GetHashesFromHighLodChar(JsonElement charElement)
        {
            var hashes = new List<string>();
            if (charElement.TryGetProperty("detectAnyHashes", out var detectElement))
            {
                foreach (var hash in detectElement.EnumerateArray())
                {
                    hashes.Add(hash.GetString());
                }
            }
            return hashes;
        }

  
        private Dictionary<string, string> BuildHashMap(JsonElement hashesRoot)
        {
            var rawMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var section in hashesRoot.EnumerateObject())
            {
                if (section.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var mapping in section.Value.EnumerateObject())
                {
                    string oldHash = mapping.Name
                        .Replace("hash =", "")
                        .Trim();

                    string newHash = mapping.Value.GetString()?
                        .Replace("hash =", "")
                        .Trim();

                    if (!string.IsNullOrWhiteSpace(oldHash) &&
                        !string.IsNullOrWhiteSpace(newHash))
                    {
                        if (!oldHash.Equals(
        newHash,
        StringComparison.OrdinalIgnoreCase))
                        {
                            rawMap[oldHash] = newHash;
                        }

                    }
                }
            }

            return ResolveHashChains(rawMap);
        }

        private Dictionary<string, string> ResolveHashChains(Dictionary<string, string> rawMap)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hash in rawMap.Keys)
            {
                string current = hash;

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (rawMap.TryGetValue(current, out var next))
                {
                    if (!visited.Add(current))
                        break;

                    current = next;
                }

                result[hash] = current;
            }

            return result;
        }

        private string UpdateHash(string hash, Dictionary<string, string> hashMap)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return hash;

            return hashMap.TryGetValue(hash, out var newest)
                ? newest
                : hash;
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LogLine(string message)
        {
            LogTextBox.AppendText($"{message}\n");
            LogTextBox.ScrollToEnd();
        }
    }
}