using System;
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

        public static string[] StartupFiles { get; private set; }
        public static event Action<string[]> FilesReceived;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Try to create mutex to check if another instance is running
            _mutex = new Mutex(true, MutexName, out _isFirstInstance);

            if (!_isFirstInstance)
            {
                // Another instance is running, send files to it
                if (e.Args != null && e.Args.Length > 0)
                {
                    SendFilesToRunningInstance(e.Args);
                }
                else
                {
                    // No files, just activate the existing window
                    SendFilesToRunningInstance(new string[] { "__ACTIVATE__" });
                }
                
                // Exit this instance
                Shutdown();
                return;
            }

            // This is the first instance
            base.OnStartup(e);
            
            // Store command line arguments (files to open)
            if (e.Args != null && e.Args.Length > 0)
            {
                StartupFiles = e.Args;
            }

            // Start pipe server to receive files from other instances
            StartPipeServer();
        }

        private void StartPipeServer()
        {
            _pipeServerThread = new Thread(PipeServerLoop)
            {
                IsBackground = true
            };
            _pipeServerThread.Start();
        }

        private void PipeServerLoop()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        server.WaitForConnection();
                        
                        using (var reader = new StreamReader(server))
                        {
                            string data = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(data))
                            {
                                string[] files = data.Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries);
                                
                                // Invoke on UI thread
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    FilesReceived?.Invoke(files);
                                }));
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Pipe was closed or error occurred
                    break;
                }
            }
        }

        private void SendFilesToRunningInstance(string[] files)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(3000); // 3 second timeout
                    
                    using (var writer = new StreamWriter(client))
                    {
                        writer.Write(string.Join("|", files));
                        writer.Flush();
                    }
                }
            }
            catch (Exception)
            {
                // Failed to connect to running instance
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
