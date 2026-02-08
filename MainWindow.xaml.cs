using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using llama_server_winui.Services;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace llama_server_winui
{
    /// <summary>
    /// Main window implementing INotifyPropertyChanged for active engine tracking
    /// Handles window lifecycle and synchronization with tray icon
    /// </summary>
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Allow generated code to pass the Window instance where a FrameworkElement is expected
        // by implicitly converting MainWindow to its root FrameworkElement (RootGrid).
        // Guarantee a non-null FrameworkElement is returned to satisfy nullable analysis.
        public static implicit operator Microsoft.UI.Xaml.FrameworkElement(MainWindow? window)
        {
            if (window is null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            // The implicit conversion may be called by generated XAML binding code
            // before `InitializeComponent()` has populated named elements like `RootGrid`.
            // Return a harmless fallback FrameworkElement instead of throwing to
            // avoid crashing during XAML wiring. This preserves behavior while the
            // real visual tree is initialized.
            var root = window.RootGrid;
            if (root is null)
            {
                try
                {
                    var _log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "llama_startup.log");
                    System.IO.File.AppendAllText(_log, $"[MainWindow] RootGrid null in implicit operator at {DateTime.Now}\r\n");
                }
                catch { }
                return new Microsoft.UI.Xaml.Controls.Grid();
            }

            return root;
        }

        // Bind to shared engines from App
        public ObservableCollection<LlamaEngine> Engines => App.Engines;

        private LlamaEngine? _activeEngine;
        private LlamaEngine? _selectedLogEngine;
        private readonly ObservableCollection<EngineLogEntry> _emptyLogEntries = new();
        private bool _isLogsView;
        
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

        public LlamaEngine? SelectedLogEngine
        {
            get => _selectedLogEngine;
            set
            {
                if (_selectedLogEngine != value)
                {
                    UnsubscribeLogEvents(_selectedLogEngine);
                    _selectedLogEngine = value;
                    SubscribeLogEvents(_selectedLogEngine);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedLogEntries));
                }
            }
        }

        public ObservableCollection<EngineLogEntry> SelectedLogEntries =>
            SelectedLogEngine?.LogEntries ?? _emptyLogEntries;

        public bool IsLogsView
        {
            get => _isLogsView;
            private set
            {
                if (_isLogsView != value)
                {
                    _isLogsView = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEnginesView));
                }
            }
        }

        public bool IsEnginesView => !IsLogsView;

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
            var _log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "llama_startup.log");
            try
            {
                System.IO.File.AppendAllText(_log, $"[MainWindow] Before InitializeComponent: {DateTime.Now}\r\n");
                this.InitializeComponent();
                System.IO.File.AppendAllText(_log, $"[MainWindow] After InitializeComponent: {DateTime.Now}\r\n");
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(_log, $"[MainWindow] InitializeComponent Exception: {ex}\r\n"); } catch {}
                throw;
            }
            if (RootGrid != null)
            {
                RootGrid.DataContext = this;
            }
            
            // Handle window closing event - hide instead of close (unless exiting)
            this.AppWindow.Closing += OnWindowClosing;

            // Subscribe to engine state changes
            foreach (var engine in Engines)
            {
                engine.PropertyChanged += Engine_PropertyChanged;
            }

            // Set initial active engine if any is running
            UpdateActiveEngine();
            SelectedLogEngine = ActiveEngine ?? Engines.FirstOrDefault();
            IsLogsView = false;

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

                    // Update CUDA
                    var cudaEngine = Engines.FirstOrDefault(e => e.Name.Contains("CUDA"));
                    
                    // Filter out cudart libraries (start with cudart) and ensure x64
                    var validAssets = release.Assets.Where(a => !a.Name.StartsWith("cudart") && a.Name.Contains("x64.zip"));

                    // Prefer CUDA 12, fallback to any valid CUDA
                    var assetCuda = validAssets.FirstOrDefault(a => a.Name.Contains("bin-win-cuda-cu12"));
                    if (assetCuda == null) 
                    {
                         assetCuda = validAssets.FirstOrDefault(a => a.Name.Contains("bin-win-cuda"));
                    }

                    if (cudaEngine != null && assetCuda != null)
                    {
                        cudaEngine.LatestVersion = release.TagName;
                        cudaEngine.Tag = release.TagName;
                        cudaEngine.DownloadUrl = assetCuda.BrowserDownloadUrl;
                        cudaEngine.ReleaseNotesUrl = release.HtmlUrl;
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
            if (SelectedLogEngine == null && runningEngine != null)
            {
                SelectedLogEngine = runningEngine;
            }
        }

        private void SubscribeLogEvents(LlamaEngine? engine)
        {
            if (engine == null)
            {
                return;
            }

            engine.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
        }

        private void UnsubscribeLogEvents(LlamaEngine? engine)
        {
            if (engine == null)
            {
                return;
            }

            engine.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
        }

        private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogsListView == null || LogsListView.Items.Count == 0)
            {
                return;
            }

            var lastIndex = LogsListView.Items.Count - 1;
            if (lastIndex >= 0)
            {
                LogsListView.ScrollIntoView(LogsListView.Items[lastIndex]);
            }
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
            IsLogsView = false;
            UpdateSidebarSelection(sender as Button);
        }

        private void EnginesButton_Click(object sender, RoutedEventArgs e)
        {
            // Already on engines view
            IsLogsView = false;
            UpdateSidebarSelection(sender as Button);
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to logs view (future feature)
            IsLogsView = true;
            if (SelectedLogEngine == null)
            {
                SelectedLogEngine = ActiveEngine ?? Engines.FirstOrDefault();
            }
            UpdateSidebarSelection(sender as Button);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to settings view (future feature)
            IsLogsView = false;
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
                SelectedLogEngine = ActiveEngine;
                LogsButton_Click(LogsButton, new RoutedEventArgs());
            }
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedLogEngine?.LogEntries.Clear();
        }

        private async Task<string?> PickModelFileAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".gguf");
            picker.FileTypeFilter.Add(".bin");

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async Task<string?> PromptForModelPathAsync(LlamaEngine engine)
        {
            var currentPath = engine.ModelPath ?? string.Empty;

            var pathBox = new TextBox
            {
                Text = currentPath,
                IsReadOnly = true,
                PlaceholderText = "Select a model...",
                Height = 36,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 360,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var browseButton = new Button
            {
                Content = "Browse",
                Height = 36,
                MinWidth = 90
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(pathBox);
            row.Children.Add(browseButton);
            Grid.SetColumn(browseButton, 1);

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "Model File (.gguf / .bin)", FontSize = 12 });
            panel.Children.Add(row);

            var root = (Content as FrameworkElement)?.XamlRoot;
            if (root == null)
            {
                return await PickModelFileAsync();
            }

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = $"Select Model for {engine.Name}",
                Content = panel,
                PrimaryButtonText = "Start Server",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(currentPath)
            };

            browseButton.Click += async (_, __) =>
            {
                var pickedPath = await PickModelFileAsync();
                if (!string.IsNullOrWhiteSpace(pickedPath))
                {
                    pathBox.Text = pickedPath;
                    dialog.IsPrimaryButtonEnabled = true;
                }
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(pathBox.Text))
            {
                return pathBox.Text;
            }

            return null;
        }

        private async void PickModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LlamaEngine engine)
            {
                var path = await PickModelFileAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    engine.ModelPath = path;
                }
            }
        }

        private async void RunServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LlamaEngine engine)
            {
                if (!engine.IsInstalled || engine.IsServerRunning || engine.IsServerStarting || engine.IsServerStopping)
                {
                    return;
                }

                var pickedPath = await PromptForModelPathAsync(engine);
                if (string.IsNullOrWhiteSpace(pickedPath))
                {
                    return;
                }

                engine.ModelPath = pickedPath;

                await engine.RunServerCommand.ExecuteAsync(null);
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
                
                _ = Task.Run(async () =>
                {
                    await WaitForServerStopAsync(ActiveEngine);
                    App.MainDispatcher?.TryEnqueue(async () =>
                    {
                        await ActiveEngine.RunServerCommand.ExecuteAsync(null);
                    });
                });
            }
        }

        private static async Task WaitForServerStopAsync(LlamaEngine engine, int timeoutMs = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!engine.IsServerRunning && !engine.IsServerStopping)
                {
                    return;
                }

                await Task.Delay(300);
            }
        }
    }
}
