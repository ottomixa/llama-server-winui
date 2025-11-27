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

            // Mock Data - Replace this with GitHub API calls
            Engines.Add(new LlamaEngine
            {
                Name = "Vulkan llama.cpp (Windows)",
                Description = "Vulkan accelerated llama.cpp engine",
                CurrentVersion = "v1.56.0",
                LatestVersion = "v1.58.0",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3565/llama-b3565-bin-win-vulkan-x64.zip" // Example URL
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CUDA llama.cpp (Windows)",
                Description = "Nvidia CUDA accelerated llama.cpp engine",
                CurrentVersion = "v1.56.0",
                LatestVersion = "v1.58.0",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3565/llama-b3565-bin-win-cuda-cu12.2.0-x64.zip" // Example URL
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CPU llama.cpp (Windows)",
                Description = "CPU-only llama.cpp engine",
                CurrentVersion = "v1.56.0",
                LatestVersion = "v1.58.0",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3565/llama-b3565-bin-win-avx2-x64.zip" // Example URL
            });
        }
    }
}