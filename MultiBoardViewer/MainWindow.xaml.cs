using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

        private class ProcessInfo
        {
            public Process Process { get; set; }
            public WindowsFormsHost Host { get; set; }
            public System.Windows.Forms.Panel Panel { get; set; }
            public string TempDirectory { get; set; }
            public string AppType { get; set; } // "BoardViewer" or "SumatraPDF"
            public IntPtr WindowHandle { get; set; } // Store window handle for resize
        }

        public MainWindow()
        {
            InitializeComponent();
            
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
            
            // Create initial empty tab on startup
            CreateEmptyTab();
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
            
            _addTabButton.ToolTip = "New Tab";
            
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

        private void CreateEmptyTab()
        {
            // Create new empty tab with drop zone
            TabItem newTab = new TabItem
            {
                Header = "New Tab"
            };

            // Create a border as drop zone
            Border dropZone = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)),
                AllowDrop = true
            };

            // Create content for drop zone
            StackPanel content = new StackPanel
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
                Margin = new Thickness(0, 10, 0, 5)
            };

            TextBlock hint = new TextBlock
            {
                Text = "PDF & BoardViewer files supported",
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Create "Open File" button
            Button openFileButton = new Button
            {
                Content = "➕ Open File",
                FontSize = 14,
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 20, 0, 0),
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
                    Filter = "All Supported Files|*.pdf;*.fz;*.brd;*.bom;*.cad|PDF Files|*.pdf|BoardViewer Files|*.fz;*.brd;*.bom;*.cad|All Files|*.*",
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

            content.Children.Add(icon);
            content.Children.Add(text);
            content.Children.Add(hint);
            content.Children.Add(openFileButton);
            dropZone.Child = content;

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

            newTab.Content = dropZone;

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

        // Check if current tab is an empty drop zone tab
        private bool IsCurrentTabEmpty()
        {
            if (tabControl.SelectedItem is TabItem selectedTab)
            {
                return selectedTab.Content is Border;
            }
            return false;
        }

        // Drag & Drop handlers
        private void Window_DragOver(object sender, DragEventArgs e)
        {
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
                    WindowHandle = IntPtr.Zero
                };
                _tabProcesses[tab] = processInfo;

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
                    WindowHandle = IntPtr.Zero
                };

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
                    AppType = "BoardViewer"
                };

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
                    WindowHandle = IntPtr.Zero
                };
                _tabProcesses[newTab] = processInfo;

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
            // Close all processes when window is closing
            foreach (var kvp in _tabProcesses)
            {
                try
                {
                    if (kvp.Value.Process != null && !kvp.Value.Process.HasExited)
                    {
                        kvp.Value.Process.CloseMainWindow();
                        kvp.Value.Process.WaitForExit(1000);
                        
                        if (!kvp.Value.Process.HasExited)
                        {
                            kvp.Value.Process.Kill();
                        }
                        
                        kvp.Value.Process.Dispose();
                    }

                    // Clean up temp directory
                    if (!string.IsNullOrEmpty(kvp.Value.TempDirectory) && Directory.Exists(kvp.Value.TempDirectory))
                    {
                        try
                        {
                            Directory.Delete(kvp.Value.TempDirectory, true);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing process on exit: {ex.Message}");
                }
            }

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
