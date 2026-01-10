using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace llama_server_winui
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Static reference to the main window's DispatcherQueue for UI thread access
        /// </summary>
        public static DispatcherQueue? MainDispatcher { get; private set; }
        
        /// <summary>
        /// Static reference to the main window
        /// </summary>
        public static MainWindow? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainDispatcher = MainWindow.DispatcherQueue;
            MainWindow.Activate();
        }
    }
}

