using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using H.NotifyIcon;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace llama_server_winui
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// Implements system tray integration with synchronized state between window and tray menu.
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

        /// <summary>
        /// Shared engine collection - single source of truth for both window and tray
        /// </summary>
        public static ObservableCollection<LlamaEngine> Engines { get; private set; } = new();
        
        private TaskbarIcon? _trayIcon;
        private bool _isExiting = false;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainDispatcher = MainWindow.DispatcherQueue;
            
            // Initialize engines first
            InitializeEngines();
            
            // Setup tray icon before showing window
            InitializeTrayIcon();
            
            // Subscribe to all engine state changes for tray synchronization
            foreach (var engine in Engines)
            {
                engine.PropertyChanged += Engine_PropertyChanged;
            }
            
            // Show window on launch
            MainWindow.Activate();
        }

        private void InitializeEngines()
        {
            Engines.Add(new LlamaEngine
            {
                Name = "Vulkan llama.cpp",
                Description = "Vulkan accelerated - AMD, Intel, NVIDIA GPUs",
                Tag = "Engine",
                CurrentVersion = "Not installed",
                LatestVersion = "b4969",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b4969/llama-b4969-bin-win-vulkan-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b4969"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CUDA llama.cpp",
                Description = "NVIDIA CUDA accelerated - Best for NVIDIA GPUs",
                Tag = "Engine",
                CurrentVersion = "Not installed",
                LatestVersion = "b3376",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b3376/llama-b3376-bin-win-cuda-cu12.2.0-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b3376"
            });

            Engines.Add(new LlamaEngine
            {
                Name = "CPU llama.cpp",
                Description = "CPU-only using AVX2 - Works on any modern CPU",
                Tag = "Engine",
                CurrentVersion = "Not installed",
                LatestVersion = "b4969",
                DownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b4969/llama-b4969-bin-win-avx2-x64.zip",
                ReleaseNotesUrl = "https://github.com/ggml-org/llama.cpp/releases/tag/b4969"
            });

            foreach (var engine in Engines)
            {
                _ = engine.RefreshInstallationDetailsAsync();
            }
        }

        /// <summary>
        /// Handles engine state changes and updates tray accordingly
        /// Critical for keeping window and tray synchronized
        /// </summary>
        private void Engine_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Update tray on state changes that affect visibility
            if (e.PropertyName == nameof(LlamaEngine.IsServerRunning) ||
                e.PropertyName == nameof(LlamaEngine.IsInstalled) ||
                e.PropertyName == nameof(LlamaEngine.StatusMessage))
            {
                MainDispatcher?.TryEnqueue(() =>
                {
                    UpdateTrayIcon();
                    UpdateTrayMenu();
                });
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new TaskbarIcon();
                
                // Set initial icon and tooltip
                UpdateTrayIcon();
                
                // Left-click toggles window visibility
                _trayIcon.LeftClickCommand = new RelayCommand(ToggleWindowVisibility);
                
                // Build initial context menu
                UpdateTrayMenu();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        private void UpdateTrayIcon()
        {
            if (_trayIcon == null) return;

            var runningCount = Engines.Count(e => e.IsServerRunning);
            
            if (runningCount > 0)
            {
                // Active state - green indicator
                _trayIcon.ToolTipText = $"Llama Server Manager - {runningCount} server(s) running";
                
                // Try to load active icon, fallback to default
                try
                {
                    _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri("ms-appx:///Assets/TrayIconActive.ico"));
                }
                catch
                {
                    // Fallback to regular icon if active icon not found
                    try
                    {
                        _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                            new Uri("ms-appx:///Assets/Square44x44Logo.png"));
                    }
                    catch { }
                }
            }
            else
            {
                // Idle state - gray/default icon
                _trayIcon.ToolTipText = "Llama Server Manager - No servers running";
                
                try
                {
                    _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri("ms-appx:///Assets/Square44x44Logo.png"));
                }
                catch { }
            }
        }

        private void UpdateTrayMenu()
        {
            if (_trayIcon == null) return;

            var menu = new MenuFlyout();

            // Header (disabled, just for display)
            var header = new MenuFlyoutItem 
            { 
                Text = "🦙 Llama Server Manager",
                IsEnabled = false
            };
            menu.Items.Add(header);
            menu.Items.Add(new MenuFlyoutSeparator());

            // Show/Hide Window toggle
            var windowToggle = new MenuFlyoutItem
            {
                Text = (MainWindow?.Visible ?? false) ? "Hide Window" : "Show Window",
                Command = new RelayCommand(ToggleWindowVisibility)
            };
            menu.Items.Add(windowToggle);
            menu.Items.Add(new MenuFlyoutSeparator());

            // Dynamic engine controls
            var installedEngines = Engines.Where(e => e.IsInstalled).ToList();
            
            if (installedEngines.Any())
            {
                foreach (var engine in installedEngines)
                {
                    var engineItem = new MenuFlyoutItem
                    {
                        Text = engine.IsServerRunning 
                            ? $"⏸️  Stop {engine.Name}" 
                            : $"▶️  Start {engine.Name}",
                        Command = engine.IsServerRunning 
                            ? engine.StopServerCommand 
                            : engine.RunServerCommand
                    };
                    menu.Items.Add(engineItem);
                }
            }
            else
            {
                var noEnginesItem = new MenuFlyoutItem
                {
                    Text = "No engines installed",
                    IsEnabled = false
                };
                menu.Items.Add(noEnginesItem);
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            // Exit application
            var exitItem = new MenuFlyoutItem
            {
                Text = "❌ Exit",
                Command = new RelayCommand(ExitApplication)
            };
            menu.Items.Add(exitItem);

            _trayIcon.ContextFlyout = menu;
        }

        private void ToggleWindowVisibility()
        {
            if (MainWindow == null) return;

            try
            {
                if (MainWindow.Visible)
                {
                    MainWindow.Hide();
                }
                else
                {
                    MainWindow.Activate();
                }
                
                // Update menu to reflect new state
                UpdateTrayMenu();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling window: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            _isExiting = true;

            try
            {
                // Stop all running servers gracefully
                var runningEngines = Engines.Where(e => e.IsServerRunning).ToList();
                
                foreach (var engine in runningEngines)
                {
                    try
                    {
                        engine.StopServer();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping {engine.Name}: {ex.Message}");
                    }
                }

                // Clean up tray icon
                _trayIcon?.Dispose();
                _trayIcon = null;

                // Close window
                MainWindow?.Close();

                // Exit application
                Exit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during exit: {ex.Message}");
                // Force exit even if cleanup fails
                Exit();
            }
        }

        /// <summary>
        /// Indicates whether the application is in the process of exiting
        /// Used by MainWindow to determine whether to hide or actually close
        /// </summary>
        public bool IsExiting => _isExiting;
    }
}
