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
        private CancellationTokenSource _treeCts;

        // Static cache for directory tree to avoid re-scanning on new tab
        private static FolderNode _cachedTreeRoot;
        private static string _cachedTreeFolder;

        // Static cache for expanded folder paths
        private static readonly HashSet<string> _cachedExpandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _expandedPathsLoaded = false;
        private const string ExpandedFoldersFileName = "expanded_folders.txt";

        // Debounce timer: batch disk writes after 600ms of no expand/collapse activity
        private static System.Threading.Timer _saveDebounceTimer;
        private static readonly object _saveTimerLock = new object();

        private static void LoadExpandedFolders()
        {
            if (_expandedPathsLoaded) return;
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(appDir, ExpandedFoldersFileName);
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        string path = line.Trim();
                        if (!string.IsNullOrEmpty(path))
                            _cachedExpandedPaths.Add(path);
                    }
                }
            }
            catch { }
            _expandedPathsLoaded = true;
        }

        // Debounced: resets 600ms timer on each call; only saves after activity stops
        private static void SaveExpandedFoldersDebounced()
        {
            lock (_saveTimerLock)
            {
                if (_saveDebounceTimer == null)
                    _saveDebounceTimer = new System.Threading.Timer(_ => SaveExpandedFoldersToDisk(), null, 600, System.Threading.Timeout.Infinite);
                else
                    _saveDebounceTimer.Change(600, System.Threading.Timeout.Infinite);
            }
        }

        // Immediate save on background thread (used when changing root folder)
        private static void SaveExpandedFolders()
        {
            SaveExpandedFoldersToDisk();
        }

        private static void SaveExpandedFoldersToDisk()
        {
            Task.Run(() =>
            {
                try
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string filePath = Path.Combine(appDir, ExpandedFoldersFileName);
                    string[] snapshot;
                    lock (_cachedExpandedPaths)
                    {
                        snapshot = _cachedExpandedPaths.ToArray();
                    }
                    File.WriteAllLines(filePath, snapshot);
                }
                catch { }
            });
        }

        // Event to notify parent window to open files
        public event EventHandler<string[]> FilesOpenRequested;
        public event EventHandler<FileOpenWithViewerEventArgs> FileOpenWithViewerRequested;

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
            RefreshDirectoryTree();
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
                ClearRecentButton.Visibility = Visibility.Collapsed;
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
                ClearRecentButton.Visibility = Visibility.Visible;
                foreach (string file in files)
                {
                    AddFileButton(file, RecentFilesList);
                }
            }
        }

        private void ClearRecentButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the recent files list?", "Clear Recent Files", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _recentFilesService.Clear();
            }
        }

        private void AddFileButton(string filePath, StackPanel container)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                bool isPdf = filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

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
                    RequestOpenFile(filePath);
                };

                if (!isPdf)
                {
                    ContextMenu contextMenu = new ContextMenu();

                    MenuItem openNexusBvItem = new MenuItem { Header = "Open with NexusBV" };
                    openNexusBvItem.Click += (s, ev) => RequestOpenWithViewer(filePath, "NexusBV");

                    MenuItem openOpenBoardViewItem = new MenuItem { Header = "Open with OpenBoardView" };
                    openOpenBoardViewItem.Click += (s, ev) => RequestOpenWithViewer(filePath, "OpenBoardView");

                    MenuItem openBoardViewerItem = new MenuItem { Header = "Open with BoardViewer" };
                    openBoardViewerItem.Click += (s, ev) => RequestOpenWithViewer(filePath, "BoardViewer");

                    contextMenu.Items.Add(openNexusBvItem);
                    contextMenu.Items.Add(openBoardViewerItem);
                    contextMenu.Items.Add(openOpenBoardViewItem);


                    fileButton.ContextMenu = contextMenu;
                }

                container.Children.Add(fileButton);
            }
            catch { }
        }

        private void RequestOpenFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                FilesOpenRequested?.Invoke(this, new string[] { filePath });
                return;
            }

            MessageBox.Show($"File not found:\n{filePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            _recentFilesService.RemoveFile(filePath);
        }

        private void RequestOpenWithViewer(string filePath, string viewerType)
        {
            if (File.Exists(filePath))
            {
                FileOpenWithViewerRequested?.Invoke(this, new FileOpenWithViewerEventArgs(filePath, viewerType));
                return;
            }

            MessageBox.Show($"File not found:\n{filePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            _recentFilesService.RemoveFile(filePath);
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
                DirectoryTreePanel.Visibility = Visibility.Visible;
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
                DirectoryTreePanel.Visibility = Visibility.Collapsed;
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
            DirectoryTreePanel.Visibility = Visibility.Collapsed;

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
                    lock (_cachedExpandedPaths)
                    {
                        _cachedExpandedPaths.Clear();
                    }
                    SaveExpandedFolders();
                    RefreshDirectoryTree(forceRefresh: true);
                }
            }
        }

        // --- Other UI Handlers ---

        public void ToggleSidebar()
        {
            if (LeftColumn.Width.Value == 0)
            {
                LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                LeftColumn.Width = new GridLength(0);
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
                Filter = "All Supported Files|*.pdf;*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|PDF Files|*.pdf|NexusBV Files|*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|All Files|*.*",
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

        // --- Directory Tree Logic ---

        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".fz", ".brd", ".bom", ".cad", ".bdv", ".asc", ".bv", ".cst", ".gr", ".f2b", ".faz", ".tvw"
        };

        private void RefreshTreeButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDirectoryTree(forceRefresh: true);
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in FolderTreeView.Items)
            {
                if (item is TreeViewItem tvi)
                {
                    // Collapse children of root
                    foreach (var child in tvi.Items)
                    {
                        if (child is TreeViewItem childTvi)
                        {
                            CollapseAllItems(childTvi);
                        }
                    }
                }
            }
        }

        private void CollapseAllItems(TreeViewItem item)
        {
            if (item == null || !(item.Tag is FolderNode)) return;

            // Recursively collapse child items first to ensure proper event propagation
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childTvi)
                {
                    CollapseAllItems(childTvi);
                }
            }

            // Collapse this item if it is expanded
            if (item.IsExpanded)
            {
                item.IsExpanded = false;
            }
        }

        private async void RefreshDirectoryTree(bool forceRefresh = false)
        {
            _treeCts?.Cancel();
            _treeCts = new CancellationTokenSource();
            var token = _treeCts.Token;

            FolderTreeView.Items.Clear();
            string folder = _searchService.SearchFolder;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                TreePlaceholder.Text = "Please select a search folder first (click 📁)";
                TreePlaceholder.Visibility = Visibility.Visible;
                _cachedTreeRoot = null;
                _cachedTreeFolder = null;
                return;
            }

            // Use cache if available and not forcing a refresh
            if (!forceRefresh && _cachedTreeRoot != null && _cachedTreeFolder == folder)
            {
                TreePlaceholder.Visibility = Visibility.Collapsed;
                PopulateTreeView(_cachedTreeRoot);
                return;
            }

            TreePlaceholder.Text = "Scanning directory tree...";
            TreePlaceholder.Visibility = Visibility.Visible;

            try
            {
                var rootNode = await BuildFilteredDirectoryTreeAsync(folder, token);

                if (token.IsCancellationRequested) return;

                if (rootNode == null)
                {
                    TreePlaceholder.Text = "No PDF or boardview files found in this folder.";
                    TreePlaceholder.Visibility = Visibility.Visible;
                    _cachedTreeRoot = null;
                    _cachedTreeFolder = null;
                }
                else
                {
                    TreePlaceholder.Visibility = Visibility.Collapsed;

                    // Update cache
                    _cachedTreeRoot = rootNode;
                    _cachedTreeFolder = folder;

                    PopulateTreeView(rootNode);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    TreePlaceholder.Text = $"Error scanning folder:\n{ex.Message}";
                    TreePlaceholder.Visibility = Visibility.Visible;
                    _cachedTreeRoot = null;
                    _cachedTreeFolder = null;
                }
            }
        }

        private async Task<FolderNode> BuildFilteredDirectoryTreeAsync(string path, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return BuildFilteredDirectoryTreeInternal(path, token);
                }
                catch
                {
                    return null;
                }
            }, token);
        }

        private FolderNode BuildFilteredDirectoryTreeInternal(string path, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;

            var node = new FolderNode
            {
                Name = Path.GetFileName(path),
                FullPath = path
            };

            // Get files in this directory
            try
            {
                var files = Directory.GetFiles(path);
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) return null;

                    string ext = Path.GetExtension(file);
                    if (SupportedExtensions.Contains(ext))
                    {
                        node.Files.Add(new FileNode
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file
                        });
                    }
                }
            }
            catch { }

            // Get subdirectories
            try
            {
                var subDirs = Directory.GetDirectories(path);
                foreach (var subDir in subDirs)
                {
                    if (token.IsCancellationRequested) return null;

                    string dirName = Path.GetFileName(subDir);
                    if (string.IsNullOrEmpty(dirName)) continue;
                    if (dirName.StartsWith("$") || 
                        dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var childNode = BuildFilteredDirectoryTreeInternal(subDir, token);
                    if (childNode != null)
                    {
                        node.SubFolders.Add(childNode);
                    }
                }
            }
            catch { }

            // Return node if it contains files or subfolders with files
            if (node.Files.Count > 0 || node.SubFolders.Count > 0)
            {
                return node;
            }

            return null;
        }

        private void PopulateTreeView(FolderNode rootNode)
        {
            FolderTreeView.Items.Clear();
            if (rootNode == null) return;

            // Load once before building the tree (not inside each node)
            LoadExpandedFolders();

            TreeViewItem rootItem = CreateFolderTreeViewItem(rootNode);
            rootItem.IsExpanded = true; // Always expand root
            FolderTreeView.Items.Add(rootItem);
        }

        private TreeViewItem CreateFolderTreeViewItem(FolderNode folderNode)
        {
            var item = new TreeViewItem
            {
                Header = CreateHeaderPanel("📁", folderNode.Name),
                Tag = folderNode,
                Margin = new Thickness(0, 2, 0, 2)
            };

            bool hasChildren = folderNode.SubFolders.Count > 0 || folderNode.Files.Count > 0;
            if (!hasChildren) return item;

            // Add dummy placeholder so WPF renders the expand arrow without building children yet
            item.Items.Add(new TreeViewItem());
            bool childrenLoaded = false;

            item.Expanded += (s, e) =>
            {
                e.Handled = true;
                lock (_cachedExpandedPaths) { _cachedExpandedPaths.Add(folderNode.FullPath); }
                SaveExpandedFoldersDebounced();

                // Lazy load: build child UI only on first expand
                if (!childrenLoaded)
                {
                    childrenLoaded = true;
                    item.Items.Clear();
                    foreach (var subFolder in folderNode.SubFolders)
                        item.Items.Add(CreateFolderTreeViewItem(subFolder));
                    foreach (var file in folderNode.Files)
                        item.Items.Add(CreateFileTreeViewItem(file));
                }
            };

            item.Collapsed += (s, e) =>
            {
                e.Handled = true;
                lock (_cachedExpandedPaths) { _cachedExpandedPaths.Remove(folderNode.FullPath); }
                SaveExpandedFoldersDebounced();
            };

            // Restore previously expanded state (LoadExpandedFolders already called once)
            if (_cachedExpandedPaths.Contains(folderNode.FullPath))
                item.IsExpanded = true;

            return item;
        }

        private TreeViewItem CreateFileTreeViewItem(FileNode fileNode)
        {
            string icon = fileNode.FullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "📕" : "📘";
            var item = new TreeViewItem
            {
                Header = CreateHeaderPanel(icon, fileNode.Name),
                Tag = fileNode,
                Margin = new Thickness(0, 1, 0, 1)
            };

            item.Selected += (s, e) =>
            {
                // Prevent event bubbling to parent folder nodes
                e.Handled = true;
            };

            item.PreviewMouseLeftButtonUp += (s, e) =>
            {
                RequestOpenFile(fileNode.FullPath);
                e.Handled = true;
            };

            // Context menu for boardview files
            bool isPdf = fileNode.FullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf)
            {
                ContextMenu contextMenu = new ContextMenu();

                MenuItem openNexusBvItem = new MenuItem { Header = "Open with NexusBV" };
                openNexusBvItem.Click += (s, ev) => RequestOpenWithViewer(fileNode.FullPath, "NexusBV");

                MenuItem openOpenBoardViewItem = new MenuItem { Header = "Open with OpenBoardView" };
                openOpenBoardViewItem.Click += (s, ev) => RequestOpenWithViewer(fileNode.FullPath, "OpenBoardView");

                MenuItem openBoardViewerItem = new MenuItem { Header = "Open with BoardViewer" };
                openBoardViewerItem.Click += (s, ev) => RequestOpenWithViewer(fileNode.FullPath, "BoardViewer");

                contextMenu.Items.Add(openNexusBvItem);
                contextMenu.Items.Add(openBoardViewerItem);
                contextMenu.Items.Add(openOpenBoardViewItem);

                item.ContextMenu = contextMenu;
            }

            return item;
        }

        private StackPanel CreateHeaderPanel(string icon, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 13, 
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = text, 
                FontSize = 13, 
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                VerticalAlignment = VerticalAlignment.Center 
            });
            return panel;
        }
    }

    public class FolderNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public List<FolderNode> SubFolders { get; set; } = new List<FolderNode>();
        public List<FileNode> Files { get; set; } = new List<FileNode>();
    }

    public class FileNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
    }

    public class FileOpenWithViewerEventArgs : EventArgs
    {
        public FileOpenWithViewerEventArgs(string filePath, string viewerType)
        {
            FilePath = filePath;
            ViewerType = viewerType;
        }

        public string FilePath { get; }
        public string ViewerType { get; }
    }
}
