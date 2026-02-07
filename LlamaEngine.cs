using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using llama_server_winui.Services;

namespace llama_server_winui
{
    public partial class LlamaEngine : ObservableObject
    {
        public LlamaEngine()
        {
            CurrentVersion = string.Empty;
            LatestVersion = string.Empty;
            StatusMessage = string.Empty;
            ModelPath = string.Empty;
            ExtractedFolderPath = string.Empty;
            LlamaServerExePath = string.Empty;
            LlamaServerVersion = "Unknown";
        }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tag { get; set; } = "Engine";
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotesUrl { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CurrentVersion { get; set; }

        [ObservableProperty]
        public partial string LatestVersion { get; set; }

        public string InstalledVersionDisplay
        {
            get
            {
                var version = string.IsNullOrWhiteSpace(CurrentVersion) ||
                              CurrentVersion.Equals("Not installed", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : CurrentVersion;

                return IsInstalled
                    ? (string.IsNullOrWhiteSpace(version) ? "Installed" : $"Installed {version}")
                    : "Not installed";
            }
        }

        public string AvailableVersionDisplay =>
            string.IsNullOrWhiteSpace(LatestVersion)
                ? "Available --"
                : $"Available {LatestVersion}";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        public partial bool IsInstalled { get; set; }

        [ObservableProperty]
        public partial bool IsUpdateAvailable { get; set; }

        [ObservableProperty]
        public partial bool IsDownloading { get; set; }

        [ObservableProperty]
        public partial double DownloadProgress { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        public partial string ModelPath { get; set; }

        [ObservableProperty]
        public partial string ExtractedFolderPath { get; set; }

        [ObservableProperty]
        public partial string LlamaServerExePath { get; set; }

        [ObservableProperty]
        public partial string LlamaServerVersion { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        public partial bool IsServerRunning { get; set; }

        [ObservableProperty]
        public partial ProcessMetrics? CurrentMetrics { get; set; }

        private ProcessLifecycleManager? _processManager;
        
        // Polling state for download progress
        private DispatcherQueueTimer? _progressTimer;
        private readonly InterlockedProgress _tracker = new();
        private CancellationTokenSource? _downloadCts;

        // Atomic progress tracker for thread-safe download monitoring
        private class InterlockedProgress : IProgress<DownloadProgressInfo>
        {
            private long _bytesDownloaded;
            private long _totalBytes;
            private double _speed;
            
            public long BytesDownloaded => Interlocked.Read(ref _bytesDownloaded);
            public long TotalBytes => Interlocked.Read(ref _totalBytes);
            public double Speed => _speed;
            
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
                Debug.WriteLine(message);
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

                if (IsServerRunning)
                {
                    StatusMessage = "Stopping server...";
                    StopServer();
                    await Task.Delay(2000);
                }

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

                // Start background download task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Log("Background download task started.");
                        var downloader = new FileDownloader();
                        await downloader.DownloadFileAsync(url, zipPath, _tracker, _downloadCts.Token);
                        _tracker.IsComplete = true;
                        Log("Download completed successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Download failed: {ex}");
                        _tracker.ErrorMessage = ex.Message;
                        _tracker.IsFailed = true;
                    }
                });

                // Start UI polling timer
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
                    UpdateDownloadUI(zipPath, extractPath);
                }
                catch (Exception ex)
                {
                    _progressTimer?.Stop();
                    Log($"Timer Tick Error: {ex}");
                }
            };
            _progressTimer.Start();
        }

        private void UpdateDownloadUI(string zipPath, string extractPath)
        {
            // Check for completion/failure
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
                
                Task.Run(async () =>
                {
                    try
                    {
                        Log($"Extracting to {extractPath}...");
                        // Ensure no processes are locking the folder
                        KillLlamaServerProcesses(extractPath);

                        if (Directory.Exists(extractPath))
                        {
                            // Try to rename first (atomic and less prone to locking)
                            string trashPath = extractPath + "_trash_" + DateTime.Now.Ticks;
                            try
                            {
                                Directory.Move(extractPath, trashPath);
                                // Delete trash in background
                                _ = Task.Run(() => RetryDeleteDirectoryAsync(trashPath));
                            }
                            catch
                            {
                                // Fallback to direct delete if move fails
                                await RetryDeleteDirectoryAsync(extractPath);
                            }
                        }
                        ZipFile.ExtractToDirectory(zipPath, extractPath);
                        Log("Extraction complete.");
                        
                        App.MainDispatcher?.TryEnqueue(() =>
                        {
                            CurrentVersion = LatestVersion;
                            IsInstalled = true;
                            StatusMessage = "Ready ✓";
                            IsDownloading = false;
                            DownloadProgress = 0;
                            _ = RefreshInstallationDetailsAsync();
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

        [RelayCommand(CanExecute = nameof(CanRunServer))]
        private async Task RunServer()
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

            if (!File.Exists(exePath))
            {
                StatusMessage = "Executable not found!";
                return;
            }

            try
            {
                Log($"Starting server with ProcessLifecycleManager: {exePath}");
                
                // Dispose old manager if exists
                _processManager?.Dispose();
                
                // Create new process manager
                _processManager = new ProcessLifecycleManager();
                
                // Subscribe to events
                _processManager.MetricsUpdated += OnMetricsUpdated;
                _processManager.StateChanged += OnProcessStateChanged;
                _processManager.OutputReceived += OnOutputReceived;
                
                var args = "--port 8080";
                if (!string.IsNullOrWhiteSpace(ModelPath))
                {
                    var safeModelPath = ModelPath.Replace("\"", "\\\"");
                    args += $" --model \"{safeModelPath}\"";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };
                
                // Start with health check
                StatusMessage = "Starting server...";
                var success = await _processManager.StartAsync(
                    psi, 
                    "http://localhost:8080/health", 
                    healthCheckTimeoutSeconds: 30
                );
                
                if (success)
                {
                    IsServerRunning = true;
                    StatusMessage = "Server running on port 8080";
                    Log("Server started successfully with health check passed");
                }
                else
                {
                    IsServerRunning = false;
                    StatusMessage = "Failed to start server";
                    Log("Server failed to start or health check failed");
                }
            }
            catch (Exception ex)
            {
                Log($"Error starting server: {ex}");
                StatusMessage = "Error: " + ex.Message;
                IsServerRunning = false;
            }
        }

        private void OnMetricsUpdated(object? sender, ProcessMetrics metrics)
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                CurrentMetrics = metrics;
                // Update status with CPU info
                if (metrics.State == ProcessState.Running)
                {
                    StatusMessage = $"Running - CPU: {metrics.CpuPercent:F1}%";
                }
            });
        }

        private void OnProcessStateChanged(object? sender, ProcessState state)
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                Log($"Process state changed to: {state}");
                
                if (state == ProcessState.Stopped || state == ProcessState.Error)
                {
                    IsServerRunning = false;
                    CurrentMetrics = null;
                    StatusMessage = state == ProcessState.Error ? "Server error" : "Server stopped";
                }
                else if (state == ProcessState.Running)
                {
                    IsServerRunning = true;
                }
            });
        }

        private void OnOutputReceived(object? sender, string output)
        {
            // Log output for debugging
            Log($"[Server Output] {output}");
        }

        [RelayCommand]
        public void StopServer()
        {
            if (_processManager != null)
            {
                try
                {
                    Log("Stopping server...");
                    StatusMessage = "Stopping...";
                    _processManager.Stop();
                    _processManager.Dispose();
                    _processManager = null;
                    IsServerRunning = false;
                    CurrentMetrics = null;
                    StatusMessage = "Server stopped";
                    Log("Server stopped successfully");
                }
                catch (Exception ex)
                {
                    Log($"Error stopping server: {ex}");
                    StatusMessage = "Error stopping: " + ex.Message;
                }
            }
        }

        // Helper methods for XAML visibility binding
        public Visibility BoolToVisibility(bool value) => 
            value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InverseVisibility(bool value) => 
            value ? Visibility.Collapsed : Visibility.Visible;

        public Visibility RunButtonVisibility(bool isInstalled, bool isServerRunning) => 
            isInstalled && !isServerRunning ? Visibility.Visible : Visibility.Collapsed;


        public Visibility DownloadButtonVisibility(bool isInstalled, bool isUpdateAvailable) =>
            !isInstalled || isUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

        public string DownloadButtonText =>
            !IsInstalled ? "Install" : (IsUpdateAvailable ? "Update" : "Install");
        
        public string VersionStatusText => IsUpdateAvailable ? "New Version available" : "Latest Version Installed";

        partial void OnCurrentVersionChanged(string value)
        {
            CheckUpdateAvailable();
            OnPropertyChanged(nameof(InstalledVersionDisplay));
        }

        partial void OnLatestVersionChanged(string value)
        {
            CheckUpdateAvailable();
            OnPropertyChanged(nameof(AvailableVersionDisplay));
        }

        partial void OnIsInstalledChanged(bool value)
        {
            CheckUpdateAvailable();
            OnPropertyChanged(nameof(InstalledVersionDisplay));
            OnPropertyChanged(nameof(DownloadButtonText));
        }

        private void CheckUpdateAvailable()
        {
            if (string.IsNullOrEmpty(CurrentVersion) || string.IsNullOrEmpty(LatestVersion))
            {
                IsUpdateAvailable = false;
                return;
            }

            var cur = ExtractBuildNumber(CurrentVersion);
            var lat = ExtractBuildNumber(LatestVersion);
            // Show update if available and we are installed
            IsUpdateAvailable = IsInstalled && cur > 0 && lat > 0 && lat > cur;
            OnPropertyChanged(nameof(DownloadButtonText));
            OnPropertyChanged(nameof(VersionStatusText));
        }

        private int ExtractBuildNumber(string version)
        {
            var match = Regex.Match(version, @"(?:b)?(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int ver))
                return ver;
            return 0;
        }

        public Visibility StopButtonVisibility(bool isServerRunning) => 
            isServerRunning ? Visibility.Visible : Visibility.Collapsed;

        private bool CanRunServer() => IsInstalled && !IsServerRunning && !string.IsNullOrEmpty(ModelPath);

        public async Task RefreshInstallationDetailsAsync()
        {            
            try
            {
                string localFolder = GetAppStoragePath();
                ExtractedFolderPath = Path.Combine(localFolder, "Engines", Name);
                
                string exePath = Path.Combine(ExtractedFolderPath, "llama-server.exe");
                if (!File.Exists(exePath))
                {
                    // Check subdirectories too
                    try
                    {
                        if (Directory.Exists(ExtractedFolderPath))
                        {
                            var exeFiles = Directory.GetFiles(ExtractedFolderPath, "llama-server.exe", SearchOption.AllDirectories);
                            if (exeFiles.Length > 0) exePath = exeFiles[0];
                        }
                    }
                    catch { }
                }

                if (File.Exists(exePath))
                {
                    LlamaServerExePath = exePath;
                    IsInstalled = true;

                    // Check version
                    var ver = await GetLlamaServerVersionAsync(exePath);
                    if (!string.IsNullOrEmpty(ver))
                    {
                        CurrentVersion = "b" + ver;
                        LlamaServerVersion = $"llama-server (b{ver})";
                    }
                    else 
                    {
                        LlamaServerVersion = "llama-server (ready)";
                    }
                }
                else
                {
                    LlamaServerExePath = "Not installed";
                    LlamaServerVersion = "N/A";
                    IsInstalled = false;
                }
            }
            catch { }
        }

        private async Task<string> GetLlamaServerVersionAsync(string path)
        {
            try 
            {
                var psi = new ProcessStartInfo 
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var stdoutTask = p.StandardOutput.ReadToEndAsync();
                    var stderrTask = p.StandardError.ReadToEndAsync();
                    await Task.WhenAll(stdoutTask, stderrTask);
                    await p.WaitForExitAsync();
                    
                    var output = stdoutTask.Result + "\n" + stderrTask.Result;
                    var match = Regex.Match(output, @"version:\s*(\d+)");
                    if (match.Success) return match.Groups[1].Value;
                }
            }
            catch {}
            return string.Empty;
        }

        private async Task RetryDeleteDirectoryAsync(string path, int maxRetries = 10, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    return;
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(delayMs);
                }
            }
        }

        private void KillLlamaServerProcesses(string pathToCheck)
        {
            try
            {
                var processes = Process.GetProcessesByName("llama-server");
                foreach (var p in processes)
                {
                    try 
                    {
                        var processPath = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath) && processPath.Contains(pathToCheck, StringComparison.OrdinalIgnoreCase))
                            p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
