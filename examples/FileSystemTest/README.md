# FileSystemTest

A comprehensive test application for the [`sokol_filesystem`](../../docs/SOKOL_FILESYSTEM.md) module — a cross-platform single-header filesystem library for [Sokol.NET](../../README.md).

## Overview

The app runs **88 tests** across five categories at startup and presents results in a tabbed ImGui UI rendered over a GLSL gradient background. Every `sfs_*` API surface is exercised.

| Category | Tests | What is covered |
|----------|-------|-----------------|
| **Paths** | 6 | `sfs_get_base_path`, `sfs_get_pref_path`, `sfs_get_assets_dir`, `sfs_get_temp_dir`, `sfs_get_current_directory`, `sfs_set_current_directory` |
| **Assets** | 22 | Read-only APK/bundle assets: `sfs_is_file`, `sfs_get_path_info`, `sfs_get_last_modified_time`, `sfs_open_file`, `sfs_get_file_size`, `sfs_read_file`, `sfs_eof_file`, `sfs_seek_file`, `sfs_tell_file`; binary integrity (all 256 byte values 0x00–0xFF) |
| **File I/O** | 27 | Writable pref path: create/write/flush/read/seek/tell/append/copy/rename/remove; binary write + round-trip |
| **Dir/Glob** | 6 | `sfs_enumerate_directory`, `sfs_glob_directory` (case-sensitive, case-insensitive, wildcard prefix) |
| **User Folders** | 7 | `sfs_get_user_folder` for Home, Desktop, Documents, Downloads, Music, Pictures, SavedGames |

## Screenshot

![FileSystemTest – All Results tab showing 88/88 PASS](../../screenshots/FileSystemTest.png)

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| macOS | ✅ 88/88 | Metal |
| iOS | ✅ 88/88 | Metal |
| Android | ✅ 88/88 | APK assets via `AAssetManager`; mtime=0 for bundled assets is expected |
| WebAssembly | ✅ 88/88 | Assets via synchronous XHR; binary-safe (`charCodeAt & 0xFF`) |
| Windows | ✅ | Direct3D 11 |
| Linux | ✅ | OpenGL |

## Asset Files

| File | Description |
|------|-------------|
| `Assets/test_data/sample.txt` | Plain-text file used for text read / seek / tell / eof tests |
| `Assets/test_data/numbers.dat` | Legacy number data file |
| `Assets/test_data/binary.bin` | 256-byte binary file (`byte[i] = i`, 0x00–0xFF) — verifies binary read integrity across all platforms |
| `Assets/readme.txt` | Presence-check asset |

## Key Implementation Notes

- **Binary safety** — `SFS_OPEN_READ` always uses `"rb"` mode (POSIX/Windows). On Emscripten, `overrideMimeType('text/plain; charset=x-user-defined')` + `charCodeAt(i) & 0xFF` preserves all 256 byte values. On Android, `AAsset_read` is raw binary.
- **Android APK assets** — `sfs_open_file`/`sfs_get_path_info` fall back from POSIX `stat`/`fopen` to `AAssetManager` automatically. The buffer is heap-allocated, wrapped in `fmemopen`, and freed on `sfs_close_file`.
- **Emscripten** — POSIX `stat` is tried first (covers writable MEMFS pref path); HTTP `HEAD`/`GET` fallback handles web-served assets.
- **`ImGuiWindowFlags.NoBackground`** — the fullscreen ImGui window is transparent so the sokol-rendered gradient background (`color_top = dark navy`, `color_bottom = medium blue`) shows through.

## Running

### Desktop (macOS/Windows/Linux)

```bash
cd examples/FileSystemTest
dotnet build FileSystemTest.csproj -t:CompileShaders
dotnet run --project FileSystemTest.csproj
```

### VS Code

Press **F5** and select **FileSystemTest** → desired platform.

### Web

Use **Tasks: Run Task** → **prepare-FileSystemTest-web**, then open the local HTTP server.

### Android / iOS

Use **Tasks: Run Task** → **Android: Install APK** or **iOS: Install** after selecting **FileSystemTest** as the active example.

## Related Documentation

- [`docs/SOKOL_FILESYSTEM.md`](../../docs/SOKOL_FILESYSTEM.md) — complete API reference, platform notes, build system integration
- [`ext/sokol/sokol_filesystem.h`](../../ext/sokol/sokol_filesystem.h) — single-header C library source
- [`src/sokol/generated/SFilesystem.cs`](../../src/sokol/generated/SFilesystem.cs) — auto-generated C# P/Invoke bindings
