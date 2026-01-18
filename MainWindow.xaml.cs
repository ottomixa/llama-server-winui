using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using llama_server_winui.Services;

namespace llama_server_winui
{
    /// <summary>
    /// Main window implementing INotifyPropertyChanged for active engine tracking
    /// Handles window lifecycle and synchronization with tray icon
    /// </summary>
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Bind to shared engines from App
        public ObservableCollection<LlamaEngine> Engines => App.Engines;

        private LlamaEngine? _activeEngine;
        
        /// <summary>
        /// The currently running engine (shown in right panel)
        /// </summary>
        public LlamaEngine? ActiveEngine
        {
            get => _activeEngine;
            private set
            {
                if (_activeEngine != value)
                {
                    _activeEngine = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasActiveEngine));
                    OnPropertyChanged(nameof(NoActiveEngine));
                    UpdatePanelVisibility();
                }
            }
        }

        public bool HasActiveEngine => ActiveEngine != null;
        public bool NoActiveEngine => ActiveEngine == null;

        /// <summary>
        /// Manually update panel visibility to avoid x:Bind issues with the Window class
        /// </summary>
        private void UpdatePanelVisibility()
        {
            if (LoadingOverlay != null) LoadingOverlay.Visibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
            if (ActiveServerPanel != null) ActiveServerPanel.Visibility = HasActiveEngine ? Visibility.Visible : Visibility.Collapsed;
            if (EmptyStatePanel != null) EmptyStatePanel.Visibility = HasActiveEngine ? Visibility.Collapsed : Visibility.Visible;
        }

        private readonly GitHubService _gitHubService = new();
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    UpdatePanelVisibility();
                }
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            
            // Handle window closing event - hide instead of close (unless exiting)
            this.AppWindow.Closing += OnWindowClosing;

            // Subscribe to engine state changes
            foreach (var engine in Engines)
            {
                engine.PropertyChanged += Engine_PropertyChanged;
            }

            // Set initial active engine if any is running
            UpdateActiveEngine();

            // Fire and forget version check
            _ = InitializeVersionsAsync();

            // Initial update for visibility
            UpdatePanelVisibility();
        }

        private async Task InitializeVersionsAsync()
        {
            try
            {
                IsLoading = true;
                await Task.Delay(800);

                var release = await _gitHubService.GetLatestReleaseAsync();
                if (release != null)
                {
                    // Update Vulkan
                    var vulkanEngine = Engines.FirstOrDefault(e => e.Name.Contains("Vulkan"));
                    var assetVulkan = release.Assets.FirstOrDefault(a => a.Name.Contains("bin-win-vulkan-x64.zip"));
                    if (vulkanEngine != null && assetVulkan != null)
                    {
                        vulkanEngine.LatestVersion = release.TagName;
                        vulkanEngine.Tag = release.TagName;
                        vulkanEngine.DownloadUrl = assetVulkan.BrowserDownloadUrl;
                        vulkanEngine.ReleaseNotesUrl = release.HtmlUrl;
                    }

                    // Update CPU
                    var cpuEngine = Engines.FirstOrDefault(e => e.Name.Contains("CPU"));
                    var assetAVX = release.Assets.FirstOrDefault(a => a.Name.Contains("bin-win-avx2-x64.zip"));
                    if (cpuEngine != null && assetAVX != null)
                    {
                        cpuEngine.LatestVersion = release.TagName;
                        cpuEngine.Tag = release.TagName;
                        cpuEngine.DownloadUrl = assetAVX.BrowserDownloadUrl;
                        cpuEngine.ReleaseNotesUrl = release.HtmlUrl;
                    }
                }
            }
            catch (Exception) { /* Keep defaults */ }
            finally { IsLoading = false; }
        }

        /// <summary>
        /// Handles property changes from engines to track which one is active
        /// </summary>
        private void Engine_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LlamaEngine.IsServerRunning))
            {
                // Update active engine when any engine's running state changes
                App.MainDispatcher?.TryEnqueue(() =>
                {
                    UpdateActiveEngine();
                });
            }
        }

        /// <summary>
        /// Updates the active engine based on which engine is currently running
        /// </summary>
        private void UpdateActiveEngine()
        {
            // Find the first running engine
            var runningEngine = Engines.FirstOrDefault(e => e.IsServerRunning);
            ActiveEngine = runningEngine;
        }

        /// <summary>
        /// Handles window closing - hide instead of close unless app is exiting
        /// </summary>
        private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, 
                                      Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            // Only hide if not exiting the entire application
            if (!((App)Application.Current).IsExiting)
            {
                args.Cancel = true;
                this.AppWindow.Hide();
            }
            else
            {
                // App is exiting - stop all running servers
                foreach (var engine in Engines)
                {
                    if (engine.IsServerRunning)
                    {
                        try
                        {
                            engine.StopServer();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error stopping engine during shutdown: {ex.Message}");
                        }
                    }
                }
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Helper method for x:Bind to convert bool to Visibility
        /// </summary>
        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // Navigation button handlers (for future implementation)
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to home view (future feature)
            UpdateSidebarSelection(sender as Button);
        }

        private void EnginesButton_Click(object sender, RoutedEventArgs e)
        {
            // Already on engines view
            UpdateSidebarSelection(sender as Button);
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to logs view (future feature)
            UpdateSidebarSelection(sender as Button);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings view (future feature)
            UpdateSidebarSelection(sender as Button);
        }

        /// <summary>
        /// Updates sidebar button selection visual state
        /// </summary>
        private void UpdateSidebarSelection(Button? selectedButton)
        {
            if (selectedButton == null) return;

            try
            {
                // Reset all buttons to default style
                var buttons = new Button[] { HomeButton, EnginesButton, LogsButton, SettingsButton };

                foreach (var button in buttons)
                {
                    if (button != null)
                    {
                        button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(0, 0, 0, 0)); // Transparent
                        button.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 0, 0, 0)); // Black
                    }
                }

                // Highlight selected button
                selectedButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0, 120, 212)); // #0078D4
                selectedButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 255, 255, 255)); // White
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating sidebar selection: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Refresh engines list (future: check for updates)
            foreach (var engine in Engines)
            {
                // Could trigger version check here
            }
        }

        private void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            // Handle release notes toggle (future: expand/collapse inline)
            if (sender is Button button && button.DataContext is LlamaEngine engine)
            {
                // Navigate to release notes URL
                try
                {
                    var uri = new Uri(engine.ReleaseNotesUrl);
                    _ = Windows.System.Launcher.LaunchUriAsync(uri);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening release notes: {ex.Message}");
                }
            }
        }

        private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveEngine != null)
            {
                // Navigate to logs view with this engine's logs
                LogsButton_Click(LogsButton, new RoutedEventArgs());
            }
        }

        private async void PickModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LlamaEngine engine)
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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

        private void StopActiveServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveEngine != null)
            {
                ActiveEngine.StopServer();
            }
        }

        private void RestartServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveEngine != null && ActiveEngine.IsServerRunning)
            {
                // Stop and restart
                ActiveEngine.StopServer();
                
                // Wait a moment then restart
                var timer = new System.Threading.Timer(async _ =>
                {
                    App.MainDispatcher?.TryEnqueue(async () =>
                    {
                        await ActiveEngine.RunServerCommand.ExecuteAsync(null);
                    });
                }, null, TimeSpan.FromSeconds(2), System.Threading.Timeout.InfiniteTimeSpan);
            }
        }
    }
}
