# 🦙 Llama Server WinUI

A modern Windows desktop application for downloading, managing, and running [llama.cpp](https://github.com/ggml-org/llama.cpp) server instances. Built with **WinUI 3** and designed for users who want a simple, graphical way to run local LLMs without using the command line.

![Windows](https://img.shields.io/badge/platform-Windows-blue) ![.NET 8](https://img.shields.io/badge/.NET-8.0-purple) ![WinUI 3](https://img.shields.io/badge/WinUI-3-green)

---

## 📖 Overview

**Llama Server WinUI** is a "Runtime Manager" for llama.cpp, similar to how [LM Studio](https://lmstudio.ai/) manages local LLM runtimes. It provides a clean, card-based UI to:

1. **Download** pre-built llama.cpp binaries directly from GitHub Releases
2. **Manage** multiple engine variants (CUDA, Vulkan, CPU)
3. **Run** the `llama-server.exe` with a single click

This application is perfect for users who want to leverage powerful open-source LLMs locally but prefer a GUI over command-line tools like Ollama.

---

## ✨ Features

### Implemented Features

| Feature | Description |
|---------|-------------|
| **Multi-Engine Support** | Supports three llama.cpp variants: CUDA (NVIDIA), Vulkan, and CPU-only |
| **One-Click Download** | Downloads engine binaries from GitHub Releases with a single button click |
| **Automatic Extraction** | Extracts downloaded ZIP files to a local `Engines` folder |
| **Version Display** | Shows current installed version and latest available version |
| **Progress Indication** | Displays download and extraction progress with status messages |
| **Server Launch** | Runs `llama-server.exe` on port 8080 with one click |
| **Modern UI** | Clean card-based layout inspired by LM Studio using WinUI 3 |
| **MVVM Architecture** | Uses CommunityToolkit.MVVM for clean separation of concerns |
| **Self-Contained Deployment** | Can be deployed as a self-contained Windows application |

---

## 🏗️ Architecture

### Technology Stack

- **Framework**: WinUI 3 with Windows App SDK 1.8
- **Language**: C# with .NET 8.0
- **UI Pattern**: MVVM using [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **Target**: Windows 10 (19041+) / Windows 11

### Project Structure

```
llama-server-winui/
├── App.xaml              # Application entry point and resources
├── App.xaml.cs           # Application startup logic
├── MainWindow.xaml       # Main UI layout with card-based ListView
├── MainWindow.xaml.cs    # Engine initialization and data binding
├── LlamaEngine.cs        # Data model with download/run commands
├── Assets/               # Application icons and logos
└── Properties/           # Launch settings and publish profiles
```

### Core Components

#### `LlamaEngine.cs`
The data model representing a single llama.cpp runtime variant. Key features:
- Observable properties for reactive UI updates
- `DownloadAndInstall()` command for downloading from GitHub
- `RunServer()` command for launching llama-server.exe
- `InverseVisibility()` helper for conditional button display

#### `MainWindow.xaml`
The main UI implementing a card-based layout with:
- Release channel selector (Stable/Beta)
- ListView with custom DataTemplate for engine cards
- Download, Run, and status display for each engine

---

## 💻 Getting Started

### Prerequisites

- Windows 10 (build 19041) or Windows 11
- Visual Studio 2022 with:
  - **.NET desktop development** workload
  - **Windows App SDK C++ Templates** (for full WinUI 3 support)

### Build and Run

1. Clone the repository:
   ```powershell
   git clone https://github.com/yourusername/llama-server-winui.git
   cd llama-server-winui
   ```

2. Open `llama-server-winui.sln` in Visual Studio 2022

3. Restore NuGet packages (should happen automatically)

4. Press **F5** to run in Debug mode

---

## Compilation Instructions

To compile the llama-server, follow these steps:

1. Clone the vcpkg repository:
   ```bash
   git clone https://github.com/Microsoft/vcpkg.git
   cd vcpkg
   ```

2. Install curl and other dependencies:
   ```bash
   .\bootstrap-vcpkg.bat
   .\vcpkg install curl:x64-windows
   ```

3. Configure the build:
   ```bash
   cmake -S . -B build -DCMAKE_TOOLCHAIN_FILE="vcpkg/scripts/buildsystems/vcpkg.cmake"
   ```

4. Build the project:
   ```bash
   cmake --build build --config Release
   ```

**Note:** If your antivirus moves any executable files to quarantine (e.g., Avast antivirus flagging `Win64:MalwareX-gen`), ensure to restore them from quarantine and add exceptions as necessary.

## Downloading llama-server.exe

To download the `llama-server.exe` from a trusted source, you can use the following command:

```bash
curl -L -o llama-server.exe https://github.com/YourRepo/llama-server/releases/latest/download/llama-server.exe
```

Replace `YourRepo` with the actual repository name where the executable is hosted.

---

## 📋 Requested Features (from Development History)

The following features were requested during the initial development phase:

### Core Requirements
1. ✅ Windows UI form for managing llama.cpp libraries
2. ✅ Download llama.cpp engine binaries from GitHub Releases
3. ✅ Run llama-server from the downloaded binaries
4. ✅ Support for multiple engine variants (CUDA, Vulkan, CPU)
5. ✅ Card-based layout similar to LM Studio
6. ✅ Version tracking (current vs. latest)

### Technical Requirements
1. ✅ WinUI 3 Desktop application
2. ✅ MSIX packaging support for Microsoft Store distribution
3. ✅ MVVM architecture for clean code organization
4. ✅ CommunityToolkit.Mvvm for boilerplate reduction
5. ✅ Reactive UI updates with ObservableProperty

### UI/UX Requirements
1. ✅ Download button with progress indication
2. ✅ Run Server button (visible only after installation)
3. ✅ Status messages for user feedback
4. ✅ Release Notes hyperlink
5. ✅ Stable/Beta channel selector

---

## 🛠️ Development Notes

### Design Decisions

1. **C# over C++**: While C++ could provide tighter llama.cpp integration, C# was chosen for faster development and easier maintenance.

2. **WinUI 3**: Chosen for modern Windows look and feel, plus Microsoft Store deployment support.

3. **No XAML Designer**: WinUI 3 doesn't have a traditional design view. Use **XAML Live Preview** and **Hot Reload** during debugging instead.

### Known Considerations

- The app uses `Windows.Storage.ApplicationData.Current.LocalFolder` for engine storage
- Server launches on port 8080 by default (hardcoded)
- Download URLs currently use mock data pointing to specific GitHub release versions

---

## 📄 License

This project is open source. See [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

- [llama.cpp](https://github.com/ggml-org/llama.cpp) - The amazing C/C++ LLM inference engine
- [LM Studio](https://lmstudio.ai/) - UI inspiration
- [CommunityToolkit](https://github.com/CommunityToolkit/dotnet) - MVVM helpers
