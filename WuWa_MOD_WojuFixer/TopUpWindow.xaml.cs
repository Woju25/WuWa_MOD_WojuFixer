using System;
using System.Diagnostics;
using System.Windows;

namespace WuWa_MOD_WojuFixer
{
    public partial class TopUpWindow : Window
    {
        private const string TopUpUrl =
            "https://lootbar.com/top-up/wuthering-waves?aff_short=Woju";

        private const string GuideUrl =
            "https://youtu.be/yECO_raIjFk";

        public TopUpWindow()
        {
            InitializeComponent();
        }

        private void TopUpButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(TopUpUrl);
        }

        private void TopUpGuideButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(GuideUrl);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                                    $"Unable to open the link.\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}