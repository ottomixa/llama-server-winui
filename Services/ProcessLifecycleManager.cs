using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace llama_server_winui.Services
{
    /// <summary>
    /// Represents the current state of a managed process
    /// </summary>
    public enum ProcessState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    /// <summary>
    /// Performance metrics for a running process
    /// </summary>
    public record ProcessMetrics(
        double CpuPercent,
        long MemoryBytes,
        TimeSpan Uptime,
        ProcessState State,
        int Port,
        long TotalRequests = 0
    );

    /// <summary>
    /// Manages the complete lifecycle of a server process with monitoring
    /// Handles: start, stop, health checks, performance tracking, and output capture
    /// </summary>
    public class ProcessLifecycleManager : IDisposable
    {
        private Process? _process;
        private Timer? _monitorTimer;
        private readonly StringBuilder _outputBuffer = new();
        private readonly object _outputLock = new();
        private DateTime _startTime;
        private ProcessState _currentState = ProcessState.Stopped;
        private bool _disposed = false;
        private CancellationTokenSource? _startCts;
        
        // Performance tracking
        private PerformanceCounter? _cpuCounter;
        private long _lastCpuTime;
        private DateTime _lastCpuCheck;

        public event EventHandler<ProcessMetrics>? MetricsUpdated;
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<ProcessState>? StateChanged;

        public ProcessState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    StateChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// Starts a process and monitors it for health and performance
        /// </summary>
        /// <param name="startInfo">Process start configuration</param>
        /// <param name="healthCheckUrl">Optional URL to check for successful startup (e.g., http://localhost:8080)</param>
        /// <param name="healthCheckTimeoutSeconds">How long to wait for health check to pass</param>
        /// <returns>True if process started and passed health check, false otherwise</returns>
        public async Task<bool> StartAsync(
            ProcessStartInfo startInfo, 
            string? healthCheckUrl = null,
            int healthCheckTimeoutSeconds = 30)
        {
            if (_process != null && !_process.HasExited)
            {
                return false; // Already running
            }

            try
            {
                CurrentState = ProcessState.Starting;
                _startCts = new CancellationTokenSource();

                // Configure process for output capture
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                // Wire up output handlers
                _process.OutputDataReceived += OnOutputDataReceived;
                _process.ErrorDataReceived += OnErrorDataReceived;
                _process.Exited += OnProcessExited;

                // Start the process
                if (!_process.Start())
                {
                    CurrentState = ProcessState.Error;
                    return false;
                }

                _startTime = DateTime.UtcNow;

                // Begin async output reading
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Initialize performance counters
                InitializePerformanceCounters();

                // Perform health check if URL provided
                if (!string.IsNullOrEmpty(healthCheckUrl))
                {
                    var healthCheckPassed = await PerformHealthCheck(
                        healthCheckUrl, 
                        healthCheckTimeoutSeconds,
                        _startCts.Token);

                    if (!healthCheckPassed)
                    {
                        // Health check failed, kill the process
                        Stop();
                        CurrentState = ProcessState.Error;
                        return false;
                    }
                }

                // Process started successfully
                CurrentState = ProcessState.Running;

                // Start monitoring timer (1 second interval)
                _monitorTimer = new Timer(
                    MonitorCallback, 
                    null, 
                    TimeSpan.FromSeconds(1), 
                    TimeSpan.FromSeconds(1));

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting process: {ex.Message}");
                CurrentState = ProcessState.Error;
                return false;
            }
        }

        /// <summary>
        /// Performs HTTP health check to verify server is responding
        /// </summary>
        private async Task<bool> PerformHealthCheck(string url, int timeoutSeconds, CancellationToken cancellationToken)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var response = await client.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"Health check passed: {url}");
                        return true;
                    }
                }
                catch
                {
                    // Expected during startup - server not ready yet
                }

                await Task.Delay(500, cancellationToken);
            }

            Debug.WriteLine($"Health check failed: {url}");
            return false;
        }

        /// <summary>
        /// Stops the managed process gracefully with timeout and force kill fallback
        /// </summary>
        public void Stop()
        {
            if (_process == null || _process.HasExited)
            {
                CurrentState = ProcessState.Stopped;
                return;
            }

            try
            {
                CurrentState = ProcessState.Stopping;
                _startCts?.Cancel();
                _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                // Try graceful shutdown first (CloseMainWindow for GUI apps, or just wait)
                try
                {
                    _process.CloseMainWindow();
                }
                catch { }

                // Wait up to 10 seconds for graceful exit
                if (!_process.WaitForExit(10000))
                {
                    // Force kill if still running
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error force killing process: {ex.Message}");
                    }
                }

                CurrentState = ProcessState.Stopped;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping process: {ex.Message}");
                CurrentState = ProcessState.Error;
            }
            finally
            {
                CleanupProcess();
            }
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _lastCpuTime = _process.TotalProcessorTime.Ticks;
                    _lastCpuCheck = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing performance counters: {ex.Message}");
            }
        }

        private void MonitorCallback(object? state)
        {
            try
            {
                if (_process == null || _process.HasExited)
                {
                    CurrentState = ProcessState.Stopped;
                    _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                // Calculate CPU usage
                double cpuPercent = CalculateCpuUsage();

                // Get memory usage
                long memoryBytes = 0;
                try
                {
                    _process.Refresh();
                    memoryBytes = _process.WorkingSet64;
                }
                catch { }

                // Calculate uptime
                var uptime = DateTime.UtcNow - _startTime;

                // Extract port from process arguments (assuming --port argument exists)
                int port = ExtractPortFromArguments();

                // Emit metrics event
                var metrics = new ProcessMetrics(
                    cpuPercent,
                    memoryBytes,
                    uptime,
                    CurrentState,
                    port
                );

                MetricsUpdated?.Invoke(this, metrics);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in monitor callback: {ex.Message}");
            }
        }

        private double CalculateCpuUsage()
        {
            try
            {
                if (_process == null || _process.HasExited)
                    return 0;

                var now = DateTime.UtcNow;
                var currentCpuTime = _process.TotalProcessorTime.Ticks;
                
                var cpuTimeDelta = currentCpuTime - _lastCpuTime;
                var timeDelta = (now - _lastCpuCheck).TotalMilliseconds;

                if (timeDelta > 0)
                {
                    // Calculate CPU percentage
                    var cpuPercent = (cpuTimeDelta * 100.0) / (timeDelta * 10000.0 * Environment.ProcessorCount);
                    
                    _lastCpuTime = currentCpuTime;
                    _lastCpuCheck = now;
                    
                    return Math.Min(100, Math.Max(0, cpuPercent));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating CPU usage: {ex.Message}");
            }
            
            return 0;
        }

        private int ExtractPortFromArguments()
        {
            try
            {
                if (_process?.StartInfo.Arguments == null)
                    return 8080; // Default

                var args = _process.StartInfo.Arguments;
                var portIndex = args.IndexOf("--port");
                
                if (portIndex >= 0)
                {
                    var afterPort = args.Substring(portIndex + 6).Trim();
                    var spaceIndex = afterPort.IndexOf(' ');
                    var portString = spaceIndex >= 0 
                        ? afterPort.Substring(0, spaceIndex) 
                        : afterPort;
                    
                    if (int.TryParse(portString, out int port))
                        return port;
                }
            }
            catch { }
            
            return 8080; // Default fallback
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            lock (_outputLock)
            {
                _outputBuffer.AppendLine(e.Data);
                
                // Keep buffer size manageable (last 1000 lines)
                var lines = _outputBuffer.ToString().Split('\n');
                if (lines.Length > 1000)
                {
                    _outputBuffer.Clear();
                    _outputBuffer.AppendLine(string.Join("\n", lines[^1000..]));
                }
            }

            OutputReceived?.Invoke(this, e.Data);
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            lock (_outputLock)
            {
                _outputBuffer.AppendLine($"[ERROR] {e.Data}");
            }

            OutputReceived?.Invoke(this, $"[ERROR] {e.Data}");
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            Debug.WriteLine("Process exited");
            CurrentState = ProcessState.Stopped;
            _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Gets the captured output buffer (last 1000 lines)
        /// </summary>
        public string GetOutputBuffer()
        {
            lock (_outputLock)
            {
                return _outputBuffer.ToString();
            }
        }

        /// <summary>
        /// Gets the last N lines of output
        /// </summary>
        public string GetRecentOutput(int lineCount = 10)
        {
            lock (_outputLock)
            {
                var lines = _outputBuffer.ToString().Split('\n');
                var recentLines = lines.Length > lineCount 
                    ? lines[^lineCount..] 
                    : lines;
                return string.Join("\n", recentLines);
            }
        }

        private void CleanupProcess()
        {
            try
            {
                if (_process != null)
                {
                    _process.OutputDataReceived -= OnOutputDataReceived;
                    _process.ErrorDataReceived -= OnErrorDataReceived;
                    _process.Exited -= OnProcessExited;
                    _process.Dispose();
                    _process = null;
                }

                _cpuCounter?.Dispose();
                _cpuCounter = null;

                _monitorTimer?.Dispose();
                _monitorTimer = null;

                _startCts?.Dispose();
                _startCts = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Stop();
            CleanupProcess();

            GC.SuppressFinalize(this);
        }
    }
}
