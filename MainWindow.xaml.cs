using Microsoft.UI.Xaml;

namespace llama_server_winui
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();

        public MainWindow()
        {
            this.InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // Stop any running servers to prevent orphaned processes
            foreach (var engine in ViewModel.Engines)
            {
                engine.StopServer();
            }
        }
    }
}