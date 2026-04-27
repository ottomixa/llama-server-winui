# Dynamic GitHib Version Checking & UI Feedback

## Goal
Replace hardcoded `llama.cpp` versions with real-time data from the GitHub API. Provide a "Checking for updates..." preloader and improve UI feedback for the "Run Server" state (e.g., "No model selected").

## Proposed Changes

### 1. New Service: `Services/GitHubService.cs`
- **Method**: `Task<ReleaseInfo> GetLatestReleaseAsync()`
- **Logic**:
    - GET `https://api.github.com/repos/ggml-org/llama.cpp/releases/latest`
    - Parse JSON to get `tag_name` and `assets` list.
    - Identify download URLs for:
        - `avx2` (CPU)
        - `vulkan`
        - `cuda` (Handle missing assets gracefully by falling back to pinned version if needed).

### 2. UI Updates: `MainWindow.xaml`
- **Preloader**: Add a `Grid` overlay (ProgressRing + Text) visible while `IsLoading` is true.
- **Run Button Feedback**: Add a `TextBlock` next to the Run/Stop buttons that displays validation errors (e.g., "⚠ Select a model first").
- **Binding**: Bind `RunServerCommand.CanExecute` to the button's enablement (already done), but verify the visual feedback matches user request.

### 3. Logic Updates: `MainWindow.xaml.cs`
- **OnLoad**:
    - Set `IsLoading = true`.
    - Call `GitHubService`.
    - Loop through `Engines` and update `LatestVersion` and `DownloadUrl` based on the fetched assets.
    - If CUDA asset is missing in `latest`, log warning and keep pinned fallback (or search for specific CUDA release).
    - Set `IsLoading = false`.

### 4. Engine Logic: `LlamaEngine.cs`
- **Property**: Add `ValidationMessage` (Observable).
- **Update**: Inside `CanRunServer`, update `ValidationMessage` (e.g., "No model selected") so the UI can bind to it.

## Verification
- Launch app -> Verify "Checking..." appears.
- Verify CPU/Vulkan update to latest (e.g., `b4969` or newer).
- Verify CUDA handles availability (either updates or stays on working version).
- Verify "Run Server" is disabled with explicit text when no model is selected.
