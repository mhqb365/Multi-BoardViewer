using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MultiBoardViewer
{
    public partial class MainWindow : Window
    {
        // Windows API declarations for embedding external processes
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // For disabling drag & drop on embedded windows
        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private const uint GW_CHILD = 5;
        private const uint WM_SIZE = 0x0005;
        private const int SIZE_RESTORED = 0;

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_EX_DLGMODALFRAME = 0x00000001;
        private const int WS_EX_WINDOWEDGE = 0x00000100;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const int WS_EX_STATICEDGE = 0x00020000;
        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;
        private const int SW_MAXIMIZE = 3;

        // Dictionary to track processes and their containers
        private Dictionary<TabItem, ProcessInfo> _tabProcesses = new Dictionary<TabItem, ProcessInfo>();
        private int _tabCounter = 1;
        private int _sumatraTabCounter = 1;
        private string _boardViewerPath = "";
        private string _sumatraPdfPath = "";
        private DispatcherTimer _resizeTimer;
        private bool _dropHandled = false; // Flag to prevent double drop handling
        private List<string> _recentFiles = new List<string>(); // Recent files history
        private const int MaxRecentFiles = 10;
        private const string RecentFilesFileName = "recent_files.txt";
        private string _searchFolder = ""; // Folder for file search
        private const string SearchFolderFileName = "search_folder.txt";

        private class ProcessInfo
        {
            public Process Process { get; set; }
            public WindowsFormsHost Host { get; set; }
            public System.Windows.Forms.Panel Panel { get; set; }
            public string TempDirectory { get; set; }
            public string AppType { get; set; } // "BoardViewer" or "SumatraPDF"
            public IntPtr WindowHandle { get; set; } // Store window handle for resize
            public string FilePath { get; set; } // Store the file path being viewed
        }

        // Check if file is already open and switch to that tab
        private bool TrySwitchToExistingTab(string filePath)
        {
            foreach (var kvp in _tabProcesses)
            {
                if (kvp.Value.FilePath != null && 
                    kvp.Value.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    // File is already open, switch to that tab
                    tabControl.SelectedItem = kvp.Key;
                    ShowStatus($"Switched to existing tab: {Path.GetFileName(filePath)}", true);
                    return true;
                }
            }
            return false;
        }

        // Check if a file with the same name is already open and switch to that tab
        private bool TrySwitchToExistingTabByFileName(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            foreach (var kvp in _tabProcesses)
            {
                if (kvp.Value.FilePath != null)
                {
                    string existingFileName = Path.GetFileName(kvp.Value.FilePath);
                    if (existingFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        // File with same name is already open, switch to that tab
                        tabControl.SelectedItem = kvp.Key;
                        ShowStatus($"Switched to existing tab: {fileName}", true);
                        return true;
                    }
                }
            }
            return false;
        }

        // Load recent files from file
        private void LoadRecentFiles()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string recentFilePath = Path.Combine(appDir, RecentFilesFileName);
                
                if (File.Exists(recentFilePath))
                {
                    _recentFiles = File.ReadAllLines(recentFilePath)
                        .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f))
                        .Take(MaxRecentFiles)
                        .ToList();
                }
            }
            catch { }
        }

        // Save recent files to file
        private void SaveRecentFiles()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string recentFilePath = Path.Combine(appDir, RecentFilesFileName);
                File.WriteAllLines(recentFilePath, _recentFiles);
            }
            catch { }
        }

        // Load search folder from file
        private void LoadSearchFolder()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string searchFolderPath = Path.Combine(appDir, SearchFolderFileName);
                
                if (File.Exists(searchFolderPath))
                {
                    string folder = File.ReadAllText(searchFolderPath).Trim();
                    if (Directory.Exists(folder))
                    {
                        _searchFolder = folder;
                    }
                }
            }
            catch { }
        }

        // Save search folder to file
        private void SaveSearchFolder()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string searchFolderPath = Path.Combine(appDir, SearchFolderFileName);
                File.WriteAllText(searchFolderPath, _searchFolder);
            }
            catch { }
        }

        // Supported file extensions
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".fz", ".brd", ".bom", ".cad", ".bdv", ".asc", ".bv", ".cst", ".gr", ".f2b", ".faz", ".tvw"
        };

        // Search files in the configured folder (async version)
        private async System.Threading.Tasks.Task<List<string>> SearchFilesAsync(string searchText, CancellationToken cancellationToken)
        {
            var results = new List<string>();
            
            if (string.IsNullOrEmpty(_searchFolder) || !Directory.Exists(_searchFolder))
                return results;
            
            if (string.IsNullOrWhiteSpace(searchText))
                return results;

            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(_searchFolder, "*", SearchOption.AllDirectories);
                        int count = 0;
                        
                        foreach (var f in files)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                            
                            if (count >= 50) // Limit results
                                break;
                            
                            try
                            {
                                string fileName = Path.GetFileName(f);
                                string ext = Path.GetExtension(f);
                                
                                if (SupportedExtensions.Contains(ext) && 
                                    fileName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    results.Add(f);
                                    count++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }, cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch { }
            
            return results;
        }

        // Add file to recent history
        private void AddToRecentFiles(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            // Remove if already exists (to move it to top)
            _recentFiles.RemoveAll(f => f.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            
            // Add to top
            _recentFiles.Insert(0, filePath);
            
            // Keep only MaxRecentFiles
            if (_recentFiles.Count > MaxRecentFiles)
            {
                _recentFiles = _recentFiles.Take(MaxRecentFiles).ToList();
            }
            
            // Save to file
            SaveRecentFiles();
        }

        public MainWindow()
        {
            InitializeComponent();
            
            // Load recent files history
            LoadRecentFiles();
            
            // Load search folder setting
            LoadSearchFolder();
            
            // Initialize timer for handling window resizing
            _resizeTimer = new DispatcherTimer();
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(100);
            _resizeTimer.Tick += ResizeTimer_Tick;

            // Handle window resize to update embedded processes
            this.SizeChanged += MainWindow_SizeChanged;
            
            // Also handle layout updates for more responsive resizing
            this.LayoutUpdated += MainWindow_LayoutUpdated;

            // Try to find BoardViewer.exe in the same directory or parent directory
            AutoDetectBoardViewerPath();
            
            // Try to find SumatraPDF.exe in app folder
            AutoDetectSumatraPdfPath();
            
            // Create the "+" add tab button
            CreateAddTabButton();
            
            // Subscribe to receive files from other instances
            App.FilesReceived += OnFilesReceivedFromAnotherInstance;
            
            // Check if files were passed via command line (Open with)
            if (App.StartupFiles != null && App.StartupFiles.Length > 0)
            {
                // Open files from command line
                OpenStartupFiles();
            }
            else
            {
                // Create initial empty tab on startup
                CreateEmptyTab();
            }
        }

        private void OnFilesReceivedFromAnotherInstance(string[] files)
        {
            // Bring window to front
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();

            // Open files
            foreach (string file in files)
            {
                // Skip activation command
                if (file == "__ACTIVATE__")
                    continue;
                    
                // Skip if file doesn't exist
                if (!System.IO.File.Exists(file))
                    continue;
                
                if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    OpenPdfInNewTab(file);
                }
                else
                {
                    OpenBoardViewerWithFile(file);
                }
            }
        }

        private void OpenStartupFiles()
        {
            string[] files = App.StartupFiles;
            
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                
                // Skip if file doesn't exist
                if (!System.IO.File.Exists(file))
                    continue;
                
                if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    OpenPdfInNewTab(file);
                }
                else
                {
                    OpenBoardViewerWithFile(file);
                }
            }
            
            // If no valid files were opened, create empty tab
            if (tabControl.Items.Count == 1 && tabControl.Items[0] == _addTabButton)
            {
                CreateEmptyTab();
            }
        }

        private TabItem _addTabButton;
        
        private void CreateAddTabButton()
        {
            // Create a special "+" tab that acts as a button
            _addTabButton = new TabItem();
            
            // Create custom style for the "+" tab (no close button)
            Style addTabStyle = new Style(typeof(TabItem));
            
            ControlTemplate template = new ControlTemplate(typeof(TabItem));
            
            // Create the border
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Border";
            border.SetValue(Border.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224)));
            border.SetValue(Border.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 153, 153)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3, 3, 0, 0));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 5, 12, 5));
            border.SetValue(Border.MarginProperty, new Thickness(2, 2, 2, 0));
            border.SetValue(Border.CursorProperty, Cursors.Hand);
            
            // Create text block for "+"
            FrameworkElementFactory textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.TextProperty, "+");
            textBlock.SetValue(TextBlock.FontSizeProperty, 16.0);
            textBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlock.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            
            border.AppendChild(textBlock);
            template.VisualTree = border;
            
            // Add hover trigger
            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 230, 255)), "Border"));
            template.Triggers.Add(hoverTrigger);
            
            addTabStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
            _addTabButton.Style = addTabStyle;
            
            _addTabButton.ToolTip = "New tab";
            
            // Handle click on "+" tab directly
            _addTabButton.PreviewMouseLeftButtonDown += AddTabButton_Click;
            
            // Add to TabControl
            tabControl.Items.Add(_addTabButton);
        }

        private void AddTabButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent selection change
            if (!_isCreatingTab)
            {
                _isCreatingTab = true;
                CreateEmptyTab();
                _isCreatingTab = false;
            }
        }

        private Size _lastSize = Size.Empty;
        
        private void MainWindow_LayoutUpdated(object sender, EventArgs e)
        {
            // Check if size actually changed to avoid unnecessary updates
            Size currentSize = new Size(this.ActualWidth, this.ActualHeight);
            if (currentSize != _lastSize && _lastSize != Size.Empty)
            {
                ResizeAllSumatraTabs();
            }
            _lastSize = currentSize;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Directly resize all SumatraPDF tabs when window size changes
            ResizeAllSumatraTabs();
            
            // Also trigger timer for other embedded apps
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void ResizeAllSumatraTabs()
        {
            foreach (var kvp in _tabProcesses)
            {
                ProcessInfo processInfo = kvp.Value;
                if (processInfo.AppType == "SumatraPDF" && 
                    processInfo.Process != null && 
                    !processInfo.Process.HasExited)
                {
                    IntPtr childHandle = GetWindow(processInfo.Panel.Handle, GW_CHILD);
                    if (childHandle != IntPtr.Zero)
                    {
                        int width = processInfo.Panel.Width;
                        int height = processInfo.Panel.Height;
                        
                        if (width > 0 && height > 0)
                        {
                            MoveWindow(childHandle, 0, 0, width, height, true);
                            
                            // Send WM_SIZE to notify of size change
                            IntPtr lParam = new IntPtr((height << 16) | (width & 0xFFFF));
                            SendMessage(childHandle, WM_SIZE, new IntPtr(SIZE_RESTORED), lParam);
                            
                            processInfo.WindowHandle = childHandle;
                        }
                    }
                }
            }
        }

        // New Empty Tab button handler
        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
        {
            CreateEmptyTab();
        }

        private void VoltageDividerCalculator_Click(object sender, RoutedEventArgs e)
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltageDividerCalculator", "VoltageDividerCalculator.exe");
            Process.Start(exePath);
        }

        private void CreateEmptyTab()
        {
            // Create new empty tab with 2-column layout
            TabItem newTab = new TabItem
            {
                Header = "New tab"
            };

            // Main container - Grid with 2 columns
            Grid mainGrid = new Grid
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250))
            };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ========== LEFT COLUMN - Recent Files & About ==========
            Border leftBorder = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(20),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)),
                AllowDrop = true // Enable to receive drag events, then block them
            };
            Grid.SetColumn(leftBorder, 0);

            // Block drag & drop events on left column
            leftBorder.DragEnter += (s, ev) =>
            {
                ev.Effects = DragDropEffects.None;
                ev.Handled = true;
            };
            leftBorder.DragOver += (s, ev) =>
            {
                ev.Effects = DragDropEffects.None;
                ev.Handled = true;
            };
            leftBorder.Drop += (s, ev) =>
            {
                ev.Handled = true; // Block the drop
            };

            // Left column grid with 3 rows
            Grid leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search section
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Recent files / Search results
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // About section

            // ----- Search Section -----
            StackPanel searchSection = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(searchSection, 0);

            TextBlock searchTitle = new TextBlock
            {
                Text = "🔍 Search files",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            searchSection.Children.Add(searchTitle);

            // Search box and folder button
            Grid searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox searchBox = new TextBox
            {
                FontSize = 13,
                Padding = new Thickness(8, 6, 28, 6), // Extra padding on right for clear button
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Placeholder text
            TextBlock placeholder = new TextBlock
            {
                Text = string.IsNullOrEmpty(_searchFolder) ? "Set folder first →" : "Type to search...",
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)),
                IsHitTestVisible = false,
                Padding = new Thickness(10, 7, 0, 0)
            };

            // Clear button (X)
            Button clearButton = new Button
            {
                Content = "✕",
                FontSize = 10,
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Clear search"
            };

            // Clear button hover effect
            clearButton.MouseEnter += (s, ev) =>
            {
                clearButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80));
            };
            clearButton.MouseLeave += (s, ev) =>
            {
                clearButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150));
            };

            // Clear button click
            clearButton.Click += (s, ev) =>
            {
                searchBox.Text = "";
                searchBox.Focus();
            };

            Grid searchBoxContainer = new Grid();
            searchBoxContainer.Children.Add(searchBox);
            searchBoxContainer.Children.Add(placeholder);
            searchBoxContainer.Children.Add(clearButton);
            Grid.SetColumn(searchBoxContainer, 0);

            Button folderButton = new Button
            {
                Content = "📁",
                FontSize = 14,
                Width = 36,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = string.IsNullOrEmpty(_searchFolder) ? "Select search folder" : $"Folder: {_searchFolder}",
                Cursor = Cursors.Hand
            };
            Grid.SetColumn(folderButton, 1);

            searchGrid.Children.Add(searchBoxContainer);
            searchGrid.Children.Add(folderButton);
            searchSection.Children.Add(searchGrid);

            // ----- Recent Files Section (declare early for use in search handlers) -----
            StackPanel recentPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(recentPanel, 1);

            // Search results panel (hidden by default, shows when searching)
            ScrollViewer searchResultsScroll = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            StackPanel searchResultsPanel = new StackPanel();
            searchResultsScroll.Content = searchResultsPanel;
            searchSection.Children.Add(searchResultsScroll);

            // Folder button click handler
            folderButton.Click += (s, ev) =>
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Select folder to search for files";
                    dialog.ShowNewFolderButton = false;
                    
                    if (!string.IsNullOrEmpty(_searchFolder) && Directory.Exists(_searchFolder))
                    {
                        dialog.SelectedPath = _searchFolder;
                    }
                    
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _searchFolder = dialog.SelectedPath;
                        SaveSearchFolder();
                        folderButton.ToolTip = $"Folder: {_searchFolder}";
                        placeholder.Text = "Type to search...";
                        ShowStatus($"Search folder set: {_searchFolder}", true);
                    }
                }
            };

            // Search box text changed handler with async and cancellation
            CancellationTokenSource searchCts = null;
            DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += async (s, ev) =>
            {
                searchTimer.Stop();
                
                // Cancel previous search
                searchCts?.Cancel();
                searchCts = new CancellationTokenSource();
                var token = searchCts.Token;
                
                string searchText = searchBox.Text.Trim();
                searchResultsPanel.Children.Clear();

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    searchResultsScroll.Visibility = Visibility.Collapsed;
                    recentPanel.Visibility = Visibility.Visible;
                    return;
                }

                if (string.IsNullOrEmpty(_searchFolder))
                {
                    TextBlock noFolder = new TextBlock
                    {
                        Text = "⚠️ Please select a search folder first (click 📁)",
                        FontSize = 12,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 0)),
                        TextWrapping = TextWrapping.Wrap
                    };
                    searchResultsPanel.Children.Add(noFolder);
                    searchResultsScroll.Visibility = Visibility.Visible;
                    recentPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                // Show searching indicator
                TextBlock searchingText = new TextBlock
                {
                    Text = "Searching...",
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                    FontStyle = FontStyles.Italic
                };
                searchResultsPanel.Children.Add(searchingText);
                searchResultsScroll.Visibility = Visibility.Visible;
                recentPanel.Visibility = Visibility.Collapsed;

                // Search async
                List<string> results;
                try
                {
                    results = await SearchFilesAsync(searchText, token);
                }
                catch (OperationCanceledException)
                {
                    return; // Search was cancelled
                }

                // Check if cancelled while searching
                if (token.IsCancellationRequested)
                    return;

                searchResultsPanel.Children.Clear();

                if (results.Count == 0)
                {
                    TextBlock noResults = new TextBlock
                    {
                        Text = $"No files found matching \"{searchText}\"",
                        FontSize = 12,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                        FontStyle = FontStyles.Italic
                    };
                    searchResultsPanel.Children.Add(noResults);
                }
                else
                {
                    TextBlock resultCount = new TextBlock
                    {
                        Text = $"Found {results.Count} file(s):",
                        FontSize = 11,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    searchResultsPanel.Children.Add(resultCount);

                    foreach (string filePath in results)
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
                            Cursor = Cursors.Hand,
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

                        fileButton.MouseEnter += (sender, args) =>
                        {
                            fileButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 250));
                        };
                        fileButton.MouseLeave += (sender, args) =>
                        {
                            fileButton.Background = System.Windows.Media.Brushes.Transparent;
                        };

                        string capturedPath = filePath;
                        fileButton.Click += (sender, args) =>
                        {
                            if (File.Exists(capturedPath))
                            {
                                if (TrySwitchToExistingTabByFileName(capturedPath))
                                    return;

                                if (capturedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                {
                                    OpenPdfInTab(newTab, capturedPath);
                                }
                                else
                                {
                                    OpenBoardViewerInTab(newTab, capturedPath);
                                }
                            }
                            else
                            {
                                MessageBox.Show($"File not found:\n{capturedPath}", "File Not Found",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };

                        searchResultsPanel.Children.Add(fileButton);
                    }
                }

                searchResultsScroll.Visibility = Visibility.Visible;
                recentPanel.Visibility = Visibility.Collapsed;
            };

            searchBox.TextChanged += (s, ev) =>
            {
                bool hasText = !string.IsNullOrEmpty(searchBox.Text);
                placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
                clearButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
                searchTimer.Stop();
                searchTimer.Start();
            };

            leftGrid.Children.Add(searchSection);

            // ----- Recent Files Content -----
            TextBlock recentTitle = new TextBlock
            {
                Text = "📋 Recent files",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            recentPanel.Children.Add(recentTitle);

            // Recent files list (compact)
            StackPanel recentFilesList = new StackPanel();
            
            if (_recentFiles.Count == 0)
            {
                TextBlock noRecent = new TextBlock
                {
                    Text = "No recent files",
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                    FontStyle = FontStyles.Italic
                };
                recentFilesList.Children.Add(noRecent);
            }
            else
            {
                foreach (string filePath in _recentFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    
                    // Create compact button for each recent file
                    Button fileButton = new Button
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(8, 5, 8, 5),
                        Margin = new Thickness(0, 1, 0, 1),
                        Cursor = Cursors.Hand,
                        Tag = filePath,
                        ToolTip = filePath // Show full path on hover
                    };

                    // File icon and name (single line, compact)
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
                    fileButton.MouseEnter += (s, ev) =>
                    {
                        fileButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 250));
                    };
                    fileButton.MouseLeave += (s, ev) =>
                    {
                        fileButton.Background = System.Windows.Media.Brushes.Transparent;
                    };

                    // Click to open file
                    string capturedPath = filePath; // Capture for closure
                    fileButton.Click += (s, ev) =>
                    {
                        if (File.Exists(capturedPath))
                        {
                            // Check if file with same name is already open
                            if (TrySwitchToExistingTabByFileName(capturedPath))
                            {
                                return; // Switched to existing tab
                            }
                            
                            if (capturedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                OpenPdfInTab(newTab, capturedPath);
                            }
                            else
                            {
                                OpenBoardViewerInTab(newTab, capturedPath);
                            }
                        }
                        else
                        {
                            MessageBox.Show($"File not found:\n{capturedPath}", "File Not Found", 
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            // Remove from recent files
                            _recentFiles.Remove(capturedPath);
                            SaveRecentFiles();
                        }
                    };

                    recentFilesList.Children.Add(fileButton);
                }
            }

            recentPanel.Children.Add(recentFilesList);
            leftGrid.Children.Add(recentPanel);

            // ----- About Section -----
            StackPanel aboutSection = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(aboutSection, 2);

            // About content (hidden by default)
            Border aboutContent = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };

            StackPanel aboutContentPanel = new StackPanel();
            
            TextBlock appName = new TextBlock
            {
                Text = "Multi BoardViewer",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 100, 180)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            TextBlock appVersion = new TextBlock
            {
                Text = "Version 1.0.3",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            TextBlock appDesc = new TextBlock
            {
                Text = "A multi-tab viewer for BoardViewer files and PDF documents",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBlock authorLabel = new TextBlock
            {
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            authorLabel.Inlines.Add("Created by ");
            
            System.Windows.Documents.Hyperlink authorLink = new System.Windows.Documents.Hyperlink
            {
                NavigateUri = new Uri("https://mhqb365.com"),
                TextDecorations = null
            };
            authorLink.Inlines.Add("mhqb365.com");
            authorLink.RequestNavigate += (s, ev) =>
            {
                Process.Start(new ProcessStartInfo(ev.Uri.AbsoluteUri) { UseShellExecute = true });
                ev.Handled = true;
            };
            // Hover effect - orange color
            authorLink.MouseEnter += (s, ev) =>
            {
                authorLink.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x47));
            };
            authorLink.MouseLeave += (s, ev) =>
            {
                authorLink.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 102, 204));
            };
            authorLabel.Inlines.Add(authorLink);

            TextBlock githubLink = new TextBlock
            {
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 0)
            };
            githubLink.Inlines.Add("Open source ");
            
            System.Windows.Documents.Hyperlink link = new System.Windows.Documents.Hyperlink
            {
                NavigateUri = new Uri("https://github.com/mhqb365/Multi-BoardViewer"),
                TextDecorations = null
            };
            link.Inlines.Add("github.com/mhqb365/Multi-BoardViewer");
            link.RequestNavigate += (s, ev) =>
            {
                Process.Start(new ProcessStartInfo(ev.Uri.AbsoluteUri) { UseShellExecute = true });
                ev.Handled = true;
            };
            // Hover effect - orange color
            link.MouseEnter += (s, ev) =>
            {
                link.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x47));
            };
            link.MouseLeave += (s, ev) =>
            {
                link.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 102, 204));
            };
            githubLink.Inlines.Add(link);

            aboutContentPanel.Children.Add(appName);
            aboutContentPanel.Children.Add(appVersion);
            aboutContentPanel.Children.Add(appDesc);
            aboutContentPanel.Children.Add(authorLabel);
            aboutContentPanel.Children.Add(githubLink);
            aboutContent.Child = aboutContentPanel;

            // About button
            Button aboutButton = new Button
            {
                Content = "ℹ️ About",
                FontSize = 12,
                Padding = new Thickness(15, 8, 15, 8),
                Cursor = Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Toggle about content
            aboutButton.Click += (s, ev) =>
            {
                if (aboutContent.Visibility == Visibility.Collapsed)
                {
                    aboutContent.Visibility = Visibility.Visible;
                    aboutButton.Content = "ℹ️ About ▼";
                }
                else
                {
                    aboutContent.Visibility = Visibility.Collapsed;
                    aboutButton.Content = "ℹ️ About";
                }
            };

            aboutSection.Children.Add(aboutButton);
            aboutSection.Children.Add(aboutContent);
            leftGrid.Children.Add(aboutSection);

            leftBorder.Child = leftGrid;
            mainGrid.Children.Add(leftBorder);

            // ========== RIGHT COLUMN - Drop Zone & Open File ==========
            Border dropZone = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)),
                AllowDrop = true,
                Padding = new Thickness(20)
            };
            Grid.SetColumn(dropZone, 1);

            StackPanel rightContent = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            TextBlock icon = new TextBlock
            {
                Text = "📂",
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            TextBlock text = new TextBlock
            {
                Text = "Drop file here",
                FontSize = 18,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            TextBlock orText = new TextBlock
            {
                Text = "or",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };

            // Create "Open file" button
            Button openFileButton = new Button
            {
                Content = "➕ Open file",
                FontSize = 14,
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Button click handler to open file dialog
            openFileButton.Click += (s, ev) =>
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "Open File",
                    Filter = "All Supported Files|*.pdf;*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|PDF Files|*.pdf|BoardViewer Files|*.fz;*.brd;*.bom;*.cad;*.bdv;*.asc;*.bv;*.cst;*.gr;*.f2b;*.faz;*.tvw|All Files|*.*",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string[] files = openFileDialog.FileNames;
                    
                    if (files.Length > 0)
                    {
                        // Open first file in this tab
                        string firstFile = files[0];
                        if (firstFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            OpenPdfInTab(newTab, firstFile);
                        }
                        else
                        {
                            OpenBoardViewerInTab(newTab, firstFile);
                        }
                        
                        // Open remaining files in new tabs
                        for (int i = 1; i < files.Length; i++)
                        {
                            string file = files[i];
                            if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                OpenPdfInNewTab(file);
                            }
                            else
                            {
                                OpenBoardViewerWithFile(file);
                            }
                        }
                    }
                }
            };

            rightContent.Children.Add(icon);
            rightContent.Children.Add(text);
            rightContent.Children.Add(orText);
            rightContent.Children.Add(openFileButton);
            dropZone.Child = rightContent;

            // Handle drop on this tab
            dropZone.DragOver += (s, ev) =>
            {
                if (ev.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    ev.Effects = DragDropEffects.Copy;
                    dropZone.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 255));
                }
                else
                {
                    ev.Effects = DragDropEffects.None;
                }
                ev.Handled = true;
            };

            dropZone.DragLeave += (s, ev) =>
            {
                dropZone.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
            };

            dropZone.Drop += (s, ev) =>
            {
                if (ev.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    _dropHandled = true; // Mark that we're handling this drop
                    
                    string[] files = (string[])ev.Data.GetData(DataFormats.FileDrop);
                    
                    if (files.Length > 0)
                    {
                        // Open first file in this tab
                        string firstFile = files[0];
                        if (firstFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            OpenPdfInTab(newTab, firstFile);
                        }
                        else
                        {
                            OpenBoardViewerInTab(newTab, firstFile);
                        }
                        
                        // Open remaining files in new tabs
                        for (int i = 1; i < files.Length; i++)
                        {
                            string file = files[i];
                            if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                OpenPdfInNewTab(file);
                            }
                            else
                            {
                                OpenBoardViewerWithFile(file);
                            }
                        }
                    }
                }
                ev.Handled = true;
            };

            mainGrid.Children.Add(dropZone);
            newTab.Content = mainGrid;

            // Insert tab before the "+" button (which should be the last tab)
            int insertIndex = tabControl.Items.Count;
            if (_addTabButton != null && tabControl.Items.Contains(_addTabButton))
            {
                insertIndex = tabControl.Items.IndexOf(_addTabButton);
            }
            tabControl.Items.Insert(insertIndex, newTab);
            tabControl.SelectedItem = newTab;

            ShowStatus("New empty tab created - drop a file to open", true);
        }

        // Check if current tab is an empty drop zone tab (no process running)
        private bool IsCurrentTabEmpty()
        {
            if (tabControl.SelectedItem is TabItem selectedTab)
            {
                // If tab has a running process, it's not empty
                if (_tabProcesses.ContainsKey(selectedTab))
                {
                    return false;
                }
                
                // Check for new tab layout (Grid with drop zone)
                return selectedTab.Content is Grid || selectedTab.Content is Border;
            }
            return false;
        }

        // Check if current tab has a running viewer process
        private bool IsCurrentTabHasViewer()
        {
            if (tabControl.SelectedItem is TabItem selectedTab)
            {
                return _tabProcesses.ContainsKey(selectedTab);
            }
            return false;
        }

        // Drag & Drop handlers
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // Block drop if current tab has a viewer running
            if (IsCurrentTabHasViewer())
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            
            // Only allow drop if current tab is empty
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && IsCurrentTabEmpty())
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            // Block drop if current tab has a viewer running
            if (IsCurrentTabHasViewer())
            {
                e.Handled = true;
                return;
            }
            
            // Check if drop was already handled by a drop zone
            if (e.Handled || _dropHandled)
            {
                _dropHandled = false; // Reset flag
                return;
            }
            
            // Only allow drop if current tab is empty
            if (!IsCurrentTabEmpty())
            {
                return;
            }
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // PDF files -> Open with SumatraPDF
                        OpenPdfInNewTab(file);
                    }
                    else
                    {
                        // Other files -> Open with BoardViewer
                        OpenBoardViewerWithFile(file);
                    }
                }
            }
        }

        // Open PDF in existing tab (replace content)
        private void OpenPdfInTab(TabItem tab, string pdfPath)
        {
            if (string.IsNullOrEmpty(_sumatraPdfPath) || !File.Exists(_sumatraPdfPath))
            {
                MessageBox.Show("SumatraPDF.exe not found!\n\nPlease place SumatraPDF.exe in the SumatraPDF folder.", 
                    "SumatraPDF Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Update tab header
                tab.Header = Path.GetFileName(pdfPath);

                // Create a WindowsFormsHost to embed SumatraPDF
                WindowsFormsHost host = new WindowsFormsHost();
                host.Focusable = true;
                
                System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    BackColor = System.Drawing.Color.White
                };
                host.Child = panel;
                tab.Content = host;

                // Force the panel to create its handle
                IntPtr panelHandle = panel.Handle;

                // Start SumatraPDF with -plugin parameter
                Process process = new Process();
                process.StartInfo.FileName = _sumatraPdfPath;
                process.StartInfo.Arguments = $"-plugin {panelHandle.ToInt64()} \"{pdfPath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_sumatraPdfPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(tab);

                process.Start();

                // Store process info
                var processInfo = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "SumatraPDF",
                    WindowHandle = IntPtr.Zero,
                    FilePath = pdfPath
                };
                _tabProcesses[tab] = processInfo;

                // Add to recent files
                AddToRecentFiles(pdfPath);

                // Setup resize handler for plugin mode
                SetupPluginResizeHandler(process, panel, processInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening PDF: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Open BoardViewer file in existing tab (replace content)
        private void OpenBoardViewerInTab(TabItem tab, string filePath)
        {
            if (string.IsNullOrEmpty(_boardViewerPath) || !File.Exists(_boardViewerPath))
            {
                MessageBox.Show("BoardViewer.exe not found!\n\nPlease place BoardViewer.exe in the same folder as this application.", 
                    "BoardViewer Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Update tab header
                tab.Header = Path.GetFileName(filePath);

                // Create a WindowsFormsHost to embed the external process
                WindowsFormsHost host = new WindowsFormsHost();
                host.Focusable = true;
                
                System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill
                };
                host.Child = panel;
                tab.Content = host;

                // Start BoardViewer process with the file
                Process process = new Process();
                process.StartInfo.FileName = _boardViewerPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_boardViewerPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(tab);

                process.Start();

                // Store process info
                _tabProcesses[tab] = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "BoardViewer",
                    WindowHandle = IntPtr.Zero,
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed the process window into our panel
                EmbedProcess(process, panel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file with BoardViewer: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBoardViewerWithFile(string filePath)
        {
            // Check if file is already open
            if (TrySwitchToExistingTab(filePath))
                return;

            if (string.IsNullOrEmpty(_boardViewerPath) || !File.Exists(_boardViewerPath))
            {
                MessageBox.Show("BoardViewer.exe not found!\n\nPlease place BoardViewer.exe in the same folder as this application.", 
                    "BoardViewer Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create new tab
                TabItem newTab = new TabItem
                {
                    Header = Path.GetFileName(filePath)
                };

                // Create a WindowsFormsHost to embed the external process
                WindowsFormsHost host = new WindowsFormsHost();
                host.Focusable = true;
                
                System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill
                };
                host.Child = panel;
                newTab.Content = host;

                // Insert tab before the "+" button
                int insertIndex = tabControl.Items.Count;
                if (_addTabButton != null && tabControl.Items.Contains(_addTabButton))
                {
                    insertIndex = tabControl.Items.IndexOf(_addTabButton);
                }
                tabControl.Items.Insert(insertIndex, newTab);
                tabControl.SelectedItem = newTab;

                // Start BoardViewer process with the file
                Process process = new Process();
                process.StartInfo.FileName = _boardViewerPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_boardViewerPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(newTab);

                process.Start();

                _tabCounter++;

                // Store process info
                _tabProcesses[newTab] = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "BoardViewer",
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed the process window into the panel
                EmbedProcess(process, panel);

                ShowStatus($"Opened: {Path.GetFileName(filePath)}", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPdfInNewTab(string pdfPath)
        {
            // Check if file is already open
            if (TrySwitchToExistingTab(pdfPath))
                return;

            if (string.IsNullOrEmpty(_sumatraPdfPath) || !File.Exists(_sumatraPdfPath))
            {
                MessageBox.Show("SumatraPDF.exe not found!\n\nPlease place SumatraPDF.exe in the same folder as this application.", 
                    "SumatraPDF Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create new tab
                TabItem newTab = new TabItem
                {
                    Header = Path.GetFileName(pdfPath)
                };

                // Create a WindowsFormsHost to embed the external process
                WindowsFormsHost host = new WindowsFormsHost();
                host.Focusable = true;
                
                System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    BackColor = System.Drawing.Color.White
                };
                host.Child = panel;
                newTab.Content = host;

                // Insert tab before the "+" button
                int insertIndex = tabControl.Items.Count;
                if (_addTabButton != null && tabControl.Items.Contains(_addTabButton))
                {
                    insertIndex = tabControl.Items.IndexOf(_addTabButton);
                }
                tabControl.Items.Insert(insertIndex, newTab);
                tabControl.SelectedItem = newTab;

                // Force the panel to create its handle
                IntPtr panelHandle = panel.Handle;

                // Start SumatraPDF with -plugin parameter
                Process process = new Process();
                process.StartInfo.FileName = _sumatraPdfPath;
                process.StartInfo.Arguments = $"-plugin {panelHandle.ToInt64()} \"{pdfPath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_sumatraPdfPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(newTab);

                process.Start();

                _sumatraTabCounter++;

                // Store process info
                var processInfo = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "SumatraPDF",
                    WindowHandle = IntPtr.Zero,
                    FilePath = pdfPath
                };
                _tabProcesses[newTab] = processInfo;

                // Add to recent files
                AddToRecentFiles(pdfPath);

                // Setup resize handler for plugin mode
                SetupPluginResizeHandler(process, panel, processInfo);

                ShowStatus($"Opened: {Path.GetFileName(pdfPath)}", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening PDF: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoDetectSumatraPdfPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check in current directory
            string path1 = Path.Combine(appDir, "SumatraPDF.exe");
            if (File.Exists(path1))
            {
                _sumatraPdfPath = path1;
                return;
            }

            // Check in SumatraPDF subfolder
            string path2 = Path.Combine(appDir, "SumatraPDF", "SumatraPDF.exe");
            if (File.Exists(path2))
            {
                _sumatraPdfPath = path2;
                return;
            }

            // Check in parent directory (for development)
            string parentDir = Directory.GetParent(appDir)?.FullName;
            if (parentDir != null)
            {
                string path3 = Path.Combine(parentDir, "SumatraPDF", "SumatraPDF.exe");
                if (File.Exists(path3))
                {
                    _sumatraPdfPath = path3;
                    return;
                }
                
                string path4 = Path.Combine(parentDir, "SumatraPDF.exe");
                if (File.Exists(path4))
                {
                    _sumatraPdfPath = path4;
                    return;
                }
            }
            
            // SumatraPDF not found - drag & drop will show warning
            _sumatraPdfPath = "";
        }

        private void AutoDetectBoardViewerPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check in current directory
            string path1 = Path.Combine(appDir, "BoardViewer.exe");
            if (File.Exists(path1))
            {
                _boardViewerPath = path1;
                return;
            }

            // Check in BoardViewer subfolder
            string path2 = Path.Combine(appDir, "BoardViewer", "BoardViewer.exe");
            if (File.Exists(path2))
            {
                _boardViewerPath = path2;
                return;
            }

            // Check in parent directory (for development)
            string parentDir = Directory.GetParent(appDir)?.FullName;
            if (parentDir != null)
            {
                string path3 = Path.Combine(parentDir, "BoardViewer", "BoardViewer.exe");
                if (File.Exists(path3))
                {
                    _boardViewerPath = path3;
                    return;
                }
                
                string path4 = Path.Combine(parentDir, "BoardViewer.exe");
                if (File.Exists(path4))
                {
                    _boardViewerPath = path4;
                    return;
                }
            }

            // BoardViewer not found - drag & drop will show warning
            _boardViewerPath = "";
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            // Create destination directory
            Directory.CreateDirectory(destDir);

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFilePath = Path.Combine(destDir, fileName);
                File.Copy(filePath, destFilePath, true);
            }

            // Copy all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dirPath);
                string destDirPath = Path.Combine(destDir, dirName);
                CopyDirectory(dirPath, destDirPath);
            }
        }

        private async void SetupPluginResizeHandler(Process process, System.Windows.Forms.Panel panel, ProcessInfo processInfo)
        {
            // Wait for SumatraPDF to create its window inside the panel
            await System.Threading.Tasks.Task.Delay(1000);
            
            if (process.HasExited)
                return;

            // In plugin mode, SumatraPDF creates a child window inside the panel
            // We need to find that child window
            IntPtr windowHandle = IntPtr.Zero;
            IntPtr panelHandle = panel.Handle;
            
            for (int i = 0; i < 50; i++)
            {
                if (process.HasExited) return;
                
                // Find child window of the panel
                windowHandle = GetWindow(panelHandle, GW_CHILD);
                    
                if (windowHandle != IntPtr.Zero)
                    break;
                    
                await System.Threading.Tasks.Task.Delay(100);
            }

            if (windowHandle == IntPtr.Zero)
            {
                Debug.WriteLine("Could not find SumatraPDF child window");
                return;
            }

            Debug.WriteLine($"Found SumatraPDF child window: {windowHandle}");

            // Store window handle in ProcessInfo for later use
            processInfo.WindowHandle = windowHandle;

            // Helper to make LPARAM for WM_SIZE
            Func<int, int, IntPtr> MakeLParam = (low, high) => 
                new IntPtr((high << 16) | (low & 0xFFFF));

            // Action to resize the SumatraPDF window
            Action resizeSumatra = () =>
            {
                if (!process.HasExited)
                {
                    IntPtr childHandle = GetWindow(panel.Handle, GW_CHILD);
                    if (childHandle != IntPtr.Zero)
                    {
                        int width = panel.Width;
                        int height = panel.Height;
                        
                        // Move the window
                        MoveWindow(childHandle, 0, 0, width, height, true);
                        
                        // Send WM_SIZE message to notify SumatraPDF of the size change
                        SendMessage(childHandle, WM_SIZE, new IntPtr(SIZE_RESTORED), MakeLParam(width, height));
                        
                        processInfo.WindowHandle = childHandle;
                    }
                }
            };

            // Initial resize
            resizeSumatra();

            // Handle WinForms panel resize
            panel.Resize += (s, e) => resizeSumatra();

            // Handle WPF host resize (this is triggered when WPF layout changes)
            processInfo.Host.SizeChanged += (s, e) =>
            {
                resizeSumatra();
            };

            // Handle focus
            panel.MouseEnter += (s, e) =>
            {
                if (!process.HasExited && processInfo.WindowHandle != IntPtr.Zero)
                {
                    SetFocus(processInfo.WindowHandle);
                }
            };

            panel.Click += (s, e) =>
            {
                if (!process.HasExited && processInfo.WindowHandle != IntPtr.Zero)
                {
                    SetFocus(processInfo.WindowHandle);
                }
            };
        }
        private async void EmbedSumatraAsync(Process process, System.Windows.Forms.Panel panel)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;
                
                // Wait a bit for window to be created
                await System.Threading.Tasks.Task.Delay(500);
                
                // Try to get the window handle
                for (int i = 0; i < 50; i++)
                {
                    if (process.HasExited)
                        return;
                    
                    process.Refresh();
                    processHandle = process.MainWindowHandle;
                    
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }
                    
                    if (processHandle != IntPtr.Zero)
                        break;
                        
                    await System.Threading.Tasks.Task.Delay(100);
                }

                if (processHandle == IntPtr.Zero)
                {
                    Dispatcher.Invoke(() => ShowStatus("Could not get SumatraPDF window", false));
                    return;
                }

                // Embed the window into panel
                DoEmbedWindow(processHandle, panel, process);
                
                // Re-apply embedding after a short delay (some apps reset their style)
                await System.Threading.Tasks.Task.Delay(200);
                if (!process.HasExited)
                {
                    DoEmbedWindow(processHandle, panel, process);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding SumatraPDF: {ex.Message}");
            }
        }

        private void DoEmbedWindow(IntPtr windowHandle, System.Windows.Forms.Panel panel, Process process)
        {
            if (windowHandle == IntPtr.Zero || process.HasExited)
                return;

            // Set as child of panel FIRST
            SetParent(windowHandle, panel.Handle);

            // Remove all window chrome
            int style = GetWindowLong(windowHandle, GWL_STYLE);
            style = style & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX & ~WS_SYSMENU & ~WS_BORDER;
            style = style | WS_CHILD | WS_VISIBLE;
            SetWindowLong(windowHandle, GWL_STYLE, style);

            // Remove extended styles
            int exStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
            exStyle = exStyle & ~WS_EX_DLGMODALFRAME & ~WS_EX_WINDOWEDGE & ~WS_EX_CLIENTEDGE & ~WS_EX_STATICEDGE;
            SetWindowLong(windowHandle, GWL_EXSTYLE, exStyle);

            // Move and resize to fill the panel completely
            MoveWindow(windowHandle, 0, 0, panel.Width, panel.Height, true);
            
            // Force redraw
            ShowWindow(windowHandle, SW_SHOW);

            // Setup resize handler (remove old handlers first to avoid duplicates)
            panel.Resize -= Panel_Resize;
            panel.Resize += Panel_Resize;
            
            void Panel_Resize(object sender, EventArgs e)
            {
                if (!process.HasExited && windowHandle != IntPtr.Zero)
                {
                    MoveWindow(windowHandle, 0, 0, panel.Width, panel.Height, true);
                }
            }

            // Setup focus handlers
            panel.Click -= Panel_Click;
            panel.Click += Panel_Click;
            
            void Panel_Click(object sender, EventArgs e)
            {
                if (!process.HasExited && windowHandle != IntPtr.Zero)
                {
                    SetFocus(windowHandle);
                }
            }

            panel.MouseEnter -= Panel_MouseEnter;
            panel.MouseEnter += Panel_MouseEnter;
            
            void Panel_MouseEnter(object sender, EventArgs e)
            {
                if (!process.HasExited && windowHandle != IntPtr.Zero)
                {
                    SetFocus(windowHandle);
                }
            }
        }

        private async void EmbedProcessAsync(Process process, System.Windows.Forms.Panel panel, TabItem tab)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;
                
                // Wait for the main window handle to be available (up to 10 seconds)
                for (int i = 0; i < 100 && processHandle == IntPtr.Zero; i++)
                {
                    await System.Threading.Tasks.Task.Delay(100);
                    
                    if (process.HasExited)
                        return;
                    
                    process.Refresh();
                    processHandle = process.MainWindowHandle;
                    
                    // If MainWindowHandle is still zero, try to find window by process ID
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }
                }

                if (processHandle == IntPtr.Zero)
                {
                    Dispatcher.Invoke(() => ShowStatus("Could not get SumatraPDF window handle", false));
                    return;
                }

                // Remove ALL window decorations first (before SetParent)
                int style = GetWindowLong(processHandle, GWL_STYLE);
                style &= ~(WS_CAPTION | WS_BORDER | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
                style |= WS_CHILD | WS_VISIBLE;
                SetWindowLong(processHandle, GWL_STYLE, style);

                // Remove extended style borders
                int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
                SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                // NOW set parent after removing decorations
                SetParent(processHandle, panel.Handle);

                // Disable drag & drop on the embedded window
                DisableDragDrop(processHandle);

                // Resize and reposition the window to fit the panel
                MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);

                // Show the window
                ShowWindow(processHandle, SW_SHOW);

                // Set focus
                SetFocus(processHandle);

                // Handle panel resize
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                };

                // Handle panel click to set focus
                panel.Click += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        SetFocus(processHandle);
                        SetForegroundWindow(processHandle);
                    }
                };

                // Handle mouse enter to give focus
                panel.MouseEnter += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        SetFocus(processHandle);
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding SumatraPDF: {ex.Message}");
            }
        }

        private IntPtr FindWindowByProcessId(int processId)
        {
            IntPtr foundHandle = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                
                if (windowProcessId == processId && IsWindowVisible(hWnd))
                {
                    foundHandle = hWnd;
                    return false; // Stop enumeration
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return foundHandle;
        }

        // Disable OLE Drag & Drop on a window and all its children
        private void DisableDragDrop(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return;

            try
            {
                // Revoke drag drop on the main window
                RevokeDragDrop(hWnd);

                // Revoke drag drop on all child windows
                EnumChildWindows(hWnd, (childHwnd, lParam) =>
                {
                    try
                    {
                        RevokeDragDrop(childHwnd);
                    }
                    catch { }
                    return true; // Continue enumeration
                }, IntPtr.Zero);
            }
            catch { }
        }

        private async void EmbedProcess(Process process, System.Windows.Forms.Panel panel)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;

                // Wait for the main window handle to be available (up to 5 seconds)
                for (int i = 0; i < 50 && processHandle == IntPtr.Zero; i++)
                {
                    await System.Threading.Tasks.Task.Delay(100);
                    
                    if (process.HasExited)
                        return;
                    
                    process.Refresh();
                    processHandle = process.MainWindowHandle;
                    
                    // If MainWindowHandle is still zero, try to find window by process ID
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }
                }

                if (processHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("Could not get BoardViewer window handle");
                    return;
                }

                // FIRST: Move window off-screen to prevent flashing
                MoveWindow(processHandle, -10000, -10000, 1, 1, false);

                // Remove ALL window decorations
                int style = GetWindowLong(processHandle, GWL_STYLE);
                style &= ~(WS_CAPTION | WS_BORDER | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
                style |= WS_CHILD;
                SetWindowLong(processHandle, GWL_STYLE, style);

                // Remove extended style borders
                int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
                SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                // Set parent after removing decorations
                SetParent(processHandle, panel.Handle);

                // Disable drag & drop on the embedded window and all its children
                DisableDragDrop(processHandle);

                // Resize the window to fit the panel
                MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);

                // Show the window now that it's embedded
                ShowWindow(processHandle, SW_SHOW);

                // Set focus to the embedded window
                SetFocus(processHandle);

                // Handle panel resize
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                };

                // Handle panel click to set focus to embedded window
                panel.Click += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        SetFocus(processHandle);
                        SetForegroundWindow(processHandle);
                    }
                };

                // Handle mouse enter to give focus
                panel.MouseEnter += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        SetFocus(processHandle);
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding BoardViewer: {ex.Message}");
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent event bubbling
            
            Button button = sender as Button;
            TabItem tabItem = button?.Tag as TabItem;

            if (tabItem != null && tabItem != _addTabButton)
            {
                _isCreatingTab = true; // Prevent creating new tab during close
                
                // Find index of closing tab
                int closingIndex = tabControl.Items.IndexOf(tabItem);
                int addButtonIndex = tabControl.Items.IndexOf(_addTabButton);
                
                // Determine which tab to select after closing
                TabItem nextTab = null;
                
                // Count real tabs (excluding "+" button)
                int realTabCount = tabControl.Items.Count - 1; // minus the "+" button
                
                if (realTabCount > 1)
                {
                    // There are other tabs to switch to
                    if (closingIndex < addButtonIndex - 1)
                    {
                        // Select the next tab (to the right)
                        nextTab = tabControl.Items[closingIndex + 1] as TabItem;
                    }
                    else if (closingIndex > 0)
                    {
                        // Select the previous tab (to the left)
                        nextTab = tabControl.Items[closingIndex - 1] as TabItem;
                    }
                }
                
                // Close the tab
                CloseTabItem(tabItem);
                
                // Select next tab or create new empty tab if no tabs left
                if (nextTab != null && nextTab != _addTabButton)
                {
                    tabControl.SelectedItem = nextTab;
                }
                else if (tabControl.Items.Count == 1 && tabControl.Items[0] == _addTabButton)
                {
                    // Only "+" tab remains, create a new empty tab
                    CreateEmptyTab();
                }
                
                _isCreatingTab = false;
            }
        }

        private void CloseTabItem(TabItem tabItem)
        {
            if (_tabProcesses.ContainsKey(tabItem))
            {
                ProcessInfo processInfo = _tabProcesses[tabItem];

                // Close process in background to avoid UI freeze
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        if (processInfo.Process != null && !processInfo.Process.HasExited)
                        {
                            processInfo.Process.Kill();
                            processInfo.Process.Dispose();
                        }

                        // Clean up temp directory
                        if (!string.IsNullOrEmpty(processInfo.TempDirectory) && Directory.Exists(processInfo.TempDirectory))
                        {
                            try
                            {
                                System.Threading.Thread.Sleep(500);
                                Directory.Delete(processInfo.TempDirectory, true);
                            }
                            catch { }
                        }
                    }
                    catch { }
                });

                _tabProcesses.Remove(tabItem);
            }

            tabControl.Items.Remove(tabItem);
        }

        private void Process_Exited(TabItem tabItem)
        {
            Dispatcher.Invoke(() =>
            {
                if (_tabProcesses.ContainsKey(tabItem))
                {
                    ProcessInfo processInfo = _tabProcesses[tabItem];
                    
                    // Clean up temp directory
                    if (!string.IsNullOrEmpty(processInfo.TempDirectory) && Directory.Exists(processInfo.TempDirectory))
                    {
                        try
                        {
                            Directory.Delete(processInfo.TempDirectory, true);
                        }
                        catch { }
                    }
                    
                    _tabProcesses.Remove(tabItem);
                    tabControl.Items.Remove(tabItem);
                    ShowStatus("BoardViewer process exited", false);
                }
            });
        }

        private TabItem _previousSelectedTab;
        private bool _isCreatingTab = false;
        
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if "+" tab is selected (handled by click event)
            if (tabControl.SelectedItem == _addTabButton)
            {
                // Select previous tab if available
                if (_previousSelectedTab != null && tabControl.Items.Contains(_previousSelectedTab))
                {
                    tabControl.SelectedItem = _previousSelectedTab;
                }
                return;
            }
            
            _previousSelectedTab = tabControl.SelectedItem as TabItem;
            
            // Trigger resize when tab is switched
            _resizeTimer.Stop();
            _resizeTimer.Start();

            // Set focus to the selected tab's embedded window
            if (tabControl.SelectedItem is TabItem selectedTab && _tabProcesses.ContainsKey(selectedTab))
            {
                ProcessInfo processInfo = _tabProcesses[selectedTab];
                if (processInfo.Process != null && !processInfo.Process.HasExited)
                {
                    IntPtr handle;
                    
                    // For SumatraPDF in plugin mode, find child window of panel
                    if (processInfo.AppType == "SumatraPDF")
                    {
                        handle = GetWindow(processInfo.Panel.Handle, GW_CHILD);
                        if (handle != IntPtr.Zero)
                        {
                            processInfo.WindowHandle = handle;
                        }
                        else
                        {
                            handle = processInfo.WindowHandle;
                        }
                    }
                    else
                    {
                        handle = processInfo.WindowHandle != IntPtr.Zero 
                            ? processInfo.WindowHandle 
                            : processInfo.Process.MainWindowHandle;
                    }
                        
                    if (handle != IntPtr.Zero)
                    {
                        // Set focus to the embedded window
                        SetFocus(handle);
                        SetForegroundWindow(handle);
                    }
                }
            }
        }

        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            _resizeTimer.Stop();

            if (tabControl.SelectedItem is TabItem selectedTab && _tabProcesses.ContainsKey(selectedTab))
            {
                ProcessInfo processInfo = _tabProcesses[selectedTab];
                if (processInfo.Process != null && !processInfo.Process.HasExited)
                {
                    IntPtr handle = IntPtr.Zero;
                    
                    // For SumatraPDF in plugin mode, find child window of panel
                    if (processInfo.AppType == "SumatraPDF")
                    {
                        handle = GetWindow(processInfo.Panel.Handle, GW_CHILD);
                        if (handle != IntPtr.Zero)
                        {
                            processInfo.WindowHandle = handle;
                        }
                    }
                    else
                    {
                        handle = processInfo.WindowHandle != IntPtr.Zero 
                            ? processInfo.WindowHandle 
                            : processInfo.Process.MainWindowHandle;
                    }
                        
                    if (handle != IntPtr.Zero)
                    {
                        // Force update panel size first
                        processInfo.Host.UpdateLayout();
                        
                        int width = processInfo.Panel.Width;
                        int height = processInfo.Panel.Height;
                        
                        Debug.WriteLine($"ResizeTimer: Resizing {processInfo.AppType} to {width}x{height}");
                        
                        MoveWindow(handle, 0, 0, width, height, true);
                    }
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Stop resize timer
            try { _resizeTimer?.Stop(); } catch { }
            
            // Close all processes when window is closing - use parallel for speed
            var processesToKill = _tabProcesses.Values.ToList();
            
            System.Threading.Tasks.Parallel.ForEach(processesToKill, processInfo =>
            {
                try
                {
                    if (processInfo.Process != null && !processInfo.Process.HasExited)
                    {
                        processInfo.Process.Kill();
                    }
                }
                catch { }
                
                try
                {
                    processInfo.Process?.Dispose();
                }
                catch { }

                // Clean up temp directory
                if (!string.IsNullOrEmpty(processInfo.TempDirectory))
                {
                    try
                    {
                        if (Directory.Exists(processInfo.TempDirectory))
                        {
                            Directory.Delete(processInfo.TempDirectory, true);
                        }
                    }
                    catch { }
                }
            });

            _tabProcesses.Clear();
            
            // Clean up any remaining temp directories
            CleanupOldTempDirectories();
        }

        private void CleanupOldTempDirectories()
        {
            try
            {
                string tempRoot = Path.Combine(Path.GetTempPath(), "MultiBoardViewer");
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch { }
        }

        private void ShowStatus(string message, bool isSuccess)
        {
            // Status bar removed - do nothing
            Debug.WriteLine($"Status: {message}");
        }
    }
}
