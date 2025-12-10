using System;
using System.Windows;

namespace MultiBoardViewer
{
    public partial class App : Application
    {
        public static string[] StartupFiles { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Store command line arguments (files to open)
            if (e.Args != null && e.Args.Length > 0)
            {
                StartupFiles = e.Args;
            }
        }
    }
}
