# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

WinUI 3 desktop "runtime manager" for [llama.cpp](https://github.com/ggml-org/llama.cpp): downloads pre-built `llama-server.exe` binaries from GitHub Releases (Vulkan / CUDA / CPU-AVX2 variants) and runs them locally with monitoring. .NET 8, x64-only, unpackaged or MSIX.

## Build / run / test

The project requires Visual Studio 2022 with Windows App SDK + .NET desktop workloads. From the repo root:

```bash
# Restore + build (debug)
dotnet build llama-server-winui.sln

# Run unpackaged (the "(Unpackaged)" launch profile)
dotnet run --project llama-server-winui.csproj

# Tests (separate solution-less project, MSTest)
dotnet test llama-server-winui.Tests/llama-server-winui.Tests.csproj

# Run a single test
dotnet test llama-server-winui.Tests/llama-server-winui.Tests.csproj --filter "FullyQualifiedName~DownloadFileAsync_DownloadsFileAndReportsProgress"

# Publish self-contained (uses Properties/PublishProfiles/win-x64.pubxml)
dotnet publish llama-server-winui.csproj -c Release -p:PublishProfile=win-x64
```

Notes:
- `RuntimeIdentifiers` declares `win-x86;win-x64`, but `<Platform>x64</Platform>` and the manifest target x64 only — don't try to build x86/arm64 without changes.
- `WindowsAppSDKSelfContained=true` and `SelfContained=true` are set; published output is large but standalone.
- Release builds enable `PublishReadyToRun` and `PublishTrimmed` — keep reflection use minimal or add trim warnings.
- The test project does NOT reference the main project. It link-includes `Services/FileDownloader.cs` directly (`<Compile Include="..\Services\FileDownloader.cs" Link=...>`). When adding tests for other services, either add similar link-includes or convert to a `ProjectReference` — but note the main csproj targets `net8.0-windows10.0.22621.0` while tests target `net8.0-windows10.0.19041.0`.

## Architecture

### Single source of truth: `App.Engines`

`App.xaml.cs` owns `static ObservableCollection<LlamaEngine> Engines` and `static DispatcherQueue MainDispatcher`. Both `MainWindow` and the system tray (`H.NotifyIcon.WinUI`) bind to this collection. The three engine instances (Vulkan / CUDA / CPU) are constructed in `InitializeEngines()` with hardcoded fallback URLs/versions, then `MainWindow.InitializeVersionsAsync()` overwrites `LatestVersion`/`DownloadUrl` from the latest GitHub release via `GitHubService` (asset name patterns: `bin-win-vulkan-x64.zip`, `bin-win-cuda-cu12*x64.zip`, `bin-win-avx2-x64.zip`; `cudart*` assets are excluded).

Both `App` and `MainWindow` subscribe to each engine's `PropertyChanged` and react to `IsServerRunning` / `IsInstalled` / `StatusMessage` to keep tray menu, tray icon, and the right-hand "active server" panel in sync. Anything mutating engine state from a background thread MUST marshal through `App.MainDispatcher.TryEnqueue` — direct UI updates from worker threads will crash WinUI's D3D thread.

### `LlamaEngine` (MVVM model with embedded behavior)

This is intentionally not a thin model — it contains the download/run/stop commands and process supervision. Generated `[ObservableProperty]` source generators require `LangVersion=preview` and use the C# 13 `partial` property syntax (`public partial string X { get; set; }`). When adding properties:
- Use `partial void OnXChanged` to fan out `OnPropertyChanged` for derived `Visibility`/display properties (e.g., `RunButtonVisibility`, `InstalledVersionDisplay`).
- Use `[NotifyCanExecuteChangedFor(nameof(...Command))]` to keep button enabled-state in sync with state flags.

Key state machine flags: `IsInstalled`, `IsDownloading`, `IsServerStarting`, `IsServerRunning`, `IsServerStopping`. `CanRunServer()` requires installed + idle + non-empty `ModelPath`.

### Download flow: `Task.Run` + `InterlockedProgress` + `DispatcherQueueTimer` polling

`LlamaEngine.DownloadAndInstall` does NOT `await` the download. Pattern (in `LlamaEngine.cs`):
1. Background `Task.Run` calls `FileDownloader.DownloadFileAsync` and writes into a thread-safe `InterlockedProgress` tracker (uses `Interlocked.Exchange` for byte counters, `volatile` flags for completion/failure).
2. UI thread starts a `DispatcherQueueTimer` at 100 ms ticks (`StartProgressTimer` → `UpdateDownloadUI`) that reads the tracker and updates `DownloadProgress` / `StatusMessage`.
3. On completion the timer kicks off another `Task.Run` for extraction, which dispatches the final state flip back to the UI thread.

This avoids `IProgress<T>`-from-worker → UI marshalling cost on hot loops. Don't "simplify" it back to await + Progress<T> without measuring.

Extraction is destructive: it kills any `llama-server` processes whose `MainModule.FileName` lies under the target folder, then renames the existing folder to `_trash_<ticks>` and deletes asynchronously, falling back to `RetryDeleteDirectoryAsync` (10× × 500ms).

### Process lifecycle: `Services/ProcessLifecycleManager`

Generic supervisor for `llama-server.exe` (or any process). Wires `OutputDataReceived` + `ErrorDataReceived` (note: llama.cpp logs to stderr — both streams are treated as info, NOT errors), polls `WorkingSet64` and `TotalProcessorTime` every 1s via a `Timer`, raises `MetricsUpdated` / `OutputReceived` / `StateChanged`. `Stop()` does graceful `CloseMainWindow` → 10s wait → `Kill(entireProcessTree:true)`. `ForceKill()` skips the wait and is what `App.ForceCleanExit` uses on `ProcessExit`/Ctrl+C.

`LlamaEngine.WaitForServerReadyAsync` does an additional TCP connect probe to `127.0.0.1:DefaultPort` (8080, hardcoded constant) for up to 90s after start — this is what flips status from "Loading model..." to "Server running on port 8080". The `ProcessLifecycleManager.PerformHealthCheck` (HTTP-based) exists but is currently NOT used by `LlamaEngine` (it passes no `healthCheckUrl`).

### Log batching (D3D thread protection)

`LlamaEngine.AppendLogLine` queues entries under a lock and schedules ONE `Task.Delay(50)` flush onto the dispatcher. Flooding `LogEntries.Add` from the stderr pump directly on the UI thread causes WinUI render-thread stalls. Preserve this batching when modifying log handling. `MaxLogEntries = 2000` — older entries are evicted FIFO.

### Storage layout

All engine artifacts live under `%LOCALAPPDATA%\LlamaServerWinUI\` (`Environment.SpecialFolder.LocalApplicationData`):
- `Engines\{EngineName}\` — extracted `llama-server.exe` and DLLs
- `{EngineName}.zip` — temp download (deleted after extract)
- `debug_log.txt` — appended by `LlamaEngine.Log()`

`RefreshInstallationDetailsAsync` searches recursively for `llama-server.exe` because some llama.cpp release zips wrap the binaries in a subfolder.

### Tray + window lifecycle

Closing the window via the X button HIDES it (`MainWindow` intercepts `AppWindow.Closing` and cancels unless `App.IsExiting`). Real exit happens only through the tray's "Exit" item, which calls `App.ExitApplication()`. `ProcessExit` / `Console.CancelKeyPress` handlers in `App` call `ForceKillServer()` on every running engine so closing a `dotnet run` terminal doesn't leave orphaned `llama-server.exe` processes.

## Conventions / things that bite

- **Nullable enabled, `LangVersion=preview`** — partial properties from CommunityToolkit MVVM 8.4 require this. Don't downgrade.
- **`MainWindow` implicit operator to `FrameworkElement`** (returns `RootGrid`, with a fallback empty `Grid` if called pre-`InitializeComponent`). XAML codegen relies on this; don't remove it.
- **UI-thread marshalling** is via `App.MainDispatcher?.TryEnqueue(...)` — there is no DI/services container, just static accessors on `App`.
- **Hardcoded port 8080** lives as `LlamaEngine.DefaultPort` and `ProcessLifecycleManager.ExtractPortFromArguments` fallback. Changing it requires updating both.
- **Version comparison** uses `ExtractBuildNumber` regex `(?:b)?(\d+)` against tags like `b4969`. Non-numeric tags break update detection silently (`IsUpdateAvailable` stays false).
- `tools/procdump/procdump.exe` and `procdump64a.exe` are gitignored — checked in for crash analysis but not redistributed.

## Reference files

- `complete-package-integration-guide.md` — third-party integration notes (root, untracked but committed pattern; consult for deployment-related questions).
- `docs/implementation_plan-002.md` — design notes for an in-progress refactor.
- `README.md` — user-facing overview, includes a llama.cpp source-build recipe (vcpkg + cmake) that is unrelated to this app's runtime; do not confuse it with how to build this project.
