using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using llama_server_winui.Services;
using System.Linq;
using System.ComponentModel;

namespace llama_server_winui
{
    // Removed [INotifyPropertyChanged] because MainWindow inherits from Window (can't change base).
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Manually implement backing field + property to avoid source-gen AOT issues
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ObservableCollection<LlamaEngine> Engines { get; set; } = new();
        private readonly GitHubService _gitHubService = new();

        public MainWindow()
        {
            this.InitializeComponent();
            this.Closed += MainWindow_Closed;

            InitializeEngines();

            // Fire and forget version check
            _ = InitializeVersionsAsync();
        }

        private void InitializeEngines()
        {
            // Initial State (Fallbacks)
            Engines.Add(new LlamaEngine
            {
                Name = "Vulkan llama.cpp (Windows)",
                Description = "Vulkan accelerated llama.cpp engine - Works on AMD, Intel, and NVIDIA GPUs",
                Tag = "vulkan-fallback", // Temporary
                LatestVersion = "Checking...",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CUDA llama.cpp (Windows)",
                Description = "NVIDIA CUDA accelerated llama.cpp engine - Best performance on NVIDIA GPUs",
                Tag = "b3376", // Pinned for safety
                LatestVersion = "b3376 (Pinned)",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b3376",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3376/llama-b3376-bin-win-cuda-cu12.2.0-x64.zip"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CPU llama.cpp (Windows)",
                Description = "CPU-only llama.cpp engine using AVX2 - Works on any modern CPU",
                Tag = "avx-fallback",
                LatestVersion = "Checking...",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases"
            });
        }

        private async Task InitializeVersionsAsync()
        {
            try
            {
                IsLoading = true;

                // Add start delay to show the loader for at least a moment (UX)
                await Task.Delay(1000);

                var release = await _gitHubService.GetLatestReleaseAsync();

                if (release != null)
                {
                    // Update Vulkan
                    var vulkanEngine = Engines.FirstOrDefault(e => e.Name.Contains("Vulkan"));
                    if (vulkanEngine != null)
                    {
                        var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("bin-win-vulkan-x64.zip"));
                        if (asset != null)
                        {
                            vulkanEngine.LatestVersion = release.TagName;
                            vulkanEngine.Tag = release.TagName;
                            vulkanEngine.DownloadUrl = asset.BrowserDownloadUrl;
                            vulkanEngine.ReleaseNotesUrl = release.HtmlUrl;
                        }
                    }

                    // Update CPU
                    var cpuEngine = Engines.FirstOrDefault(e => e.Name.Contains("CPU"));
                    if (cpuEngine != null)
                    {
                        var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("bin-win-avx2-x64.zip"));
                        if (asset != null)
                        {
                            cpuEngine.LatestVersion = release.TagName;
                            cpuEngine.Tag = release.TagName;
                            cpuEngine.DownloadUrl = asset.BrowserDownloadUrl;
                            cpuEngine.ReleaseNotesUrl = release.HtmlUrl;
                        }
                    }

                    // Optional: Check CUDA if they fix the assets in future
                    var cudaEngine = Engines.FirstOrDefault(e => e.Name.Contains("CUDA"));
                    if (cudaEngine != null)
                    {
                        // Loose check for any cuda 12
                        var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("cuda-cu12") && a.Name.Contains("x64.zip"));
                        if (asset != null)
                        {
                            // If found, we can upgrade!
                            cudaEngine.LatestVersion = release.TagName;
                            cudaEngine.Tag = release.TagName;
                            cudaEngine.DownloadUrl = asset.BrowserDownloadUrl;
                            cudaEngine.ReleaseNotesUrl = release.HtmlUrl;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore network errors, keep fallbacks
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            foreach (var engine in Engines)
            {
                if (engine.IsServerRunning)
                {
                    engine.StopServer();
                }
            }
        }

        private async void PickModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.Button btn && btn.DataContext is LlamaEngine engine)
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();

                // Retrieve the window handle (HWND) of the current WinUI 3 window
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                // Initialize the file picker with the window handle
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".gguf");
                picker.FileTypeFilter.Add(".bin");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    engine.ModelPath = file.Path;
                }
            }
        }

        public Visibility BoolToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }
}



