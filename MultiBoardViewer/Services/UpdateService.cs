using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
                            // Find zip asset for automatic update
                            string downloadUrl = null;
                            if (root.TryGetProperty("assets", out JsonElement assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement asset in assetsElement.EnumerateArray())
                                {
                                    string name = asset.GetProperty("name").GetString();
                                    if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                        break;
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(downloadUrl))
                            {
                                // Fallback to manual download if zip asset not found
                                var result = MessageBox.Show(
                                    $"A new version ({latestVersionTag}) is available!\n\n" +
                                    $"Release Notes:\n{body}\n\n" +
                                    "Automatic download package was not found. Do you want to go to the download page?",
                                    "Update Available",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Information);

                                if (result == MessageBoxResult.Yes)
                                {
                                    Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true });
                                }
                                return;
                            }

                            var updateResult = MessageBox.Show(
                                $"A new version ({latestVersionTag}) is available!\n\n" +
                                $"Release Notes:\n{body}\n\n" +
                                "Do you want to download and install this update automatically?\n\n" +
                                "[Yes] = Install automatically\n" +
                                "[No] = Go to release web page\n" +
                                "[Cancel] = Remind me later",
                                "Update Available",
                                MessageBoxButton.YesNoCancel,
                                MessageBoxImage.Question);

                            if (updateResult == MessageBoxResult.Yes)
                            {
                                await InstallUpdateAsync(downloadUrl);
                            }
                            else if (updateResult == MessageBoxResult.No)
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

        private async Task InstallUpdateAsync(string downloadUrl)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "mbv_update.zip");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), "mbv_update_extracted");

            // Disable main window to prevent user actions
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.IsEnabled = false;
            }

            // Create borderless, modern update progress window
            Window progressWindow = new Window
            {
                Title = "Updating Multi-BoardViewer",
                Width = 360,
                Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            Border mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(215, 215, 215)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20)
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock titleText = new TextBlock
            {
                Text = "Installing Update",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30))
            };
            Grid.SetRow(titleText, 0);
            mainGrid.Children.Add(titleText);

            TextBlock statusText = new TextBlock
            {
                Text = "Downloading update package...",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
            Grid.SetRow(statusText, 2);
            mainGrid.Children.Add(statusText);

            ProgressBar progressBar = new ProgressBar
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                BorderThickness = new Thickness(0)
            };

            Grid.SetRow(progressBar, 4);
            mainGrid.Children.Add(progressBar);

            mainBorder.Child = mainGrid;
            progressWindow.Content = mainBorder;

            progressWindow.Show();

            try
            {
                // 1. Download ZIP
                await DownloadUpdateAsync(downloadUrl, tempZipPath, percentage =>
                {
                    progressWindow.Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percentage;
                        statusText.Text = $"Downloading update... {percentage}%";
                    });
                });

                // 2. Extract ZIP
                progressWindow.Dispatcher.Invoke(() =>
                {
                    progressBar.IsIndeterminate = true;
                    statusText.Text = "Extracting files...";
                });

                await Task.Run(() =>
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                    Directory.CreateDirectory(tempExtractDir);
                    ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);
                });

                // 3. Prepare updater script
                progressWindow.Dispatcher.Invoke(() =>
                {
                    statusText.Text = "Applying update...";
                });

                string currentExePath = Environment.ProcessPath;
                string currentAppDir = AppDomain.CurrentDomain.BaseDirectory;
                string processName = Path.GetFileName(currentExePath);

                // Identify if zip has single folder or root files
                string sourceDir = tempExtractDir;
                var subDirs = Directory.GetDirectories(tempExtractDir);
                var files = Directory.GetFiles(tempExtractDir);
                if (subDirs.Length == 1 && files.Length == 0)
                {
                    sourceDir = subDirs[0];
                }

                // Write batch script to temp directory
                string batchContent = $@"@echo off
title Multi-BoardViewer Updater
echo Waiting for Multi-BoardViewer to exit...

:loop
tasklist /FI ""IMAGENAME eq {processName}"" 2>NUL | find /I /N ""{processName}"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto loop
)

echo Updating files...
xcopy /E /I /Y ""{sourceDir}\*"" ""{currentAppDir}""

echo Restarting Multi-BoardViewer...
start """" ""{currentExePath}""

echo Done. Cleaning up...
(goto) 2>nul & del ""%~f0""
";

                string batchPath = Path.Combine(Path.GetTempPath(), "mbv_update.bat");
                await File.WriteAllTextAsync(batchPath, batchContent, System.Text.Encoding.ASCII);

                // Execute batch script
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                Process.Start(startInfo);

                // Shutdown app to allow file overwriting
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                // Re-enable main window in case of failure
                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.IsEnabled = true;
                }
                progressWindow.Close();
                MessageBox.Show($"Failed to install update:\n{ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DownloadUpdateAsync(string downloadUrl, string tempZipPath, Action<int> progressCallback)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "MultiBoardViewer-Updater");
                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        var read = 0;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes != -1)
                            {
                                var percentage = (int)((totalRead * 100) / totalBytes);
                                progressCallback(percentage);
                            }
                        }
                    }
                }
            }
        }
    }
}
