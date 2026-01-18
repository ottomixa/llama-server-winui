using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using llama_server_winui.Services;

namespace llama_server_winui
{
    public partial class LlamaEngine : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tag { get; set; } = "Engine";
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotesUrl { get; set; } = string.Empty;

        [ObservableProperty]
        private string _currentVersion = string.Empty;

        [ObservableProperty]
        private string _latestVersion = string.Empty;

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isServerRunning;

        // Installation details properties
        [ObservableProperty]
        private string _zipDownloadPath = string.Empty;

        [ObservableProperty]
        private string _extractedFolderPath = string.Empty;

        [ObservableProperty]
        private bool _isLlamaServerExePresent;

        [ObservableProperty]
        private string _llamaServerExePath = string.Empty;

        [ObservableProperty]
        private string _llamaServerVersion = string.Empty;

        private Process? _serverProcess;
        
        // Polling state
        private DispatcherQueueTimer? _progressTimer;
        private readonly InterlockedProgress _tracker = new();
        private CancellationTokenSource? _downloadCts;

        // Atomic progress tracker
        private class InterlockedProgress : IProgress<DownloadProgressInfo>
        {
            private long _bytesDownloaded;
            private long _totalBytes;
            private double _speed;
            
            public long BytesDownloaded => Interlocked.Read(ref _bytesDownloaded);
            public long TotalBytes => Interlocked.Read(ref _totalBytes);
            public double Speed => _speed; // Double read is atomic on 64-bit, good enough for UI
            
            public volatile bool IsComplete;
            public volatile bool IsFailed;
            public volatile string? ErrorMessage;

            public void Report(DownloadProgressInfo value)
            {
                Interlocked.Exchange(ref _bytesDownloaded, value.BytesDownloaded);
                Interlocked.Exchange(ref _totalBytes, value.TotalBytes);
                _speed = value.SpeedMBps;
            }

            public void Reset()
            {
                Interlocked.Exchange(ref _bytesDownloaded, 0);
                Interlocked.Exchange(ref _totalBytes, -1);
                _speed = 0;
                IsComplete = false;
                IsFailed = false;
                ErrorMessage = null;
            }
        }

        private string GetAppStoragePath()
        {
            // Use LocalAppData for unpackaged app compatibility
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(baseDir, "LlamaServerWinUI");
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
            return appDir;
        }

        private void Log(string message)
        {
            try 
            {
                string path = Path.Combine(GetAppStoragePath(), "debug_log.txt");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch { }
        }

        [RelayCommand]
        private async Task DownloadAndInstall()
        {
            if (IsDownloading) return;

            try
            {
                IsDownloading = true;
                DownloadProgress = 0;
                StatusMessage = "Starting...";
                Log($"Starting download for {Name}...");
                
                _tracker.Reset();
                _downloadCts = new CancellationTokenSource();

                string localFolder = GetAppStoragePath();
                string zipPath = Path.Combine(localFolder, $"{Name}.zip");
                string extractPath = Path.Combine(localFolder, "Engines", Name);
                string url = DownloadUrl;

                Log($"Paths: Zip={zipPath}, Extract={extractPath}, URL={url}");

                if (string.IsNullOrEmpty(url))
                {
                    StatusMessage = "Error: Invalid URL";
                    Log("Error: Invalid URL");
                    IsDownloading = false;
                    return;
                }

                // Start background task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Log("Background task started.");
                        var downloader = new FileDownloader();
                        await downloader.DownloadFileAsync(url, zipPath, _tracker, _downloadCts.Token);
                        _tracker.IsComplete = true; // Mark done
                        Log("Download completed successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Download failed: {ex}");
                        _tracker.ErrorMessage = ex.Message;
                        _tracker.IsFailed = true;
                    }
                });

                // Start polling timer
                StartProgressTimer(zipPath, extractPath);
                Log("Progress timer started.");
            }
            catch (Exception ex)
            {
                Log($"Critical Error in DownloadAndInstall: {ex}");
                StatusMessage = $"Error starting: {ex.Message}";
                IsDownloading = false;
            }
            await Task.CompletedTask;
        }

        private void StartProgressTimer(string zipPath, string extractPath)
        {
            var dispatcher = App.MainDispatcher;
            if (dispatcher == null) 
            {
                StatusMessage = "Error: UI Dispatcher unavailable";
                Log("Error: Dispatcher is null");
                IsDownloading = false;
                return;
            }

            _progressTimer = dispatcher.CreateTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(100);
            _progressTimer.Tick += (s, e) => 
            {
                try
                {
                    UpdateUI(zipPath, extractPath);
                }
                catch (Exception ex)
                {
                    // Last resort safety net
                    if (_progressTimer != null) _progressTimer.Stop();
                    Log($"Timer Tick Crash: {ex}");
                }
            };
            _progressTimer.Start();
        }

        private void UpdateUI(string zipPath, string extractPath)
        {
            // Check for completion/failure first
            if (_tracker.IsFailed)
            {
                _progressTimer?.Stop();
                StatusMessage = $"Error: {_tracker.ErrorMessage}";
                IsDownloading = false;
                DownloadProgress = 0;
                CleanupFile(zipPath);
                return;
            }

            if (_tracker.IsComplete)
            {
                _progressTimer?.Stop();
                StatusMessage = "Extracting...";
                
                Task.Run(() =>
                {
                    try
                    {
                        Log($"Extracting to {extractPath}...");
                        if (Directory.Exists(extractPath))
                            Directory.Delete(extractPath, true);
                        ZipFile.ExtractToDirectory(zipPath, extractPath);
                        Log("Extraction complete.");

                        // Find llama-server.exe and get version info
                        string exePath = FindLlamaServerExe(extractPath);
                        string version = string.Empty;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            version = GetLlamaServerVersionOutput(exePath);
                        }
                        
                        App.MainDispatcher?.TryEnqueue(() =>
                        {
                            CurrentVersion = LatestVersion;
                            IsInstalled = true;
                            StatusMessage = "Ready ✓";
                            
                            // Update installation detail properties
                            ZipDownloadPath = zipPath;
                            ExtractedFolderPath = extractPath;
                            LlamaServerExePath = exePath;
                            IsLlamaServerExePresent = !string.IsNullOrEmpty(exePath);
                            LlamaServerVersion = version;
                            IsDownloading = false;
                            DownloadProgress = 0;
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"Extraction failed: {ex}");
                        App.MainDispatcher?.TryEnqueue(() =>
                        {
                            StatusMessage = $"Extract error: {ex.Message}";
                            IsDownloading = false;
                            DownloadProgress = 0;
                        });
                    }
                    finally
                    {
                        CleanupFile(zipPath);
                    }
                });
                return;
            }

            // Normal progress update
            var downloaded = _tracker.BytesDownloaded;
            var total = _tracker.TotalBytes;
            var speed = _tracker.Speed;

            var downloadedMB = downloaded / 1048576.0;
            
            if (total > 0)
            {
                var totalMB = total / 1048576.0;
                var pct = (double)downloaded / total * 100;
                
                // Safety check for NaN/Infinity
                if (double.IsNaN(pct) || double.IsInfinity(pct)) pct = 0;
                
                DownloadProgress = pct;
                StatusMessage = $"{downloadedMB:F1} / {totalMB:F1} MB ({speed:F1} MB/s)";
            }
            else
            {
                StatusMessage = $"{downloadedMB:F1} MB ({speed:F1} MB/s)";
            }
        }

        private void CleanupFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Searches for llama-server.exe in the extracted folder. 
        /// Returns the full path if found, or empty string if not found.
        /// </summary>
        private string FindLlamaServerExe(string extractPath)
        {
            try
            {
                // First check direct path
                string directPath = Path.Combine(extractPath, "llama-server.exe");
                if (File.Exists(directPath))
                {
                    Log($"Found llama-server.exe at: {directPath}");
                    return directPath;
                }

                // Search recursively
                if (Directory.Exists(extractPath))
                {
                    var exeFiles = Directory.GetFiles(extractPath, "llama-server.exe", SearchOption.AllDirectories);
                    if (exeFiles.Length > 0)
                    {
                        Log($"Found llama-server.exe at: {exeFiles[0]}");
                        return exeFiles[0];
                    }
                }
                
                Log("llama-server.exe not found in extracted folder.");
            }
            catch (Exception ex)
            {
                Log($"Error searching for llama-server.exe: {ex.Message}");
            }
            return string.Empty;
        }

        /// <summary>
        /// Runs llama-server.exe --version and captures the output.
        /// Returns the version output or an error message.
        /// </summary>
        private string GetLlamaServerVersionOutput(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return "Executable not found";
            }

            try
            {
                Log($"Running: {exePath} --version");
                
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return "Failed to start process";
                }

                // Wait with timeout (5 seconds)
                bool exited = process.WaitForExit(5000);
                if (!exited)
                {
                    process.Kill();
                    return "Timed out";
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                string error = process.StandardError.ReadToEnd().Trim();

                // Some tools output version to stderr
                string result = !string.IsNullOrEmpty(output) ? output : error;
                
                Log($"Version output: {result}");
                return !string.IsNullOrEmpty(result) ? result : "No output";
            }
            catch (Exception ex)
            {
                Log($"Error getting version: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Refreshes the installation details by checking the current state.
        /// Call this to update installation info properties.
        /// </summary>
        public void RefreshInstallationDetails()
        {
            string localFolder = GetAppStoragePath();
            string zipPath = Path.Combine(localFolder, $"{Name}.zip");
            string extractPath = Path.Combine(localFolder, "Engines", Name);

            ZipDownloadPath = zipPath;
            ExtractedFolderPath = extractPath;

            string exePath = FindLlamaServerExe(extractPath);
            LlamaServerExePath = exePath;
            IsLlamaServerExePresent = !string.IsNullOrEmpty(exePath) && File.Exists(exePath);

            if (IsLlamaServerExePresent)
            {
                IsInstalled = true;
                LlamaServerVersion = GetLlamaServerVersionOutput(exePath);
                
                // Set status message if not already set
                if (string.IsNullOrEmpty(StatusMessage) || StatusMessage == "Starting...")
                {
                    StatusMessage = "Ready ✓";
                }
            }
            else
            {
                LlamaServerVersion = string.Empty;
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private string _validationMessage = string.Empty;

        private bool CanRunServer()
        {
            if (!IsInstalled)
            {
                // ValidationMessage = "Engine not installed"; // Optional, normally button Hidden
                return false;
            }
            if (IsServerRunning)
            {
                return false;
            }
            if (string.IsNullOrEmpty(ModelPath))
            {
                ValidationMessage = "⚠ Select a model";
                return false;
            }
            if (!File.Exists(ModelPath))
            {
                ValidationMessage = "⚠ Model not found";
                return false;
            }
            
            ValidationMessage = string.Empty;
            return true;
        }

        [RelayCommand(CanExecute = nameof(CanRunServer))]
        private void RunServer()
        {
            if (IsServerRunning)
            {
                StatusMessage = "Server already running";
                return;
            }

            string localFolder = GetAppStoragePath();
            string extractPath = Path.Combine(localFolder, "Engines", Name);
            
            string exePath = Path.Combine(extractPath, "llama-server.exe");
            if (!File.Exists(exePath))
            {
                try
                {
                    if (Directory.Exists(extractPath))
                    {
                        var exeFiles = Directory.GetFiles(extractPath, "llama-server.exe", SearchOption.AllDirectories);
                        if (exeFiles.Length > 0) exePath = exeFiles[0];
                    }
                }
                catch { }
            }

            if (File.Exists(exePath))
            {
                try
                {
                    Log($"Starting server: {exePath}");
                    Log($"Model: {ModelPath}");

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"--port 8080 -m \"{ModelPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(exePath)
                    };
                    
                    _serverProcess = Process.Start(psi);
                    
                    if (_serverProcess != null)
                    {
                        _serverProcess.EnableRaisingEvents = true;
                        _serverProcess.Exited += OnServerExited;
                        IsServerRunning = true;
                        StatusMessage = "Server running on port 8080";
                        // Since IsServerRunning changed, Command state should update automatically/manually if needed, 
                        // but CanRunServer depends on IsServerRunning, so we might need to notify change.
                        // However, IsServerRunning is ObservableProperty, maybe strict dependency isn't wired up to Command?
                        // We will rely on manual command refresh or PropertyChanged triggers.
                        RunServerCommand.NotifyCanExecuteChanged();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error starting server: {ex}");
                    StatusMessage = "Error: " + ex.Message;
                }
            }
            else
            {
                StatusMessage = "Executable not found!";
            }
        }

        private void OnServerExited(object? sender, EventArgs e)
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                IsServerRunning = false;
                StatusMessage = "Server stopped";
                RunServerCommand.NotifyCanExecuteChanged();
            });
            _serverProcess = null;
        }

        [RelayCommand]
        public void StopServer()
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    IsServerRunning = false;
                    StatusMessage = "Server stopped";
                    RunServerCommand.NotifyCanExecuteChanged();
                }
                catch (Exception ex)
                {
                    StatusMessage = "Error stopping: " + ex.Message;
                }
            }
        }

        public Visibility InverseVisibility(bool isInstalled) => 
            isInstalled ? Visibility.Collapsed : Visibility.Visible;

        // Simplified visibility logic now handled by Command.CanExecute binding usually implies availability, 
        // but for Visibility we might still want to hide/show buttons.
        // run button is always visible if installed, but disabled if no model.
        public Visibility RunButtonVisibility(bool isInstalled, bool isServerRunning) => 
            isInstalled && !isServerRunning ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StopButtonVisibility(bool isServerRunning) => 
            isServerRunning ? Visibility.Visible : Visibility.Collapsed;

        // Helper methods for Installation Details UI
        public string LlamaServerExeStatusIcon(bool isPresent) => 
            isPresent ? "\uE73E" : "\uE711"; // Checkmark or X

        public SolidColorBrush LlamaServerExeStatusColor(bool isPresent) => 
            isPresent ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

        public string LlamaServerExeStatusText(bool isPresent) => 
            isPresent ? "Found" : "Not Found";
    }
}

