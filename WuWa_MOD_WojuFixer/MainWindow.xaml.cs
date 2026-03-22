using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;
using WuWa_MOD_WojuFixer.Core;

namespace WuWa_MOD_WojuFixer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Log("Ready (Woju).");
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

                if (!summary.AnyChange)
                {
                    System.Windows.MessageBox.Show("No changes needed.", "Woju");
                    Log("No changes needed. No files were modified.");
                    return;
                }

                Log("Applying changes (creating backups first)...");
                await Task.Run(() => processor.ExecutePlans(plans));

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

        private void Log(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }
}