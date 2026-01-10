using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

namespace llama_server_winui
{
    public sealed partial class MainWindow : Window
    {
        public ObservableCollection<LlamaEngine> Engines { get; set; } = new();

        public MainWindow()
        {
            this.InitializeComponent();
            this.Closed += MainWindow_Closed;

            // Engine definitions - Replace with GitHub API calls in Phase 2
            Engines.Add(new LlamaEngine
            {
                Name = "Vulkan llama.cpp (Windows)",
                Description = "Vulkan accelerated llama.cpp engine - Works on AMD, Intel, and NVIDIA GPUs",
                CurrentVersion = "Not installed",
                LatestVersion = "b4969",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b4969/llama-b4969-bin-win-vulkan-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b4969"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CUDA llama.cpp (Windows)",
                Description = "NVIDIA CUDA accelerated llama.cpp engine - Best performance on NVIDIA GPUs",
                Tag = "b3376",
                CurrentVersion = "Not installed",
                LatestVersion = "b3376",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3376/llama-b3376-bin-win-cuda-cu12.2.0-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b3376"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CPU llama.cpp (Windows)",
                Description = "CPU-only llama.cpp engine using AVX2 - Works on any modern CPU",
                CurrentVersion = "Not installed",
                LatestVersion = "b4969",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b4969/llama-b4969-bin-win-avx2-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b4969"
            });
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
    }
}



