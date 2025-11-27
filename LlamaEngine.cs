using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace llama_server_winui
{
    public partial class LlamaEngine : ObservableObject
    {
        public string Name { get; set; }       // e.g., "CUDA llama.cpp (Windows)"
        public string Description { get; set; } // e.g., "Nvidia CUDA accelerated..."
        public string Tag { get; set; } = "Engine";

        [ObservableProperty]
        private string _currentVersion; // e.g., "v1.56.0"

        [ObservableProperty]
        private string _latestVersion;  // e.g., "v1.58.0"

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isServerRunning;

        private Process _serverProcess;

        // The direct download link from GitHub Releases
        public string DownloadUrl { get; set; }

        // Action: Download and Install
        [RelayCommand]
        private async Task DownloadAndInstall()
        {
            IsDownloading = true;
            StatusMessage = "Downloading...";

            string localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            string zipPath = Path.Combine(localFolder, $"{Name}.zip");
            string extractPath = Path.Combine(localFolder, "Engines", Name);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Simple download with progress (simplified for brevity)
                    var data = await client.GetByteArrayAsync(DownloadUrl);
                    await File.WriteAllBytesAsync(zipPath, data);
                }

                StatusMessage = "Extracting...";
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                CurrentVersion = LatestVersion;
                IsInstalled = true;
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error: " + ex.Message;
            }
            finally
            {
                IsDownloading = false;
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }

        // Action: Run the Server
        [RelayCommand]
        private void RunServer()
        {
            string localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            string exePath = Path.Combine(localFolder, "Engines", Name, "llama-server.exe");

            if (File.Exists(exePath))
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--port 8080", // Add your default args here
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                _serverProcess = Process.Start(psi);
                if (_serverProcess != null)
                {
                    IsServerRunning = true;
                    StatusMessage = "Server is running...";
                }
            }
            else
            {
                StatusMessage = "Executable not found!";
            }
        }

        // Action: Stop the server
        [RelayCommand]
        public void StopServer()
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess = null;
                IsServerRunning = false;
                StatusMessage = "Server stopped.";
            }
        }

        // Add this inside the public partial class LlamaEngine : ObservableObject
        public Visibility InverseVisibility(bool isInstalled)
        {
            // If installed (true) -> Collapsed (Hidden)
            // If not installed (false) -> Visible (Show Download button)
            return isInstalled ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}