using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace MultiBoardViewer
{
    public partial class App : Application
    {
        private const string MutexName = "MultiBoardViewer_SingleInstance_Mutex";
        private const string PipeName = "MultiBoardViewer_Pipe";
        private static Mutex _mutex;
        private Thread _pipeServerThread;
        private bool _isFirstInstance;
        private CancellationTokenSource _cancellationTokenSource;

        public static string[] StartupFiles { get; private set; }
        public static event Action<string[]> FilesReceived;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (RedirectLegacyExecutable(e.Args))
            {
                Shutdown();
                return;
            }

            // Try to create mutex to check if another instance is running
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
                _isFirstInstance = createdNew;
            }
            catch (AbandonedMutexException)
            {
                // Previous instance crashed, we can take over
                _isFirstInstance = true;
            }

            if (!_isFirstInstance)
            {
                // Another instance is running, send files to it
                try
                {
                    if (e.Args != null && e.Args.Length > 0)
                    {
                        SendFilesToRunningInstance(e.Args);
                    }
                    else
                    {
                        SendFilesToRunningInstance(new string[] { "__ACTIVATE__" });
                    }
                }
                catch { }
                
                // Release mutex and exit
                try { _mutex?.Dispose(); } catch { }
                Shutdown();
                return;
            }

            // This is the first instance
            base.OnStartup(e);
            
            // Store command line arguments
            if (e.Args != null && e.Args.Length > 0)
            {
                StartupFiles = e.Args;
            }

            // Start pipe server
            _cancellationTokenSource = new CancellationTokenSource();
            StartPipeServer();
        }

        private bool RedirectLegacyExecutable(string[] args)
        {
            string currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath))
            {
                return false;
            }

            if (!Path.GetFileName(currentExePath).Equals("Multi BoardViewer.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string preferredExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MultiBoardViewer.exe");
            if (!File.Exists(preferredExePath))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = preferredExePath,
                    Arguments = BuildCommandLineArguments(args),
                    UseShellExecute = true
                });
                ScheduleLegacyExecutableDelete(currentExePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string BuildCommandLineArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return "";
            }

            string[] quotedArgs = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                quotedArgs[i] = "\"" + args[i].Replace("\"", "\\\"") + "\"";
            }
            return string.Join(" ", quotedArgs);
        }

        private void ScheduleLegacyExecutableDelete(string legacyExePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 /nobreak >nul & del /f /q \"{legacyExePath}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        private void StartPipeServer()
        {
            _pipeServerThread = new Thread(PipeServerLoop)
            {
                IsBackground = true,
                Name = "PipeServerThread"
            };
            _pipeServerThread.Start();
        }

        private void PipeServerLoop()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, 
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    
                    // Wait for connection with cancellation support
                    var asyncResult = server.BeginWaitForConnection(null, null);
                    
                    // Wait with timeout to allow checking cancellation
                    while (!asyncResult.AsyncWaitHandle.WaitOne(500))
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            server.Dispose();
                            return;
                        }
                    }
                    
                    server.EndWaitForConnection(asyncResult);
                    
                    using (var reader = new StreamReader(server))
                    {
                        string data = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(data))
                        {
                            string[] files = data.Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries);
                            
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    FilesReceived?.Invoke(files);
                                }
                                catch { }
                            }));
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore errors and continue loop
                }
                finally
                {
                    try { server?.Dispose(); } catch { }
                }
            }
        }

        private void SendFilesToRunningInstance(string[] files)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000); // 2 second timeout
                    
                    using (var writer = new StreamWriter(client))
                    {
                        writer.Write(string.Join("|", files));
                        writer.Flush();
                    }
                }
            }
            catch (TimeoutException)
            {
                // Running instance not responding, user can try again
            }
            catch (Exception)
            {
                // Failed to connect
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Signal pipe server to stop
            try { _cancellationTokenSource?.Cancel(); } catch { }
            
            // Wait briefly for thread to exit
            try { _pipeServerThread?.Join(1000); } catch { }
            
            // Release mutex
            try
            {
                if (_isFirstInstance && _mutex != null)
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch { }
            
            try { _mutex?.Dispose(); } catch { }
            try { _cancellationTokenSource?.Dispose(); } catch { }
            
            base.OnExit(e);
        }
    }
}
