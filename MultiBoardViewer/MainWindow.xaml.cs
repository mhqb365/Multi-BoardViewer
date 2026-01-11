using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MultiBoardViewer.Controls;
using MultiBoardViewer.Services;

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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

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
        private const int GWL_HWNDPARENT = -8;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_EX_DLGMODALFRAME = 0x00000001;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_WINDOWEDGE = 0x00000100;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_STATICEDGE = 0x00020000;
        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;
        private const int SW_MAXIMIZE = 3;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint BM_CLICK = 0x00F5;

        // Keyboard message constants
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CHAR = 0x0102;
        private const uint WM_SYSKEYDOWN = 0x0104;
        private const uint WM_SYSKEYUP = 0x0105;
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_ACTIVATEAPP = 0x001C;
        private const int WA_ACTIVE = 1;

        // Dictionary to track processes and their containers
        private Dictionary<TabItem, ProcessInfo> _tabProcesses = new Dictionary<TabItem, ProcessInfo>();
        private int _tabCounter = 1;
        private int _sumatraTabCounter = 1;
        private string _boardViewerPath = "";
        private string _openBoardViewPath = "";
        private string _flexBoardViewPath = "";
        private string _sumatraPdfPath = "";
        private DispatcherTimer _resizeTimer;
        private bool _dropHandled = false; // Flag to prevent double drop handling
        private RecentFilesService _recentFilesService;
        private Process _voltageDividerProcess = null; // Track calculator process

        // Drag and drop for tab reordering
        private bool _isDragging = false;
        private TabItem _draggedTab = null;
        private Point _dragStartPoint;
        private TabItem _lastTargetTab = null; // Track last target to avoid frequent reordering

        private class ProcessInfo
        {
            public Process Process { get; set; }
            public WindowsFormsHost Host { get; set; }
            public System.Windows.Forms.Panel Panel { get; set; }
            public string TempDirectory { get; set; }
            public string AppType { get; set; } // "BoardViewer" or "SumatraPDF"
            public IntPtr WindowHandle { get; set; } // Store window handle for resize
            public string FilePath { get; set; } // Store the file path being viewed
            public bool IsOverlayWindow { get; set; }
            public bool OverlayVisible { get; set; }
            public bool OverlayRectInitialized { get; set; }
            public int OverlayX { get; set; }
            public int OverlayY { get; set; }
            public int OverlayWidth { get; set; }
            public int OverlayHeight { get; set; }
        }

        // Check if file is already open and switch to that tab
        private bool TrySwitchToExistingTab(string filePath, string viewerType = null)
        {
            foreach (var kvp in _tabProcesses)
            {
                if (kvp.Value.FilePath != null &&
                    kvp.Value.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    // if viewerType is specified, check if it matches
                    if (viewerType != null && !string.Equals(kvp.Value.AppType, viewerType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // File is already open, switch to that tab
                    tabControl.SelectedItem = kvp.Key;
                    ShowStatus($"Switched to existing tab: {Path.GetFileName(filePath)}", true);
                    return true;
                }
            }
            return false;
        }

        // Check if a file with the same name is already open and switch to that tab
        private bool TrySwitchToExistingTabByFileName(string filePath, string viewerType = null)
        {
            string fileName = Path.GetFileName(filePath);
            foreach (var kvp in _tabProcesses)
            {
                if (kvp.Value.FilePath != null)
                {
                    string existingFileName = Path.GetFileName(kvp.Value.FilePath);
                    if (existingFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        // if viewerType is specified, check if it matches
                        if (viewerType != null && !string.Equals(kvp.Value.AppType, viewerType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // File with same name is already open, switch to that tab
                        tabControl.SelectedItem = kvp.Key;
                        ShowStatus($"Switched to existing tab: {fileName}", true);
                        return true;
                    }
                }
            }
            return false;
        }



        // Add file to recent history
        private void AddToRecentFiles(string filePath)
        {
            _recentFilesService?.AddToRecentFiles(filePath);
        }

        public MainWindow()
        {
            InitializeComponent();

            // Add drag and drop event handlers for tab reordering
            tabControl.PreviewMouseLeftButtonDown += TabControl_PreviewMouseLeftButtonDown;
            tabControl.PreviewMouseMove += TabControl_PreviewMouseMove;
            tabControl.PreviewMouseLeftButtonUp += TabControl_PreviewMouseLeftButtonUp;

            // Hook keyboard events to forward to embedded windows
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.PreviewKeyUp += MainWindow_PreviewKeyUp;
            this.PreviewTextInput += MainWindow_PreviewTextInput;

            // Initialize services
            _recentFilesService = new RecentFilesService();

            // Initialize timer for handling window resizing
            _resizeTimer = new DispatcherTimer();
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(100);
            _resizeTimer.Tick += ResizeTimer_Tick;

            // Handle window resize to update embedded processes
            this.SizeChanged += MainWindow_SizeChanged;

            // Handle window activation to restore focus to embedded apps
            this.Activated += MainWindow_Activated;
            this.LocationChanged += MainWindow_LocationChanged;
            this.StateChanged += MainWindow_StateChanged;

            // Also handle layout updates for more responsive resizing
            this.LayoutUpdated += MainWindow_LayoutUpdated;

            // Try to find BoardViewer.exe in the same directory or parent directory
            AutoDetectBoardViewerPath();

            // Try to find OpenBoardView.exe
            AutoDetectOpenBoardViewPath();

            // Try to find FlexBoardView.exe
            AutoDetectFlexBoardViewPath();

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
            UpdateOverlayWindowsForSelection();

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
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltageDividerCalculator", "VoltageDividerCalculator.exe");
                if (File.Exists(exePath))
                {
                    _voltageDividerProcess = Process.Start(exePath);
                }
            }
            catch { }
        }

        private void CreateEmptyTab()
        {
            // First check if an empty tab already exists
            foreach (var item in tabControl.Items)
            {
                if (item is TabItem tab && tab != _addTabButton && !_tabProcesses.ContainsKey(tab) && tab.Content is StartPage)
                {
                    tabControl.SelectedItem = tab;
                    return;
                }
            }

            // Create new empty tab
            TabItem newTab = new TabItem
            {
                Header = "New tab"
            };

            var startPage = new MultiBoardViewer.Controls.StartPage();
            startPage.FilesOpenRequested += (s, files) =>
            {
                if (files == null || files.Length == 0) return;

                // Open first file in this tab
                string firstFile = files[0];
                if (firstFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    OpenPdfInTab(newTab, firstFile);
                }
                else
                {
                    OpenBoardFileInTab(newTab, firstFile);
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
                        OpenBoardFileWithFile(file);
                    }
                }
            };

            newTab.Content = startPage;

            // Insert tab before the "+" button (which should be the last tab)
            int insertIndex = tabControl.Items.Count;
            if (_addTabButton != null && tabControl.Items.Contains(_addTabButton))
            {
                insertIndex = tabControl.Items.IndexOf(_addTabButton);
            }
            tabControl.Items.Insert(insertIndex, newTab);
            tabControl.SelectedItem = newTab;

            ShowStatus("New empty tab created", true);
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

                // Check for new tab layout (StartPage)
                return selectedTab.Content is StartPage || selectedTab.Content is UserControl;
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
            // Check if file is already open
            if (TrySwitchToExistingTab(pdfPath, "SumatraPDF"))
                return;

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

                // Add context menu for switching viewers
                ContextMenu contextMenu = new ContextMenu();
                MenuItem boardViewerItem = new MenuItem { Header = "Open with BoardViewer" };
                boardViewerItem.Click += (s, e) => { OpenBoardViewerInTab(tab, filePath); };
                MenuItem openBoardViewItem = new MenuItem { Header = "Open with OpenBoardView" };
                openBoardViewItem.Click += (s, e) => { OpenOpenBoardViewInTab(tab, filePath); };
                MenuItem flexBoardViewItem = new MenuItem { Header = "Open with FlexBoardView" };
                flexBoardViewItem.Click += (s, e) => { OpenFlexBoardViewInTab(tab, filePath); };
                contextMenu.Items.Add(boardViewerItem);
                contextMenu.Items.Add(openBoardViewItem);
                contextMenu.Items.Add(flexBoardViewItem);
                tab.ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file with BoardViewer: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Open OpenBoardView file in existing tab (replace content)
        private void OpenOpenBoardViewInTab(TabItem tab, string filePath)
        {
            if (string.IsNullOrEmpty(_openBoardViewPath) || !File.Exists(_openBoardViewPath))
            {
                MessageBox.Show("OpenBoardView.exe not found!\n\nPlease place OpenBoardView.exe in the same folder as this application.",
                    "OpenBoardView Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Start OpenBoardView process with the file
                Process process = new Process();
                process.StartInfo.FileName = _openBoardViewPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_openBoardViewPath);
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
                    AppType = "OpenBoardView",
                    WindowHandle = IntPtr.Zero,
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed the process window into our panel
                _ = EmbedProcessAsOverlay(process, panel, _tabProcesses[tab]);

                // Add context menu for switching viewers
                ContextMenu contextMenu = new ContextMenu();
                MenuItem boardViewerItem = new MenuItem { Header = "Open with BoardViewer" };
                boardViewerItem.Click += (s, e) => { OpenBoardViewerInTab(tab, filePath); };
                MenuItem openBoardViewItem = new MenuItem { Header = "Open with OpenBoardView" };
                openBoardViewItem.Click += (s, e) => { OpenOpenBoardViewInTab(tab, filePath); };
                MenuItem flexBoardViewItem = new MenuItem { Header = "Open with FlexBoardView" };
                flexBoardViewItem.Click += (s, e) => { OpenFlexBoardViewInTab(tab, filePath); };
                contextMenu.Items.Add(boardViewerItem);
                contextMenu.Items.Add(openBoardViewItem);
                contextMenu.Items.Add(flexBoardViewItem);
                tab.ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file with OpenBoardView: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Open FlexBoardView file in existing tab (replace content)
        private void OpenFlexBoardViewInTab(TabItem tab, string filePath)
        {
            if (string.IsNullOrEmpty(_flexBoardViewPath) || !File.Exists(_flexBoardViewPath))
            {
                MessageBox.Show("FlexBoardView.exe not found!\n\nPlease place FlexBoardView.exe in the same folder as this application.",
                    "FlexBoardView Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Clean FlexBoardView logs folder before starting to prevent crash dialogs
                CleanFlexBoardViewLogs();

                // Start FlexBoardView process with the file
                Process process = new Process();
                process.StartInfo.FileName = _flexBoardViewPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_flexBoardViewPath);
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
                    AppType = "FlexBoardView",
                    WindowHandle = IntPtr.Zero,
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed as overlay to keep keyboard input working
                _ = EmbedProcessAsOverlay(process, panel, _tabProcesses[tab]);

                // Add context menu for switching viewers
                ContextMenu contextMenu = new ContextMenu();
                MenuItem boardViewerItem = new MenuItem { Header = "Open with BoardViewer" };
                boardViewerItem.Click += (s, e) => { OpenBoardViewerInTab(tab, filePath); };
                MenuItem openBoardViewItem = new MenuItem { Header = "Open with OpenBoardView" };
                openBoardViewItem.Click += (s, e) => { OpenOpenBoardViewInTab(tab, filePath); };
                MenuItem flexBoardViewItem = new MenuItem { Header = "Open with FlexBoardView" };
                flexBoardViewItem.Click += (s, e) => { OpenFlexBoardViewInTab(tab, filePath); };
                contextMenu.Items.Add(boardViewerItem);
                contextMenu.Items.Add(openBoardViewItem);
                contextMenu.Items.Add(flexBoardViewItem);
                tab.ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file with FlexBoardView: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBoardFileInTab(TabItem tab, string filePath)
        {
            var dialog = new ViewerSelectionDialog(Path.GetFileName(filePath));
            dialog.Owner = this;
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                string viewerType = null;
                if (dialog.Result == ViewerSelectionDialog.ViewerResult.BoardViewer) viewerType = "BoardViewer";
                else if (dialog.Result == ViewerSelectionDialog.ViewerResult.OpenBoardView) viewerType = "OpenBoardView";
                else if (dialog.Result == ViewerSelectionDialog.ViewerResult.FlexBoardView) viewerType = "FlexBoardView";

                if (viewerType != null && TrySwitchToExistingTab(filePath, viewerType))
                    return;
                if (dialog.Result == ViewerSelectionDialog.ViewerResult.BoardViewer)
                {
                    OpenBoardViewerInTab(tab, filePath);
                }
                else if (dialog.Result == ViewerSelectionDialog.ViewerResult.OpenBoardView)
                {
                    OpenOpenBoardViewInTab(tab, filePath);
                }
                else if (dialog.Result == ViewerSelectionDialog.ViewerResult.FlexBoardView)
                {
                    OpenFlexBoardViewInTab(tab, filePath);
                }
            }
            // Cancel does nothing
        }

        private void OpenBoardViewerWithFile(string filePath)
        {
            // Check if file is already open
            if (TrySwitchToExistingTab(filePath, "BoardViewer"))
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

        private void OpenBoardFileWithFile(string filePath)
        {
            // Check if file is already open
            // Do NOT check here globally as we want to allow opening same file with different viewer
            // Logic is handled in OpenBoardFileInTab after user selects viewer
            // if (TrySwitchToExistingTab(filePath))
            //    return;

            // Create new tab
            TabItem newTab = new TabItem
            {
                Header = Path.GetFileName(filePath)
            };

            // Insert tab before the "+" button
            int insertIndex = tabControl.Items.Count;
            if (_addTabButton != null && tabControl.Items.Contains(_addTabButton))
            {
                insertIndex = tabControl.Items.IndexOf(_addTabButton);
            }
            tabControl.Items.Insert(insertIndex, newTab);
            tabControl.SelectedItem = newTab;

            // Open with choice
            OpenBoardFileInTab(newTab, filePath);
        }

        private void OpenOpenBoardViewWithFile(string filePath)
        {
            // Check if file is already open
            if (TrySwitchToExistingTab(filePath, "OpenBoardView"))
                return;

            if (string.IsNullOrEmpty(_openBoardViewPath) || !File.Exists(_openBoardViewPath))
            {
                MessageBox.Show("OpenBoardView.exe not found!\n\nPlease place OpenBoardView.exe in the same folder as this application.",
                    "OpenBoardView Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Start OpenBoardView process with the file
                Process process = new Process();
                process.StartInfo.FileName = _openBoardViewPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_openBoardViewPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(newTab);

                process.Start();

                // Store process info
                _tabProcesses[newTab] = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "OpenBoardView",
                    WindowHandle = IntPtr.Zero,
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed the process window into the panel
                _ = EmbedProcessAsOverlay(process, panel, _tabProcesses[newTab]);

                ShowStatus($"Opened: {Path.GetFileName(filePath)}", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFlexBoardViewWithFile(string filePath)
        {
            // Check if file is already open
            if (TrySwitchToExistingTab(filePath, "FlexBoardView"))
                return;

            if (string.IsNullOrEmpty(_flexBoardViewPath) || !File.Exists(_flexBoardViewPath))
            {
                MessageBox.Show("FlexBoardView.exe not found!\n\nPlease place FlexBoardView.exe in the same folder as this application.",
                    "FlexBoardView Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Clean FlexBoardView logs folder before starting to prevent crash dialogs
                CleanFlexBoardViewLogs();

                // Start FlexBoardView process with the file
                Process process = new Process();
                process.StartInfo.FileName = _flexBoardViewPath;
                process.StartInfo.Arguments = $"\"{filePath}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(_flexBoardViewPath);
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) => Process_Exited(newTab);

                process.Start();

                // Store process info
                _tabProcesses[newTab] = new ProcessInfo
                {
                    Process = process,
                    Host = host,
                    Panel = panel,
                    TempDirectory = null,
                    AppType = "FlexBoardView",
                    WindowHandle = IntPtr.Zero,
                    FilePath = filePath
                };

                // Add to recent files
                AddToRecentFiles(filePath);

                // Embed as overlay to keep keyboard input working
                _ = EmbedProcessAsOverlay(process, panel, _tabProcesses[newTab]);

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
            if (TrySwitchToExistingTab(pdfPath, "SumatraPDF"))
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

        private void AutoDetectOpenBoardViewPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Check in current directory
            string path1 = Path.Combine(appDir, "OpenBoardView.exe");
            if (File.Exists(path1))
            {
                _openBoardViewPath = path1;
                return;
            }

            // Check in OpenBoardView subfolder
            string path2 = Path.Combine(appDir, "OpenBoardView", "OpenBoardView.exe");
            if (File.Exists(path2))
            {
                _openBoardViewPath = path2;
                return;
            }

            // Check in parent directory (for development)
            string parentDir = Directory.GetParent(appDir)?.FullName;
            if (parentDir != null)
            {
                string path3 = Path.Combine(parentDir, "OpenBoardView", "OpenBoardView.exe");
                if (File.Exists(path3))
                {
                    _openBoardViewPath = path3;
                    return;
                }

                string path4 = Path.Combine(parentDir, "OpenBoardView.exe");
                if (File.Exists(path4))
                {
                    _openBoardViewPath = path4;
                    return;
                }
            }

            // OpenBoardView not found - will show warning
            _openBoardViewPath = "";
        }

        private void AutoDetectFlexBoardViewPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Check in current directory
            string path1 = Path.Combine(appDir, "FlexBoardView.exe");
            if (File.Exists(path1))
            {
                _flexBoardViewPath = path1;
                return;
            }

            // Check in FlexBoardView subfolder
            string path2 = Path.Combine(appDir, "FlexBoardView", "FlexBoardView.exe");
            if (File.Exists(path2))
            {
                _flexBoardViewPath = path2;
                return;
            }

            // Check in parent directory (for development)
            string parentDir = Directory.GetParent(appDir)?.FullName;
            if (parentDir != null)
            {
                string path3 = Path.Combine(parentDir, "FlexBoardView", "FlexBoardView.exe");
                if (File.Exists(path3))
                {
                    _flexBoardViewPath = path3;
                    return;
                }

                string path4 = Path.Combine(parentDir, "FlexBoardView.exe");
                if (File.Exists(path4))
                {
                    _flexBoardViewPath = path4;
                    return;
                }
            }

            // FlexBoardView not found - will show warning
            _flexBoardViewPath = "";
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



                // Handle panel resize
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
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

        private async void EmbedFlexBoardView(Process process, System.Windows.Forms.Panel panel)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;

                // Wait longer for SDL window to be created (FlexBV takes time to initialize)
                for (int i = 0; i < 100; i++)
                {
                    await System.Threading.Tasks.Task.Delay(200);

                    if (process.HasExited)
                        return;

                    process.Refresh();
                    processHandle = process.MainWindowHandle;

                    // If MainWindowHandle is still zero, try to find window by process ID
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }

                    if (processHandle != IntPtr.Zero)
                        break;
                }

                if (processHandle == IntPtr.Zero)
                {
                    Dispatcher.Invoke(() => ShowStatus("Could not get FlexBoardView window handle", false));
                    return;
                }

                // For SDL apps, try a gentler embedding approach
                // Set as child of panel
                SetParent(processHandle, panel.Handle);

                // Keep window styles mostly intact for SDL compatibility
                // Only remove caption and borders, keep other styles
                int style = GetWindowLong(processHandle, GWL_STYLE);
                style = style & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_BORDER;
                style = style | WS_CHILD | WS_VISIBLE;
                SetWindowLong(processHandle, GWL_STYLE, style);

                // Modify extended styles minimally
                int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                exStyle = exStyle & ~WS_EX_WINDOWEDGE & ~WS_EX_CLIENTEDGE;
                SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                // Move and resize to fill the panel
                MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);

                // Force redraw
                ShowWindow(processHandle, SW_SHOW);

                // Setup resize handler
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                };



                Dispatcher.Invoke(() => ShowStatus("FlexBoardView embedded successfully", true));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding FlexBoardView: {ex.Message}");
                Dispatcher.Invoke(() => ShowStatus($"Failed to embed FlexBoardView: {ex.Message}", false));
            }
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



                // Handle panel resize
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                };


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding BoardViewer: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task EmbedProcessAsOverlay(Process process, System.Windows.Forms.Panel panel, ProcessInfo processInfo)
        {
            if (process == null || panel == null || processInfo == null)
            {
                return;
            }

            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;

                for (int i = 0; i < 50 && processHandle == IntPtr.Zero; i++)
                {
                    await System.Threading.Tasks.Task.Delay(100);

                    if (process.HasExited)
                        return;

                    process.Refresh();
                    processHandle = process.MainWindowHandle;

                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }
                }

                if (processHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("Could not get overlay window handle");
                    return;
                }

                processInfo.WindowHandle = processHandle;
                processInfo.IsOverlayWindow = true;
                processInfo.OverlayVisible = true;
                processInfo.OverlayRectInitialized = false;

                bool isFlexBoardView = string.Equals(processInfo.AppType, "FlexBoardView", StringComparison.OrdinalIgnoreCase);
                if (!isFlexBoardView)
                {
                    MoveWindow(processHandle, -10000, -10000, 1, 1, false);
                }
                if (!isFlexBoardView)
                {
                    int style = GetWindowLong(processHandle, GWL_STYLE);
                    style &= ~(WS_CAPTION | WS_BORDER | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_CHILD);
                    style |= WS_POPUP | WS_VISIBLE;
                    SetWindowLong(processHandle, GWL_STYLE, style);

                    int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                    exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
                    SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                    IntPtr ownerHandle = new WindowInteropHelper(this).Handle;
                    if (ownerHandle != IntPtr.Zero)
                    {
                        SetWindowLongPtr(processHandle, GWL_HWNDPARENT, ownerHandle);
                    }
                }
                else
                {
                    IntPtr ownerHandle = new WindowInteropHelper(this).Handle;
                    if (ownerHandle != IntPtr.Zero)
                    {
                        SetWindowLongPtr(processHandle, GWL_HWNDPARENT, ownerHandle);
                    }

                    int style = GetWindowLong(processHandle, GWL_STYLE);
                    style &= ~(WS_CAPTION | WS_BORDER | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_CHILD);
                    style |= WS_POPUP | WS_VISIBLE;
                    SetWindowLong(processHandle, GWL_STYLE, style);

                    int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_APPWINDOW;
                    exStyle |= WS_EX_TOOLWINDOW;
                    SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                    SetWindowPos(processHandle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }

                UpdateOverlayWindowPosition(processInfo);
                ShowWindow(processHandle, SW_SHOW);

                panel.Resize += (s, e) => UpdateOverlayWindowPosition(processInfo);
                processInfo.Host.SizeChanged += (s, e) => UpdateOverlayWindowPosition(processInfo);
                processInfo.Host.Loaded += (s, e) => UpdateOverlayWindowPosition(processInfo);
                Dispatcher.BeginInvoke(new Action(() => UpdateOverlayWindowPosition(processInfo)), DispatcherPriority.Loaded);

                UpdateOverlayWindowsForSelection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding overlay window: {ex.Message}");
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

        // Drag and drop event handlers for tab reordering
        private void TabControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tabItem = FindTabItemFromMousePosition(e.GetPosition(tabControl));
            if (tabItem != null && tabItem != _addTabButton)
            {
                _isDragging = false;
                _draggedTab = tabItem;
                _dragStartPoint = e.GetPosition(tabControl);
            }
        }

        private void TabControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedTab != null && !_isDragging)
            {
                var currentPoint = e.GetPosition(tabControl);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > 10 || Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 10)
                {
                    _isDragging = true;
                    _draggedTab.CaptureMouse();
                    _lastTargetTab = null; // Reset last target when starting drag
                }
            }
            else if (_isDragging && _draggedTab != null)
            {
                var targetTab = FindTabItemFromMousePosition(e.GetPosition(tabControl));
                if (targetTab != null && targetTab != _draggedTab && targetTab != _addTabButton && targetTab != _lastTargetTab)
                {
                    int draggedIndex = tabControl.Items.IndexOf(_draggedTab);
                    int targetIndex = tabControl.Items.IndexOf(targetTab);

                    if (draggedIndex != targetIndex)
                    {
                        tabControl.Items.RemoveAt(draggedIndex);
                        tabControl.Items.Insert(targetIndex, _draggedTab);
                        tabControl.SelectedItem = _draggedTab;
                        _lastTargetTab = targetTab; // Update last target
                    }
                }
            }
        }

        private void TabControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && _draggedTab != null)
            {
                _draggedTab.ReleaseMouseCapture();
            }
            _isDragging = false;
            _draggedTab = null;
            _lastTargetTab = null; // Reset last target when ending drag
        }

        private TabItem FindTabItemFromMousePosition(Point mousePosition)
        {
            var hitTestResult = VisualTreeHelper.HitTest(tabControl, mousePosition);
            if (hitTestResult != null)
            {
                var element = hitTestResult.VisualHit as FrameworkElement;
                while (element != null && !(element is TabItem))
                {
                    element = VisualTreeHelper.GetParent(element) as FrameworkElement;
                }
                return element as TabItem;
            }
            return null;
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
            Dispatcher.BeginInvoke(new Action(FocusSelectedEmbeddedWindow), DispatcherPriority.Background);
            Dispatcher.BeginInvoke(new Action(UpdateOverlayWindowsForSelection), DispatcherPriority.Background);
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateOverlayWindowsForSelection), DispatcherPriority.Background);
            Dispatcher.BeginInvoke(new Action(FocusSelectedEmbeddedWindow), DispatcherPriority.Background);
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateOverlayWindowsForSelection), DispatcherPriority.Background);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateOverlayWindowsForSelection), DispatcherPriority.Background);
        }

        private void FocusSelectedEmbeddedWindow()
        {
            if (tabControl.SelectedItem is TabItem selectedTab && _tabProcesses.TryGetValue(selectedTab, out ProcessInfo processInfo))
            {
                FocusEmbeddedWindow(processInfo);
            }
        }

        private void FocusEmbeddedWindow(ProcessInfo processInfo)
        {
            if (processInfo == null || processInfo.Process == null || processInfo.Process.HasExited)
            {
                return;
            }

            IntPtr windowHandle = GetEmbeddedWindowHandle(processInfo);
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (string.Equals(processInfo.AppType, "OpenBoardView", StringComparison.OrdinalIgnoreCase))
                {
                    SendMessage(windowHandle, WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero);
                    SendMessage(windowHandle, WM_ACTIVATE, new IntPtr(WA_ACTIVE), IntPtr.Zero);
                }

                uint targetProcessId;
                uint targetThreadId = GetWindowThreadProcessId(windowHandle, out targetProcessId);
                uint currentThreadId = GetCurrentThreadId();

                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                if (processInfo.IsOverlayWindow)
                {
                    SetForegroundWindow(windowHandle);
                    SetActiveWindow(windowHandle);
                }

                SetFocus(windowHandle);

                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }
            catch { }
        }

        private IntPtr GetEmbeddedWindowHandle(ProcessInfo processInfo)
        {
            if (processInfo == null)
            {
                return IntPtr.Zero;
            }

            if (processInfo.IsOverlayWindow && processInfo.WindowHandle != IntPtr.Zero)
            {
                return processInfo.WindowHandle;
            }

            if (processInfo.Panel != null)
            {
                try
                {
                    IntPtr childHandle = GetWindow(processInfo.Panel.Handle, GW_CHILD);
                    if (childHandle != IntPtr.Zero)
                    {
                        processInfo.WindowHandle = childHandle;
                        return childHandle;
                    }
                }
                catch { }
            }

            if (processInfo.WindowHandle != IntPtr.Zero)
            {
                return processInfo.WindowHandle;
            }

            if (processInfo.Process != null && !processInfo.Process.HasExited)
            {
                IntPtr handle = processInfo.Process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    handle = FindWindowByProcessId(processInfo.Process.Id);
                }

                if (handle != IntPtr.Zero)
                {
                    processInfo.WindowHandle = handle;
                }

                return handle;
            }

            return IntPtr.Zero;
        }

        private void UpdateOverlayWindowsForSelection()
        {
            if (WindowState == WindowState.Minimized)
            {
                HideAllOverlayWindows();
                return;
            }

            foreach (var kvp in _tabProcesses)
            {
                ProcessInfo processInfo = kvp.Value;
                if (!processInfo.IsOverlayWindow || processInfo.Process == null || processInfo.Process.HasExited)
                {
                    continue;
                }

                bool isFlexBoardView = string.Equals(processInfo.AppType, "FlexBoardView", StringComparison.OrdinalIgnoreCase);

                if (kvp.Key == tabControl.SelectedItem)
                {
                    if (processInfo.WindowHandle != IntPtr.Zero)
                    {
                        if (!processInfo.OverlayVisible)
                        {
                            ShowWindow(processInfo.WindowHandle, SW_SHOW);
                            processInfo.OverlayVisible = true;
                        }
                        UpdateOverlayWindowPosition(processInfo);
                    }
                }
                else
                {
                    if (processInfo.WindowHandle != IntPtr.Zero)
                    {
                        if (processInfo.OverlayVisible)
                        {
                            if (isFlexBoardView)
                            {
                                int x;
                                int y;
                                int width;
                                int height;
                                if (!TryGetOverlayRect(processInfo, out x, out y, out width, out height))
                                {
                                    width = 1;
                                    height = 1;
                                }
                                MoveWindow(processInfo.WindowHandle, -10000, -10000, width, height, true);
                                ShowWindow(processInfo.WindowHandle, SW_SHOW);
                            }
                            else
                            {
                                ShowWindow(processInfo.WindowHandle, SW_HIDE);
                            }
                            processInfo.OverlayVisible = false;
                            processInfo.OverlayRectInitialized = false;
                        }
                    }
                }
            }
        }

        private void HideAllOverlayWindows()
        {
            foreach (var kvp in _tabProcesses)
            {
                ProcessInfo processInfo = kvp.Value;
                if (processInfo.IsOverlayWindow && processInfo.WindowHandle != IntPtr.Zero)
                {
                    ShowWindow(processInfo.WindowHandle, SW_HIDE);
                }
            }
        }

        private void UpdateOverlayWindowPosition(ProcessInfo processInfo)
        {
            if (processInfo == null || !processInfo.IsOverlayWindow || processInfo.WindowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!processInfo.OverlayVisible)
            {
                return;
            }

            int x;
            int y;
            int width;
            int height;
            if (!TryGetOverlayRect(processInfo, out x, out y, out width, out height))
            {
                return;
            }

            try
            {
                if (processInfo.OverlayRectInitialized &&
                    processInfo.OverlayX == x &&
                    processInfo.OverlayY == y &&
                    processInfo.OverlayWidth == width &&
                    processInfo.OverlayHeight == height)
                {
                    return;
                }

                MoveWindow(processInfo.WindowHandle, x, y, width, height, true);
                processInfo.OverlayRectInitialized = true;
                processInfo.OverlayX = x;
                processInfo.OverlayY = y;
                processInfo.OverlayWidth = width;
                processInfo.OverlayHeight = height;
            }
            catch { }
        }

        private bool TryGetOverlayRect(ProcessInfo processInfo, out int x, out int y, out int width, out int height)
        {
            x = 0;
            y = 0;
            width = 0;
            height = 0;

            if (processInfo == null)
            {
                return false;
            }

            if (processInfo.Host != null && processInfo.Host.IsLoaded)
            {
                try
                {
                    System.Windows.Point topLeft = processInfo.Host.PointToScreen(new System.Windows.Point(0, 0));
                    var dpi = VisualTreeHelper.GetDpi(processInfo.Host);
                    int w = (int)Math.Round(processInfo.Host.ActualWidth * dpi.DpiScaleX);
                    int h = (int)Math.Round(processInfo.Host.ActualHeight * dpi.DpiScaleY);

                    if (w > 0 && h > 0)
                    {
                        x = (int)Math.Round(topLeft.X);
                        y = (int)Math.Round(topLeft.Y);
                        width = w;
                        height = h;
                        return true;
                    }
                }
                catch { }
            }

            if (processInfo.Panel != null)
            {
                try
                {
                    int w = processInfo.Panel.Width;
                    int h = processInfo.Panel.Height;
                    if (w > 0 && h > 0)
                    {
                        System.Drawing.Point screenPoint = processInfo.Panel.PointToScreen(new System.Drawing.Point(0, 0));
                        x = screenPoint.X;
                        y = screenPoint.Y;
                        width = w;
                        height = h;
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        // Forward keyboard events to embedded windows
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ForwardKeyboardEvent(e, WM_KEYDOWN, WM_SYSKEYDOWN);
        }

        private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            ForwardKeyboardEvent(e, WM_KEYUP, WM_SYSKEYUP);
        }

        private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            if (tabControl.SelectedItem is TabItem selectedTab &&
                _tabProcesses.TryGetValue(selectedTab, out ProcessInfo processInfo))
            {
                IntPtr windowHandle = GetEmbeddedWindowHandle(processInfo);
                if (windowHandle == IntPtr.Zero)
                {
                    return;
                }

                foreach (char ch in e.Text)
                {
                    SendMessage(windowHandle, WM_CHAR, new IntPtr(ch), IntPtr.Zero);
                }

                e.Handled = true;
            }
        }

        private void ForwardKeyboardEvent(KeyEventArgs e, uint normalMsg, uint sysMsg)
        {
            // Only forward if we have an active embedded window
            if (tabControl.SelectedItem is TabItem selectedTab && _tabProcesses.ContainsKey(selectedTab))
            {
                ProcessInfo processInfo = _tabProcesses[selectedTab];
                if (processInfo.Process != null && !processInfo.Process.HasExited)
                {
                    IntPtr windowHandle = GetEmbeddedWindowHandle(processInfo);

                    if (windowHandle != IntPtr.Zero)
                    {
                        if (normalMsg == WM_KEYDOWN)
                        {
                            FocusEmbeddedWindow(processInfo);
                        }

                        // Convert WPF key to virtual key code
                        int virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
                        
                        // Determine if it's a system key (Alt pressed)
                        bool isSystemKey = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                        uint message = isSystemKey ? sysMsg : normalMsg;

                        // Build lParam (repeat count, scan code, extended key flag, etc.)
                        // For simplicity, we use basic values here
                        IntPtr lParam = IntPtr.Zero;

                        // Send the message to the embedded window
                        SendMessage(windowHandle, message, new IntPtr(virtualKey), lParam);

                        // Mark event as handled for certain keys to prevent WPF from processing them
                        // This allows shortcuts like Ctrl+F to go directly to the embedded app
                        if (ShouldHandleKey(e.Key))
                        {
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private bool ShouldHandleKey(Key key)
        {
            // Handle common shortcuts that should go to embedded apps
            // You can customize this list based on your needs
            
            // Don't handle if no modifiers (allow normal typing in search boxes, etc.)
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                return false;
            }

            // Handle Ctrl+key combinations (common shortcuts)
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                switch (key)
                {
                    case Key.F:      // Find
                    case Key.G:      // Find next
                    case Key.H:      // Replace
                    case Key.Z:      // Undo
                    case Key.Y:      // Redo
                    case Key.C:      // Copy
                    case Key.V:      // Paste
                    case Key.X:      // Cut
                    case Key.A:      // Select all
                    case Key.S:      // Save
                    case Key.O:      // Open
                    case Key.P:      // Print
                    case Key.W:      // Close (but we handle this in WPF)
                        return key != Key.W; // Don't forward Ctrl+W, let WPF handle tab closing
                    default:
                        return false;
                }
            }

            // Handle function keys
            if (key >= Key.F1 && key <= Key.F12)
            {
                return true;
            }

            return false;
        }


        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            _resizeTimer.Stop();

            if (tabControl.SelectedItem is TabItem selectedTab && _tabProcesses.ContainsKey(selectedTab))
            {
                ProcessInfo processInfo = _tabProcesses[selectedTab];
                if (processInfo.Process != null && !processInfo.Process.HasExited)
                {
                    if (processInfo.IsOverlayWindow)
                    {
                        UpdateOverlayWindowPosition(processInfo);
                        return;
                    }

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

            // Close calculator if open
            try
            {
                if (_voltageDividerProcess != null && !_voltageDividerProcess.HasExited)
                {
                    _voltageDividerProcess.Kill();
                    _voltageDividerProcess.Dispose();
                }
            }
            catch { }

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

        private async System.Threading.Tasks.Task EmbedFlexBoardViewProcess(Process process, System.Windows.Forms.Panel panel)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;

                // Wait longer for SDL window to be created (FlexBV takes time to initialize)
                for (int i = 0; i < 100; i++)
                {
                    await System.Threading.Tasks.Task.Delay(200);

                    if (process.HasExited)
                        return;

                    process.Refresh();
                    processHandle = process.MainWindowHandle;

                    // If MainWindowHandle is still zero, try to find window by process ID
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }

                    if (processHandle != IntPtr.Zero)
                        break;
                }

                if (processHandle == IntPtr.Zero)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Fall back to showing message if embedding fails
                        TextBlock messageBlock = new TextBlock
                        {
                            Text = "FlexBoardView is running in a separate window.\n\nUse the context menu to switch viewers.",
                            FontSize = 14,
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center
                        };
                        // Find the tab and set content
                        foreach (var kvp in _tabProcesses)
                        {
                            if (kvp.Value.Process == process)
                            {
                                kvp.Key.Content = messageBlock;
                                break;
                            }
                        }
                        ShowStatus("Could not embed FlexBoardView - running separately", false);
                    });
                    return;
                }

                // For SDL apps, try a gentler embedding approach
                // Set as child of panel
                SetParent(processHandle, panel.Handle);

                // Keep window styles mostly intact for SDL compatibility
                // Only remove caption and borders, keep other styles
                int style = GetWindowLong(processHandle, GWL_STYLE);
                style = style & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_BORDER;
                style = style | WS_CHILD | WS_VISIBLE;
                SetWindowLong(processHandle, GWL_STYLE, style);

                // Modify extended styles minimally
                int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                exStyle = exStyle & ~WS_EX_WINDOWEDGE & ~WS_EX_CLIENTEDGE;
                SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                // Move and resize to fill the panel
                MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);

                // Force redraw
                ShowWindow(processHandle, SW_SHOW);

                // Setup resize handler
                panel.Resize += (s, e) =>
                {
                    if (!process.HasExited && processHandle != IntPtr.Zero)
                    {
                        MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                    }
                };



                Dispatcher.Invoke(() => ShowStatus("FlexBoardView embedded successfully", true));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding FlexBoardView: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    // Fall back to message on error
                    TextBlock messageBlock = new TextBlock
                    {
                        Text = "FlexBoardView encountered an error.\n\nUse the context menu to switch viewers.",
                        FontSize = 14,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red),
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    };
                    // Find the tab and set content
                    foreach (var kvp in _tabProcesses)
                    {
                        if (kvp.Value.Process == process)
                        {
                            kvp.Key.Content = messageBlock;
                            break;
                        }
                    }
                    ShowStatus($"Failed to embed FlexBoardView: {ex.Message}", false);
                });
            }
        }

        private async System.Threading.Tasks.Task EmbedFlexBoardViewSafely(Process process, System.Windows.Forms.Panel panel, TabItem tab, string filePath)
        {
            try
            {
                IntPtr processHandle = IntPtr.Zero;
                int processId = process.Id;

                // Wait for SDL window to be created (FlexBV takes time to initialize)
                // Use timeout similar to other viewers for consistency
                for (int i = 0; i < 50; i++) // 5 seconds total (100ms * 50) - same as BoardViewer
                {
                    await System.Threading.Tasks.Task.Delay(100);

                    if (process.HasExited)
                    {
                        // Process exited before we could embed it
                        Dispatcher.Invoke(() => ShowSeparateWindowMessage(tab, filePath, "FlexBoardView exited before embedding"));
                        return;
                    }

                    process.Refresh();
                    processHandle = process.MainWindowHandle;

                    // If MainWindowHandle is still zero, try to find window by process ID
                    if (processHandle == IntPtr.Zero)
                    {
                        processHandle = FindWindowByProcessId(processId);
                    }

                    if (processHandle != IntPtr.Zero)
                        break;
                }

                if (processHandle == IntPtr.Zero)
                {
                    // Could not find window handle - fall back to separate window
                    Dispatcher.Invoke(() => ShowSeparateWindowMessage(tab, filePath, "Could not find FlexBoardView window"));
                    return;
                }

                // Try minimal embedding approach for SDL apps
                // Remove window decorations but keep SDL compatibility
                try
                {
                    // Remove window borders and caption for cleaner look
                    int style = GetWindowLong(processHandle, GWL_STYLE);
                    style = style & ~WS_CAPTION & ~WS_BORDER & ~WS_THICKFRAME;
                    style = style | WS_CHILD | WS_VISIBLE;
                    SetWindowLong(processHandle, GWL_STYLE, style);

                    // Remove extended window styles for cleaner appearance
                    int exStyle = GetWindowLong(processHandle, GWL_EXSTYLE);
                    exStyle = exStyle & ~WS_EX_WINDOWEDGE & ~WS_EX_CLIENTEDGE & ~WS_EX_STATICEDGE;
                    SetWindowLong(processHandle, GWL_EXSTYLE, exStyle);

                    // Now set parent after modifying styles
                    SetParent(processHandle, panel.Handle);

                    // Move to fill the panel but keep original window style
                    MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);

                    // Force redraw
                    ShowWindow(processHandle, SW_SHOW);

                    // Setup resize handler
                    panel.Resize += (s, e) =>
                    {
                        if (!process.HasExited && processHandle != IntPtr.Zero)
                        {
                            try
                            {
                                MoveWindow(processHandle, 0, 0, panel.Width, panel.Height, true);
                            }
                            catch { /* Ignore resize errors */ }
                        }
                    };

                    // Wait a bit more after embedding to ensure stability
                    await System.Threading.Tasks.Task.Delay(500);

                    // Check if process is still running after embedding
                    if (process.HasExited)
                    {
                        Dispatcher.Invoke(() => ShowSeparateWindowMessage(tab, filePath, "FlexBoardView crashed after embedding"));
                        return;
                    }

                    Dispatcher.Invoke(() => ShowStatus("FlexBoardView embedded successfully", true));
                }
                catch (Exception embedEx)
                {
                    Debug.WriteLine($"Embedding failed, falling back to separate window: {embedEx.Message}");
                    Dispatcher.Invoke(() => ShowSeparateWindowMessage(tab, filePath, $"Embedding failed: {embedEx.Message}"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in safe embedding: {ex.Message}");
                Dispatcher.Invoke(() => ShowSeparateWindowMessage(tab, filePath, $"Error: {ex.Message}"));
            }
        }

        private void CleanFlexBoardViewLogs()
        {
            try
            {
                // Clean logs in the correct FlexBV5 folder
                string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string flexBvLogsPath = Path.Combine(localAppDataPath, "FlexBV5", "logs");

                if (Directory.Exists(flexBvLogsPath))
                {
                    try
                    {
                        Directory.Delete(flexBvLogsPath, true);
                        Debug.WriteLine($"Cleaned FlexBoardView logs folder: {flexBvLogsPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete logs folder {flexBvLogsPath}: {ex.Message}");
                    }
                }

                // Also try to clean logs in FlexBoardView directory if it exists
                string flexBvDir = Path.GetDirectoryName(_flexBoardViewPath);
                string exeLogsPath = Path.Combine(flexBvDir, "logs");

                if (Directory.Exists(exeLogsPath))
                {
                    try
                    {
                        Directory.Delete(exeLogsPath, true);
                        Debug.WriteLine($"Cleaned FlexBoardView logs in exe directory: {exeLogsPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete exe logs folder {exeLogsPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning FlexBoardView logs: {ex.Message}");
            }
        }

        private void ShowSeparateWindowMessage(TabItem tab, string filePath, string reason)
        {
            TextBlock messageBlock = new TextBlock
            {
                Text = $"FlexBoardView is running in a separate window.\n\nFile: {Path.GetFileName(filePath)}\n\nReason: {reason}\n\nUse the context menu to switch viewers.",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            tab.Content = messageBlock;
            ShowStatus($"FlexBoardView running separately: {reason}", false);
        }

        private async System.Threading.Tasks.Task HandleFlexBoardViewCrash(Process process)
        {
            try
            {
                // Wait for crash dialog to appear
                await System.Threading.Tasks.Task.Delay(2000);

                // Find FlexBV crash dialog window
                IntPtr crashDialogHandle = IntPtr.Zero;

                // Use Windows API to enumerate windows
                EnumWindows((hwnd, lParam) =>
                {
                    const int nChars = 256;
                    StringBuilder buff = new StringBuilder(nChars);
                    if (GetWindowText(hwnd, buff, nChars) > 0)
                    {
                        string title = buff.ToString();
                        if (title.Contains("FlexBV") && (title.Contains("crash") || title.Contains("error") || title.Contains("Crash")))
                        {
                            crashDialogHandle = hwnd;
                            return false; // Stop enumeration
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);

                if (crashDialogHandle != IntPtr.Zero)
                {
                    Debug.WriteLine($"Found FlexBV crash dialog: {crashDialogHandle}");

                    // Find "Erase crash logs and ignore" button
                    IntPtr buttonHandle = FindWindowEx(crashDialogHandle, IntPtr.Zero, "Button", null);
                    while (buttonHandle != IntPtr.Zero)
                    {
                        const int nChars = 256;
                        StringBuilder buff = new StringBuilder(nChars);
                        if (GetWindowText(buttonHandle, buff, nChars) > 0)
                        {
                            string buttonText = buff.ToString();
                            if (buttonText.Contains("Erase crash logs and ignore") || buttonText.Contains("Erase") || buttonText.Contains("ignore"))
                            {
                                Debug.WriteLine($"Found button: {buttonText}");

                                // Click the button
                                SendMessage(buttonHandle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);

                                Dispatcher.Invoke(() => ShowStatus("Handled FlexBV crash dialog", true));
                                break;
                            }
                        }
                        buttonHandle = FindWindowEx(crashDialogHandle, buttonHandle, "Button", null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling FlexBV crash: {ex.Message}");
            }
        }

        private void ShowStatus(string message, bool isSuccess)
        {
            // Status bar removed - do nothing
            Debug.WriteLine($"Status: {message}");
        }
    }
}
