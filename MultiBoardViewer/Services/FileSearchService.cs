using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiBoardViewer.Services
{
    public class FileSearchService
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".fz", ".brd", ".bom", ".cad", ".bdv", ".asc", ".bv", ".cst", ".gr", ".f2b", ".faz", ".tvw"
        };

        private string _searchFolder;
        private const string SearchFolderFileName = "search_folder.txt";

        public FileSearchService()
        {
            LoadSearchFolder();
        }

        public string SearchFolder
        {
            get => _searchFolder;
            set
            {
                _searchFolder = value;
                SaveSearchFolder();
            }
        }

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

        private void SaveSearchFolder()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string searchFolderPath = Path.Combine(appDir, SearchFolderFileName);
                if (!string.IsNullOrEmpty(_searchFolder))
                {
                    File.WriteAllText(searchFolderPath, _searchFolder);
                }
            }
            catch { }
        }

        public async Task<List<string>> SearchFilesAsync(string searchText, CancellationToken cancellationToken)
        {
            var results = new List<string>();

            if (string.IsNullOrEmpty(_searchFolder) || !Directory.Exists(_searchFolder))
                return results;

            if (string.IsNullOrWhiteSpace(searchText))
                return results;

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var files = SafeEnumerateFiles(_searchFolder, cancellationToken);
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

        private IEnumerable<string> SafeEnumerateFiles(string rootPath, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var path = pending.Pop();
                string[] files = null;

                try
                {
                    files = Directory.GetFiles(path);
                }
                catch { } // Ignore permission errors

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        yield return file;
                    }
                }

                try
                {
                    var subDirs = Directory.GetDirectories(path);
                    foreach (var subdir in subDirs)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            yield break;

                        // Skip system directories to safe time and avoid potential loops/waiting
                        string dirName = Path.GetFileName(subdir);
                        if (string.IsNullOrEmpty(dirName)) continue;

                        if (!dirName.StartsWith("$") &&
                            !dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) &&
                            !dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                        {
                            pending.Push(subdir);
                        }
                    }
                }
                catch { } // Ignore permission errors
            }
        }
    }
}
