using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MultiBoardViewer.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "mhqb365";
        private const string RepoName = "Multi-BoardViewer";
        private const string GitHubApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        public async Task CheckForUpdatesAsync(bool showUpToDateMessage = false)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // GitHub API requires a User-Agent header
                    client.DefaultRequestHeaders.Add("User-Agent", "MultiBoardViewer-Updater");

                    string response = await client.GetStringAsync(GitHubApiUrl);
                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        JsonElement root = doc.RootElement;
                        string latestVersionTag = root.GetProperty("tag_name").GetString();
                        string htmlUrl = root.GetProperty("html_url").GetString();
                        string body = root.GetProperty("body").GetString();

                        // Remove 'v' prefix if present
                        string latestVersionStr = latestVersionTag.StartsWith("v") ? latestVersionTag.Substring(1) : latestVersionTag;
                        
                        Version latestVersion = new Version(latestVersionStr);
                        Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                        if (latestVersion > currentVersion)
                        {
                            var result = MessageBox.Show(
                                $"A new version ({latestVersionTag}) is available!\n\n" +
                                $"Release Notes:\n{body}\n\n" +
                                "Do you want to go to the download page?",
                                "Update Available",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (result == MessageBoxResult.Yes)
                            {
                                Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true });
                            }
                        }
                        else if (showUpToDateMessage)
                        {
                            MessageBox.Show(
                                $"You are using the latest version ({currentVersion.ToString(3)}).",
                                "Up to Date",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (showUpToDateMessage)
                {
                    MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }
    }
}
