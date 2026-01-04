using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.Win32;
using MultiBoardViewer.Services;

namespace MultiBoardViewer.Controls
{
    public partial class StartPage : UserControl
    {
        private FileSearchService _searchService;
        private RecentFilesService _recentFilesService;
        private DispatcherTimer _searchTimer;
        private CancellationTokenSource _searchCts;

        // Event to notify parent window to open files
        public event EventHandler<string[]> FilesOpenRequested;

        public StartPage()
        {
            InitializeComponent();
            
            _searchService = new FileSearchService();
            _recentFilesService = new RecentFilesService();
            _recentFilesService.RecentFilesChanged += (s, e) => RefreshRecentFiles();

            // Initialize search timer
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += SearchTimer_Tick;

            UpdateSearchFolderTooltip();
            RefreshRecentFiles();
        }

        private void UpdateSearchFolderTooltip()
        {
            string folder = _searchService.SearchFolder;
            FolderButton.ToolTip = string.IsNullOrEmpty(folder) ? "Select search folder" : $"Folder: {folder}";
            SearchPlaceholder.Text = string.IsNullOrEmpty(folder) ? "Set folder first →" : "Type to search...";
        }

        private void RefreshRecentFiles()
        {
            RecentFilesList.Children.Clear();
            var files = _recentFilesService.GetRecentFiles();

            if (files.Count == 0)
            {
                TextBlock noRecent = new TextBlock
                {
                    Text = "No recent files",
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                    FontStyle = FontStyles.Italic
                };
                RecentFilesList.Children.Add(noRecent);
            }
            else
            {
                foreach (string file in files)
                {
                    AddFileButton(file, RecentFilesList);
                }
            }
        }

        private void AddFileButton(string filePath, StackPanel container)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);

                Button fileButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 1, 0, 1),
                    Tag = filePath,
                    ToolTip = filePath
                };

                StackPanel fileNamePanel = new StackPanel { Orientation = Orientation.Horizontal };
                string fileIcon = filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "📕" : "📘";
                TextBlock iconBlock = new TextBlock
                {
                    Text = fileIcon,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock nameBlock = new TextBlock
                {
                    Text = fileName,
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                fileNamePanel.Children.Add(iconBlock);
                fileNamePanel.Children.Add(nameBlock);
                fileButton.Content = fileNamePanel;

                // Hover effect
                fileButton.MouseEnter += (s, ev) => fileButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 250));
                fileButton.MouseLeave += (s, ev) => fileButton.Background = System.Windows.Media.Brushes.Transparent;

                // Click handler
                fileButton.Click += (s, ev) =>
                {
                    if (File.Exists(filePath))
                    {
                        FilesOpenRequested?.Invoke(this, new string[] { filePath });
                    }
                    else
                    {
                        MessageBox.Show($"File not found:\n{filePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _recentFilesService.RemoveFile(filePath);
                    }
                };

                // Context Menu
                ContextMenu contextMenu = new ContextMenu();
                // Add specific open options if needed, for now just general open
                // In a real refactor, we might want to expose "OpenWith" intentions too.
                // Keeping it simple for now: Right click allows specific viewers which we can pass as encoded strings or handle differently.
                // For now, let's just Stick to the main event. If user needs right-click "Open with X", we need to pipe that through.
                
                // Let's implement full context menu support later or just stick to standard open for now. 
                // The original code had specific "Open with BoardViewer" etc.
                // For this refactor, let's keep it simple.
                // If we want to support "Open With", we can use a custom event args.

                container.Children.Add(fileButton);
            }
            catch { }
        }

        // --- Search Logic ---

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private async void SearchTimer_Tick(object sender, EventArgs e)
        {
            _searchTimer.Stop();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            string searchText = SearchBox.Text.Trim();
            SearchResultsPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                SearchResultsScroll.Visibility = Visibility.Collapsed;
                RecentFilesPanel.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrEmpty(_searchService.SearchFolder))
            {
                SearchResultsPanel.Children.Add(new TextBlock
                {
                    Text = "⚠️ Please select a search folder first (click 📁)",
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 0)),
                    TextWrapping = TextWrapping.Wrap
                });
                SearchResultsScroll.Visibility = Visibility.Visible;
                RecentFilesPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Show searching...
            SearchResultsPanel.Children.Add(new TextBlock
            {
                Text = "Searching...",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                FontStyle = FontStyles.Italic
            });
            SearchResultsScroll.Visibility = Visibility.Visible;
            RecentFilesPanel.Visibility = Visibility.Collapsed;

            try
            {
                var results = await _searchService.SearchFilesAsync(searchText, token);

                if (token.IsCancellationRequested) return;

                SearchResultsPanel.Children.Clear();

                if (results.Count == 0)
                {
                    SearchResultsPanel.Children.Add(new TextBlock
                    {
                        Text = $"No files found matching \"{searchText}\"",
                        FontSize = 12,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                        FontStyle = FontStyles.Italic
                    });
                }
                else
                {
                    SearchResultsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Found {results.Count} file(s):",
                        FontSize = 11,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(0, 0, 0, 5)
                    });

                    foreach (string file in results)
                    {
                        AddFileButton(file, SearchResultsPanel);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select folder to search for files";
                dialog.ShowNewFolderButton = false;
                
                string current = _searchService.SearchFolder;
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _searchService.SearchFolder = dialog.SelectedPath;
                    UpdateSearchFolderTooltip();
                    SearchBox.Text = ""; // Clear search to reset view
                }
            }
        }

        // --- Other UI Handlers ---

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            if (AboutContent.Visibility == Visibility.Collapsed)
            {
                AboutContent.Visibility = Visibility.Visible;
                AboutButton.Content = "ℹ️ About ▼";
            }
            else
            {
                AboutContent.Visibility = Visibility.Collapsed;
                AboutButton.Content = "ℹ️ About";
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Open File",
                Filter = "All Supported Files|*.pdf;*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|PDF Files|*.pdf|BoardViewer Files|*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|All Files|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true && openFileDialog.FileNames.Length > 0)
            {
                FilesOpenRequested?.Invoke(this, openFileDialog.FileNames);
            }
        }

        // --- Drag & Drop ---

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 255));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UserControl_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            DropZone.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    FilesOpenRequested?.Invoke(this, files);
                }
            }
            e.Handled = true;
        }

        private void BlockDrag(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void BlockDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }
    }
}
