using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultiBoardViewer.Services
{
    public class RecentFilesService
    {
        private List<string> _recentFiles = new List<string>();
        private const int MaxRecentFiles = 10;
        private const string RecentFilesFileName = "recent_files.txt";

        public event EventHandler RecentFilesChanged;

        public RecentFilesService()
        {
            LoadRecentFiles();
        }

        public List<string> GetRecentFiles()
        {
            return new List<string>(_recentFiles);
        }

        public void AddToRecentFiles(string filePath)
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

            SaveRecentFiles();
            RecentFilesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveFile(string filePath)
        {
            _recentFiles.RemoveAll(f => f.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            SaveRecentFiles();
            RecentFilesChanged?.Invoke(this, EventArgs.Empty);
        }

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
    }
}
