using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using WuWa_MOD_WojuFixer.Core;
using WinForms = System.Windows.Forms;

namespace WuWa_MOD_WojuFixer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Log("Ready (Woju).");
        }

        // These handlers exist only to satisfy XAML if you hooked them up there.
        // They are optional; you can remove the Checked/Unchecked attributes from XAML instead.
        private void StableTexturesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Optional: Log("StableTextures enabled.");
        }

        private void StableTexturesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Optional: Log("StableTextures disabled.");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select mod folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            var result = dialog.ShowDialog();
            if (result == WinForms.DialogResult.OK && Directory.Exists(dialog.SelectedPath))
            {
                FolderTextBox.Text = dialog.SelectedPath;
                Log($"Selected folder: {dialog.SelectedPath}");
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            RunButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;

            try
            {
                var folder = FolderTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show("Please select a valid folder first.", "Woju");
                    return;
                }

                // 1) Normal hash replacement
                var hashesPath = Path.Combine(AppContext.BaseDirectory, "hashes.json");
                Log($"Loading hashes: {hashesPath}");

                var replacements = HashMappingLoader.LoadReplacementsFromJson(hashesPath);
                Log($"Loaded {replacements.Count} replacement rules.");

                var processor = new ModIniProcessor(replacements);

                Log("Scanning mod.ini files...");
                var (summary, plans) = await Task.Run(() => processor.Preview(folder));

                Log($"Found mod.ini files: {summary.ModIniFound}");
                Log($"Files needing changes: {summary.ModIniChanged}");
                Log($"Total replacements (estimated): {summary.TotalReplacements}");

                if (summary.FilesHitMaxPasses > 0)
                {
                    Log($"WARNING: {summary.FilesHitMaxPasses} file(s) hit the max pass limit (possible replacement cycle).");
                }

                if (summary.AnyChange)
                {
                    Log("Applying changes (creating backups first)...");
                    await Task.Run(() => processor.ExecutePlans(plans));
                    Log("Normal hash replacement completed.");
                }
                else
                {
                    Log("Normal hash replacement: no changes needed.");
                }

                // 2) StableTextures injection (optional)
                if (StableTexturesCheckBox.IsChecked == true)
                {
                    var stableHashesPath = Path.Combine(AppContext.BaseDirectory, "StableHashes.json");
                    Log($"Loading StableHashes: {stableHashesPath}");

                    var stable = StableHashesLoader.Load(stableHashesPath);

                    var injector = new StableTexturesInjector(stable);
                    Log("Scanning mod.ini files for StableTextures injection...");
                    var (injSummary, injPlans) = await Task.Run(() => injector.Preview(folder));

                    Log($"StableTextures - mod.ini scanned: {injSummary.ModIniFound}");
                    Log($"StableTextures - files changed: {injSummary.ModIniChanged}");
                    Log($"StableTextures - components applied: {injSummary.TotalComponentsApplied}");
                    Log($"StableTextures - files skipped (already applied): {injSummary.FilesSkippedAlreadyApplied}");

                    if (injSummary.AnyChange)
                    {
                        Log("Applying StableTextures injection (creating backups first)...");
                        await Task.Run(() => injector.ExecutePlans(injPlans));
                        Log("StableTextures injection completed.");
                    }
                    else
                    {
                        Log("StableTextures injection: no changes needed.");
                    }
                }

                System.Windows.MessageBox.Show("Completed successfully.", "Woju");
                Log("Completed successfully.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Woju");
                Log("ERROR: " + ex);
            }
            finally
            {
                RunButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            Log("Log cleared.");
        }

        private void BrowseWojuModsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://www.youtube.com/@Woju25");
        }

        private void DownloadRabbitFxButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://gamebanana.com/mods/527815");
        }

        private void WojuKofiButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ko-fi.com/wo_ju");
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void Log(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }
}