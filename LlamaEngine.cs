using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using llama_server_winui.Services;

namespace llama_server_winui
{
    public partial class LlamaEngine : ObservableObject
    {
        private const int DefaultPort = 8080;
        private const int MaxLogEntries = 2000;
        private const int ReadyCheckTimeoutSeconds = 90;

        public LlamaEngine()
        {
            CurrentVersion = string.Empty;
            LatestVersion = string.Empty;
            StatusMessage = string.Empty;
            ModelPath = string.Empty;
            ExtractedFolderPath = string.Empty;
            LlamaServerExePath = string.Empty;
            LlamaServerVersion = "Unknown";
            InitializePerformanceHistory();
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

        public ObservableCollection<PerformanceSample> CpuHistory { get; } = new();
        public ObservableCollection<PerformanceSample> MemoryHistory { get; } = new();

        private const int PerformanceSampleCount = 48;
        private const double PerformanceChartHeight = 40.0;
        private long _memoryPeakBytes = 1;

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

        public Visibility DownloadButtonVisibility => !IsInstalled || IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

        partial void OnIsUpdateAvailableChanged(bool value)
        {
            OnPropertyChanged(nameof(DownloadButtonVisibility));
            OnPropertyChanged(nameof(VersionStatusText));
        }

        [ObservableProperty]
        public partial bool IsDownloading { get; set; }

        public Visibility IsNotDownloadingVisibility => !IsDownloading ? Visibility.Visible : Visibility.Collapsed;

        partial void OnIsDownloadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotDownloadingVisibility));
        }

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
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        public partial bool IsServerStarting { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunServerCommand))]
        public partial bool IsServerStopping { get; set; }

        public Visibility RunButtonVisibility => IsInstalled && !IsServerRunning && !IsServerStarting && !IsServerStopping ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StopButtonVisibility => IsServerRunning ? Visibility.Visible : Visibility.Collapsed;

        partial void OnIsServerRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(RunButtonVisibility));
            OnPropertyChanged(nameof(StopButtonVisibility));
        }

        partial void OnIsServerStartingChanged(bool value)
        {
            OnPropertyChanged(nameof(RunButtonVisibility));
        }

        partial void OnIsServerStoppingChanged(bool value)
        {
            OnPropertyChanged(nameof(RunButtonVisibility));
        }

        [ObservableProperty]
        public partial ProcessMetrics? CurrentMetrics { get; set; }

        public double CurrentCpuPercent => CurrentMetrics?.CpuPercent ?? 0;
        public long CurrentMemoryBytes => CurrentMetrics?.MemoryBytes ?? 0;
        public TimeSpan CurrentUptime => CurrentMetrics?.Uptime ?? TimeSpan.Zero;
        public int CurrentPort => CurrentMetrics?.Port ?? DefaultPort;

        public ObservableCollection<EngineLogEntry> LogEntries { get; } = new();

        private ProcessLifecycleManager? _processManager;
        private CancellationTokenSource? _startupCts;
        
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
            if (IsServerRunning || IsServerStarting || IsServerStopping)
            {
                StatusMessage = IsServerStopping ? "Server is stopping" : "Server already running";
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
                AppendLogLine("Starting server...", false);

                IsServerStarting = true;
                IsServerStopping = false;
                StatusMessage = "Starting server...";
                _startupCts?.Cancel();
                _startupCts = new CancellationTokenSource();
                
                // Dispose old manager if exists
                _processManager?.Dispose();
                
                // Create new process manager
                _processManager = new ProcessLifecycleManager();
                
                // Subscribe to events
                _processManager.MetricsUpdated += OnMetricsUpdated;
                _processManager.StateChanged += OnProcessStateChanged;
                _processManager.OutputReceived += OnOutputReceived;
                
                var args = $"--port {DefaultPort}";
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
                
                // Start process
                var success = await _processManager.StartAsync(psi);
                
                if (success)
                {
                    IsServerRunning = true;
                    StatusMessage = "Loading model...";
                    Log("Server started successfully");
                    AppendLogLine("Server process started, waiting for model...", false);

                    var ready = await WaitForServerReadyAsync(DefaultPort, _startupCts.Token);
                    if (ready)
                    {
                        StatusMessage = $"Server running on port {DefaultPort}";
                        AppendLogLine("Server ready.", false);
                    }
                    else if (!_startupCts.IsCancellationRequested)
                    {
                        StatusMessage = "Server started, waiting for model...";
                        AppendLogLine("Server started, still waiting for model.", false);
                    }
                }
                else
                {
                    IsServerRunning = false;
                    IsServerStarting = false;
                    StatusMessage = "Failed to start server";
                    AppendLogLine("Failed to start server.", true);
                    Log("Server failed to start");
                }
            }
            catch (Exception ex)
            {
                Log($"Error starting server: {ex}");
                StatusMessage = "Error: " + ex.Message;
                IsServerRunning = false;
                IsServerStarting = false;
                AppendLogLine($"Error starting server: {ex.Message}", true);
            }
            finally
            {
                if (!IsServerStopping)
                {
                    IsServerStarting = false;
                }
            }
        }

        private void OnMetricsUpdated(object? sender, ProcessMetrics metrics)
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                CurrentMetrics = metrics;
                AddPerformanceSample(metrics);
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
                    IsServerStarting = false;
                    IsServerStopping = false;
                    ResetPerformanceHistory();
                }
                else if (state == ProcessState.Running)
                {
                    IsServerRunning = true;
                    IsServerStopping = false;
                    ResetPerformanceHistory();
                }
            });
        }

        private void OnOutputReceived(object? sender, string output)
        {
            // Log output for debugging
            Log($"[Server Output] {output}");
            AppendLogLine(output, output.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase));
        }

        private void AddPerformanceSample(ProcessMetrics metrics)
        {
            var cpuSample = ScaleCpu(metrics.CpuPercent);
            var memorySample = ScaleMemory(metrics.MemoryBytes);

            AddHistorySample(CpuHistory, cpuSample);
            AddHistorySample(MemoryHistory, memorySample);
        }

        private void AddHistorySample(ObservableCollection<PerformanceSample> target, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0;
            }

            if (target.Count >= PerformanceSampleCount)
            {
                target.RemoveAt(0);
            }
            target.Add(new PerformanceSample(value));
        }

        private double ScaleCpu(double cpuPercent)
        {
            var normalized = Math.Clamp(cpuPercent / 100.0, 0, 1);
            return normalized * PerformanceChartHeight;
        }

        private double ScaleMemory(long memoryBytes)
        {
            if (memoryBytes <= 0)
            {
                return 0;
            }

            if (memoryBytes > _memoryPeakBytes)
            {
                _memoryPeakBytes = memoryBytes;
            }

            var normalized = Math.Clamp((double)memoryBytes / _memoryPeakBytes, 0, 1);
            return normalized * PerformanceChartHeight;
        }

        private void InitializePerformanceHistory()
        {
            CpuHistory.Clear();
            MemoryHistory.Clear();

            for (int i = 0; i < PerformanceSampleCount; i++)
            {
                CpuHistory.Add(new PerformanceSample(0));
                MemoryHistory.Add(new PerformanceSample(0));
            }
        }

        private void ResetPerformanceHistory()
        {
            _memoryPeakBytes = 1;
            InitializePerformanceHistory();
        }

        private void AppendLogLine(string message, bool isError)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            App.MainDispatcher?.TryEnqueue(() =>
            {
                if (LogEntries.Count >= MaxLogEntries)
                {
                    LogEntries.RemoveAt(0);
                }
                LogEntries.Add(new EngineLogEntry(DateTime.Now, message, isError));
            });
        }

        private async Task<bool> WaitForServerReadyAsync(int port, CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddSeconds(ReadyCheckTimeoutSeconds);

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync("127.0.0.1", port, token);
                    if (client.Connected)
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch
                {
                    // Ignore until timeout
                }

                try
                {
                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        [RelayCommand]
        public void StopServer()
        {
            if (_processManager != null)
            {
                Log("Stopping server...");
                AppendLogLine("Stopping server...", false);
                StatusMessage = "Stopping...";
                IsServerStopping = true;
                IsServerStarting = false;
                _startupCts?.Cancel();

                var manager = _processManager;
                _processManager = null;

                _ = Task.Run(() =>
                {
                    try
                    {
                        manager.Stop();
                        manager.Dispose();
                        Log("Server stopped successfully");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping server: {ex}");
                        AppendLogLine($"Error stopping server: {ex.Message}", true);
                    }
                    finally
                    {
                        App.MainDispatcher?.TryEnqueue(() =>
                        {
                            IsServerRunning = false;
                            CurrentMetrics = null;
                            StatusMessage = "Server stopped";
                            IsServerStopping = false;
                        });
                    }
                });
            }
        }

        
        public string VersionStatusText => IsUpdateAvailable ? "New Version available" : "Latest Version Installed";

        public string DownloadButtonText
        {
            get
            {
                // Return the appropriate text based on your logic
                // For example:
                if (IsDownloading)
                    return "Downloading...";
                if (!IsInstalled)
                    return "Download";
                return "Reinstall";
            }
        }

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
            OnPropertyChanged(nameof(RunButtonVisibility));
            OnPropertyChanged(nameof(DownloadButtonVisibility));
        }

        partial void OnCurrentMetricsChanged(ProcessMetrics? value)
        {
            OnPropertyChanged(nameof(CurrentCpuPercent));
            OnPropertyChanged(nameof(CurrentMemoryBytes));
            OnPropertyChanged(nameof(CurrentUptime));
            OnPropertyChanged(nameof(CurrentPort));
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

        private bool CanRunServer() =>
            IsInstalled &&
            !IsServerRunning &&
            !IsServerStarting &&
            !IsServerStopping &&
            !string.IsNullOrEmpty(ModelPath);

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

    public sealed class EngineLogEntry
    {
        public EngineLogEntry(DateTime timestamp, string message, bool isError)
        {
            Timestamp = timestamp;
            Message = message;
            IsError = isError;
        }

        public DateTime Timestamp { get; }
        public string Message { get; }
        public bool IsError { get; }

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
    }

    public sealed class PerformanceSample
    {
        public PerformanceSample(double value)
        {
            Value = value;
        }

        public double Value { get; set; }
    }
}
