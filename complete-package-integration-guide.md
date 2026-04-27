# 🚀 Complete Integration Guide

## 📋 Prerequisites Checklist

- [x] Visual Studio 2022 (17.0 or later)
- [x] Windows App SDK 1.8 workload installed
- [x] .NET 8.0 SDK
- [x] Windows 10 (19041+) or Windows 11

---

## 📦 Step 1: Install Required NuGet Packages

Add these to your `llama-server-winui.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  <PackageReference Include="H.NotifyIcon.WinUI" Version="2.0.116" />
  <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.7175" />
  <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.251106002" />
</ItemGroup>
```

### Install via Package Manager Console:
```powershell
Install-Package H.NotifyIcon.WinUI -Version 2.0.116
```

Or via .NET CLI:
```bash
dotnet add package H.NotifyIcon.WinUI --version 2.0.116
```

---

## 📁 Step 2: File Structure

Create this directory structure in your project:

```
llama-server-winui/
├── App.xaml
├── App.xaml.cs                    ← REPLACE
├── MainWindow.xaml                ← REPLACE
├── MainWindow.xaml.cs             ← REPLACE
├── LlamaEngine.cs                 ← REPLACE
├── Converters/
│   └── Converters.cs              ← NEW FILE
├── Services/
│   ├── FileDownloader.cs          ← KEEP EXISTING
│   └── ProcessLifecycleManager.cs ← NEW FILE
└── Assets/
    ├── Square44x44Logo.png        ← EXISTING (use for tray)
    └── TrayIconActive.ico         ← OPTIONAL (create later)
```

---

## 🔧 Step 3: Add New Files

### 3.1 Create `Converters/Converters.cs`

Right-click project → Add → New Folder → Name it "Converters"
Right-click "Converters" folder → Add → Class → Name it "Converters.cs"

**Copy the complete content from the artifact: "Converters.cs"**

### 3.2 Create `Services/ProcessLifecycleManager.cs`

Right-click "Services" folder (create if doesn't exist) → Add → Class → Name it "ProcessLifecycleManager.cs"

**Copy the complete content from the artifact: "ProcessLifecycleManager.cs"**

---

## 🔄 Step 4: Replace Existing Files

### 4.1 Replace `App.xaml`

**Backup your existing `App.xaml` first!**

Then replace with the content from artifact: "App.xaml"

### 4.2 Replace `App.xaml.cs`

**Backup your existing `App.xaml.cs` first!**

Then replace with the content from artifact: "App.xaml.cs"

### 4.3 Replace `MainWindow.xaml`

**Backup your existing `MainWindow.xaml` first!**

Then replace with the content from artifact: "MainWindow.xaml"

### 4.4 Replace `MainWindow.xaml.cs`

**Backup your existing `MainWindow.xaml.cs` first!**

Then replace with the content from artifact: "MainWindow.xaml.cs"

### 4.5 Replace `LlamaEngine.cs`

**Backup your existing `LlamaEngine.cs` first!**

Then replace with the content from artifact: "LlamaEngine.cs"

---

## 🎨 Step 5: Icon Assets (Optional but Recommended)

The app will work without custom icons using the default Square44x44Logo.png. For production:

### Option A: Use Existing Icon (Quick Start)
The app will automatically fall back to `Assets/Square44x44Logo.png` for the tray icon.

### Option B: Create Custom Icons (Production)

1. **Create TrayIcon.ico** (gray/idle state)
   - 16x16, 32x32, 48x48 sizes
   - Gray llama or simple icon
   - Save as `Assets/TrayIcon.ico`

2. **Create TrayIconActive.ico** (green/active state)
   - Same sizes as above
   - Green tint or green dot overlay
   - Save as `Assets/TrayIconActive.ico`

3. **Add to project:**
   ```xml
   <ItemGroup>
     <Content Include="Assets\TrayIcon.ico" />
     <Content Include="Assets\TrayIconActive.ico" />
   </ItemGroup>
   ```

4. **Update App.xaml.cs icon paths** (lines 122 & 139):
   ```csharp
   // Change from:
   new Uri("ms-appx:///Assets/Square44x44Logo.png")
   
   // To:
   new Uri("ms-appx:///Assets/TrayIcon.ico")
   new Uri("ms-appx:///Assets/TrayIconActive.ico")
   ```

---

## ✅ Step 6: Build & Test

### 6.1 Clean and Rebuild

```powershell
# In Visual Studio
Build → Clean Solution
Build → Rebuild Solution
```

Or via command line:
```bash
dotnet clean
dotnet build
```

### 6.2 Expected Warnings (Safe to Ignore)

You may see:
- Warnings about x:Bind function calls (normal for WinUI 3)
- Warnings about nullable reference types (safe if building in Release)

### 6.3 Critical Errors to Fix

If you see:
- **"Type 'H.NotifyIcon.TaskbarIcon' not found"**
  → Install H.NotifyIcon.WinUI NuGet package

- **"Namespace 'Converters' does not exist"**
  → Check that Converters.cs is in the correct folder with correct namespace

- **"ProcessLifecycleManager not found"**
  → Check that ProcessLifecycleManager.cs is in Services folder

---

## 🧪 Step 7: Run & Validate

### 7.1 First Launch Test

1. Press **F5** to run in Debug mode
2. **Expected behavior:**
   - Window opens showing "Runtime Extension Packs"
   - System tray icon appears (check bottom-right of Windows taskbar)
   - Sidebar shows "Runtime Engines" highlighted in blue
   - Three engine cards visible: Vulkan, CUDA, CPU
   - Right panel shows "No server running" message

### 7.2 Download Test

1. Click "Download" on any engine
2. **Expected behavior:**
   - Button changes to "Downloading..." with spinner
   - Progress bar appears in the card
   - Status shows "X.X / Y.Y MB (Z.Z MB/s)"
   - After completion: "Ready ✓" badge appears
   - Right-click tray icon → Should now show "▶️ Start [Engine Name]"

### 7.3 Server Start Test

1. Click "Run Server" on downloaded engine
2. **Expected behavior:**
   - Button changes to "Stop Server" (red)
   - Green dot appears next to engine name
   - "RUNNING" badge appears
   - Right panel switches to show metrics:
     - CPU usage updates every second
     - Memory usage shown
     - Uptime counter ticking
   - Tray icon changes color (if custom icons used)
   - Tray tooltip shows "1 server(s) running"

### 7.4 Window Hide/Show Test

1. Click [X] button on window
2. **Expected behavior:**
   - Window hides (not closes)
   - Tray icon remains
   - Server continues running (check Task Manager for llama-server.exe)
3. Click tray icon (left-click)
4. **Expected behavior:**
   - Window reappears
   - Server still shown as running
   - Metrics continue updating

### 7.5 Tray Menu Test

1. Right-click tray icon
2. **Expected menu:**
   ```
   🦙 Llama Server Manager
   ─────────────────────────
   Show Window
   ─────────────────────────
   ⏸️  Stop [Engine Name]
   ─────────────────────────
   ❌ Exit
   ```
3. Click "Stop [Engine Name]"
4. **Expected behavior:**
   - Server stops within 10 seconds
   - Window updates (if visible) to show "Stopped"
   - Tray icon returns to idle color
   - Tray menu changes to "▶️ Start [Engine Name]"

### 7.6 Graceful Exit Test

1. Start a server
2. Right-click tray → "Exit"
3. **Expected behavior:**
   - All servers stop automatically
   - Window closes
   - Tray icon disappears
   - No orphaned llama-server.exe processes (verify in Task Manager)

---

## 🐛 Troubleshooting

### Problem: Tray icon doesn't appear

**Solution:**
1. Check Windows Settings → Personalization → Taskbar → "Select which icons appear on the taskbar"
2. Ensure "LlamaServerWinUI" is enabled
3. Restart the app

**Code Check:**
```csharp
// In App.xaml.cs, verify this line isn't throwing:
_trayIcon = new TaskbarIcon();
```

### Problem: Window doesn't hide on [X] click

**Solution:**
Check `MainWindow.xaml.cs`:
```csharp
private void OnWindowClosing(...)
{
    if (!((App)Application.Current).IsExiting)
    {
        args.Cancel = true;  // ← This line must be present
        this.Hide();
    }
}
```

### Problem: Metrics not updating

**Solution:**
1. Check that `ProcessLifecycleManager.cs` has this line:
   ```csharp
   _monitorTimer = new Timer(MonitorCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
   ```

2. Verify events are wired in `LlamaEngine.cs`:
   ```csharp
   _processManager.MetricsUpdated += OnMetricsUpdated;
   ```

### Problem: Download fails immediately

**Solution:**
1. Check internet connection
2. Verify GitHub URLs are correct
3. Check firewall isn't blocking downloads
4. Look in debug output for error messages

### Problem: Server doesn't start

**Solution:**
1. Verify llama-server.exe exists in extracted folder
2. Check Windows Defender didn't quarantine the executable
3. Ensure port 8080 isn't already in use:
   ```powershell
   netstat -ano | findstr :8080
   ```
4. Check debug log:
   ```
   %LocalAppData%\LlamaServerWinUI\debug_log.txt
   ```

### Problem: Compilation errors about x:Bind

**Solution:**
Some x:Bind function calls may need adjustment for your WinUI version:
```xml
<!-- If this fails: -->
<TextBlock Text="{x:Bind IsDownloading, Mode=OneWay}"/>

<!-- Replace with: -->
<TextBlock>
    <TextBlock.Text>
        <Binding Path="IsDownloading" Mode="OneWay"/>
    </TextBlock.Text>
</TextBlock>
```

---

## 📊 Performance Benchmarks (Expected)

| Operation | Expected Time | Notes |
|-----------|---------------|-------|
| App startup | < 2 seconds | Including tray icon |
| Download 200MB | 20-60 seconds | Depends on connection |
| Extract ZIP | 5-15 seconds | Depends on file count |
| Server start | 10-30 seconds | Includes health check |
| Server stop | < 10 seconds | Graceful shutdown timeout |
| Metrics update | 1 second | Timer interval |

---

## 🎓 Architecture Overview

### Data Flow

```
User Action (Window or Tray)
        ↓
   Command Execution (ICommand)
        ↓
   LlamaEngine State Change
        ↓
   PropertyChanged Event
        ↓
   ┌──────────────┬──────────────┐
   ↓              ↓              ↓
Window UI    Tray Menu    Active Panel
(x:Bind)    (Rebuild)     (x:Bind)
```

### Thread Safety

- **UI Thread**: All XAML bindings, user interactions
- **Background Thread**: Downloads, file extraction, process monitoring
- **Synchronization**: `MainDispatcher.TryEnqueue()` for cross-thread UI updates

### State Management

- **Single Source of Truth**: `App.Engines` (ObservableCollection)
- **Shared Between**: MainWindow and Tray menu
- **Synchronized Via**: PropertyChanged events

---

## 🚀 Next Steps After Integration

1. **Test thoroughly** with the validation steps above
2. **Create custom icons** for better branding
3. **Add error handling** for your specific use cases
4. **Implement persistence** (save installed engines state)
5. **Add logging view** (full logs page in sidebar)
6. **Implement settings** (auto-update, default port, etc.)
7. **Package for Microsoft Store** (MSIX)

---

## 📝 Files Modified Summary

| File | Action | Critical? |
|------|--------|-----------|
| App.xaml | Modified | ✅ Yes - Adds converters |
| App.xaml.cs | **REPLACE** | ✅ Yes - Tray integration |
| MainWindow.xaml | **REPLACE** | ✅ Yes - New layout |
| MainWindow.xaml.cs | **REPLACE** | ✅ Yes - Active engine tracking |
| LlamaEngine.cs | **REPLACE** | ✅ Yes - Process manager integration |
| Converters.cs | **NEW** | ✅ Yes - Required for bindings |
| ProcessLifecycleManager.cs | **NEW** | ✅ Yes - Core functionality |
| FileDownloader.cs | Keep existing | ⚠️ Already working |

---

## ✅ Success Criteria

Your integration is complete when:

- [x] App starts with window + tray icon
- [x] Download works with progress bar in card
- [x] Server starts with health check
- [x] Right panel shows live metrics
- [x] Tray menu synchronizes with window
- [x] Window hides on [X], servers keep running
- [x] Exit from tray stops all servers gracefully
- [x] No compilation errors or warnings (except x:Bind function warnings)

---

## 🆘 Support

If you encounter issues:

1. **Check debug log**: `%LocalAppData%\LlamaServerWinUI\debug_log.txt`
2. **Enable Debug output**: View → Output → Show output from: Debug
3. **Test each component** individually using the validation steps
4. **Verify NuGet packages** are restored correctly

---

## 🎉 Congratulations!

You now have a production-ready, LM Studio-inspired llama.cpp manager with:
- ✅ Professional three-column layout
- ✅ System tray integration
- ✅ Real-time performance monitoring
- ✅ Synchronized state management
- ✅ Graceful shutdown handling
- ✅ Modern WinUI 3 design

**Next milestone**: Package for Microsoft Store distribution!
