# sokol_filesystem — Cross-Platform Filesystem Module

## Overview

`sokol_filesystem.h` is a single-header, cross-platform filesystem utility library for [Sokol.NET](https://github.com/elix22/Sokol.NET). It follows the same design language as the other sokol headers (single-header, `SOKOL_FILESYSTEM_IMPL` trigger, `SOKOL_DLL` export macros) and exposes an API that covers the same surface area as [SDL3's filesystem module](https://wiki.libsdl.org/SDL3/CategoryFilesystem).

---

## Files Created / Modified

| Path | Description |
|------|-------------|
| `ext/sokol/sokol_filesystem.h` | **New** — the single-header library |
| `ext/sokol_filesystem_impl.c` | **New** — thin C implementation trigger (non-Apple) |
| `ext/sokol_filesystem_impl.m` | **New** — thin ObjC implementation trigger (Apple) |
| `ext/CMakeLists.txt` | **Modified** — adds filesystem source to the `sokol` shared library |
| `ext/sokol.c` | **Modified** — Android JNI bridge `nativeSetInternalStoragePath` |
| `bindgen/c/sokol_filesystem.c` | **New** — bindgen wrapper for P/Invoke generation |
| `tools/SokolApplicationBuilder/templates/Android/native-activity/.../SokolNativeActivity.java` | **Modified** — calls `nativeSetInternalStoragePath` from `onCreate` |

---

## Public API

### Types

```c
sfs_result_t        // SFS_RESULT_OK, _ERROR, _NOT_FOUND, _PERMISSION, _INVALID_PARAM
sfs_path_type_t     // SFS_PATHTYPE_NONE, _FILE, _DIRECTORY, _OTHER
sfs_path_info_t     // type, size, create_time, modify_time, access_time (Unix epoch int64)
sfs_folder_t        // HOME, DESKTOP, DOCUMENTS, DOWNLOADS, MUSIC, PICTURES, …
sfs_enum_result_t   // SFS_ENUM_CONTINUE, _SUCCESS, _FAILURE
sfs_enumerate_callback_t  // void* userdata, const char* dirname, const char* fname
sfs_glob_flags_t    // SFS_GLOB_NONE, SFS_GLOB_CASE_INSENSITIVE
sfs_open_mode_t     // SFS_OPEN_READ, _WRITE, _APPEND, _READ_WRITE, _CREATE_WRITE, _APPEND_READ
sfs_whence_t        // SFS_WHENCE_SET, _CUR, _END
sfs_file_t          // opaque file handle (wraps FILE*)
```

### Path Queries (heap-allocated — call `sfs_free_path()` when done)

```c
char*        sfs_get_base_path(void);
char*        sfs_get_pref_path(const char* org, const char* app);
char*        sfs_get_assets_dir(void);          // same as base_path on all platforms
char*        sfs_get_user_folder(sfs_folder_t folder);
char*        sfs_get_current_directory(void);
sfs_result_t sfs_set_current_directory(const char* path);
char*        sfs_get_temp_dir(void);
void         sfs_free_path(char* path);
```

### File / Directory Operations

```c
sfs_result_t sfs_create_directory(const char* path);         // creates parent dirs too
sfs_result_t sfs_remove_path(const char* path);              // file or empty dir
sfs_result_t sfs_rename_path(const char* oldpath, const char* newpath);
sfs_result_t sfs_copy_file(const char* oldpath, const char* newpath);
sfs_result_t sfs_get_path_info(const char* path, sfs_path_info_t* out_info);
bool         sfs_path_exists(const char* path);
bool         sfs_is_directory(const char* path);
bool         sfs_is_file(const char* path);
int64_t      sfs_get_last_modified_time(const char* path);   // Unix epoch, 0 on failure
```

### Directory Enumeration

```c
sfs_result_t sfs_enumerate_directory(const char* path,
                                      sfs_enumerate_callback_t callback,
                                      void* userdata);
```

### Glob / Pattern Matching

```c
char** sfs_glob_directory(const char* path, const char* pattern,
                           sfs_glob_flags_t flags, int* out_count);
void   sfs_free_glob_results(char** results, int count);
```

Pattern syntax: `*` (any chars), `?` (one char), `[abc]` / `[a-z]` / `[!abc]`

### File I/O

`sfs_file_t` is an opaque handle. All functions are safe to call with a `NULL` handle.

```c
sfs_file_t*  sfs_open_file(const char* path, sfs_open_mode_t mode);
sfs_result_t sfs_close_file(sfs_file_t* file);
int64_t      sfs_read_file(sfs_file_t* file, void* buf, int64_t count);
int64_t      sfs_write_file(sfs_file_t* file, const void* buf, int64_t count);
int64_t      sfs_seek_file(sfs_file_t* file, int64_t offset, sfs_whence_t whence);
int64_t      sfs_tell_file(sfs_file_t* file);
int64_t      sfs_get_file_size(sfs_file_t* file);      // non-destructive (restores position)
sfs_result_t sfs_flush_file(sfs_file_t* file);
bool         sfs_eof_file(sfs_file_t* file);
```

`sfs_open_mode_t` values:

| Constant | Equivalent `fopen` mode | Description |
|----------|-------------------------|-------------|
| `SFS_OPEN_READ` | `"rb"` | Read-only |
| `SFS_OPEN_WRITE` | `"wb"` | Write-only, truncate |
| `SFS_OPEN_APPEND` | `"ab"` | Append |
| `SFS_OPEN_READ_WRITE` | `"r+b"` | Read + write (file must exist) |
| `SFS_OPEN_CREATE_WRITE` | `"w+b"` | Read + write, create/truncate |
| `SFS_OPEN_APPEND_READ` | `"a+b"` | Read + append |

### Error Reporting

```c
const char* sfs_get_error(void);
```

### Android Setup

```c
void sfs_set_android_internal_path(const char* path);
```

---

## Platform Implementations

| Platform | Base path | Pref path | User folders | Asset reads | Notes |
|----------|-----------|-----------|--------------|-------------|-------|
| **Windows** | `GetModuleFileNameW` | `%APPDATA%\org\app\` | `SHGetKnownFolderPath` (CSIDL fallback) | POSIX `fopen` | All calls use UTF-8 → wide-char → UTF-8 |
| **macOS** | `NSBundle.resourcePath` (or CWD when not in `.app`) | `NSApplicationSupportDirectory/org/app/` | `NSSearchPathForDirectoriesInDomains` | POSIX `fopen` | Compiled as ObjC via `.m` |
| **iOS** | `NSBundle.resourcePath` | `NSApplicationSupportDirectory/org/app/` | Limited; Documents/Downloads/Music/Pictures | POSIX `fopen` | ObjC; tvOS uses `NSCachesDirectory` |
| **Linux** | `/proc/self/exe` parent | `$XDG_DATA_HOME/org/app/` or `~/.local/share/org/app/` | Standard subdirs under `$HOME` | POSIX `fopen` | POSIX stat/dirent shared block |
| **Android** | `"./"` | `<internalDataPath>/` (JNI-provided) | Not supported | POSIX `fopen` first, then `AAssetManager` fallback for APK assets | APK-bundled assets have no mtime (returns 0) |
| **Emscripten** | `"/"` | `/libsokol/org/app/` | Not supported | Synchronous XHR (`overrideMimeType`) | POSIX stat tried first; HTTP HEAD/GET fallback for web-served assets |

**POSIX operations** (create/remove/rename/copy/stat/enumerate/glob) are shared across macOS, iOS, Linux, and Android via the `_SFS_POSIX_IMPL` block.

### Android — APK Asset Fallback

For read operations on paths that fail POSIX `stat()` (i.e. APK-bundled assets), the Android implementation falls back to `AAssetManager`:

- `sfs_get_path_info` — tries `stat()`, then `AAssetManager_open()` / `AAssetManager_openDir()`. File size is available; **modification time is always 0** for APK assets.
- `sfs_open_file` (read mode) — tries `fopen()`, then `AAssetManager_open()` → reads the entire asset into a `malloc`'d buffer → wraps it in a `fmemopen()` handle. The buffer is freed on `sfs_close_file`.
- Leading `./` and `/` are automatically stripped before calling `AAssetManager` (e.g. `./test_data/sample.txt` → `test_data/sample.txt`).

Write operations (`WRITE`, `APPEND`, etc.) always use POSIX `fopen()` into the app's internal data path.

### Emscripten — Synchronous HTTP Fallback

.NET WASM does not use Emscripten's `--preload-file` mechanism, so APK-style preloading is unavailable. Assets are served by the HTTP server and accessed via synchronous XHR:

- `sfs_get_path_info` — tries POSIX `stat()` first (covers MEMFS pref path); on failure sends a synchronous `HEAD` request. `Content-Length` → `size`; `Last-Modified` header → `modify_time` (parsed via `new Date(lm).getTime() / 1000`).
- `sfs_open_file` (read mode) — sends a synchronous `GET` with `overrideMimeType('text/plain; charset=x-user-defined')` (required because `responseType='arraybuffer'` is forbidden on synchronous XHR on the main thread). The response is decoded byte-by-byte via `charCodeAt(i) & 0xFF`, stored in a `malloc`'d buffer, and wrapped in `fmemopen()`. The buffer is freed on `sfs_close_file`.
- Write operations use POSIX `fopen()` into the Emscripten MEMFS (pref path).
- `sfs_copy_file` — fetches source via HTTP GET, writes destination via POSIX.

---

## Build System Integration (CMakeLists.txt)

```cmake
# Apple: compile the ObjC trigger file
if (APPLE)
    set(SOKOL_FILESYSTEM_SRCS "sokol_filesystem_impl.m")
else()
    set(SOKOL_FILESYSTEM_SRCS "sokol_filesystem_impl.c")
endif()

add_library(sokol ${SOKOL_LIBRARY_TYPE}
    "sokol.c"
    ${SOKOL_FILESYSTEM_SRCS}
    ...
)
```

The `sokol_filesystem_impl.c` / `.m` files each contain:

```c
#define SOKOL_FILESYSTEM_IMPL
#define SOKOL_DLL
#include "sokol/sokol_filesystem.h"
```

No additional link libraries are required — all platform system libs used are already linked as part of the `sokol` target.

---

## Android JNI Setup

`SokolNativeActivity.java` calls the native method as early as possible in `onCreate`:

```java
nativeSetInternalStoragePath(getFilesDir().getAbsolutePath());
```

`sokol.c` implements the JNI entrypoint:

```c
JNIEXPORT void JNICALL
Java_com_sokol_app_SokolNativeActivity_nativeSetInternalStoragePath(
    JNIEnv* env, jobject thiz, jstring path)
{
    const char* cpath = (*env)->GetStringUTFChars(env, path, NULL);
    if (cpath) {
        sfs_set_android_internal_path(cpath);
        (*env)->ReleaseStringUTFChars(env, path, cpath);
    }
}
```

The stored path is used by `sfs_get_pref_path()`, `sfs_get_temp_dir()`, and write-mode `sfs_open_file()` on Android.

---

## Emscripten Persistent Storage

By default the pref path lives under `/libsokol/` in the Emscripten virtual FS (MEMFS). This is in-memory only. To persist across page reloads, mount IDBFS from JavaScript:

```js
Module.preRun = Module.preRun || [];
Module.preRun.push(function() {
    FS.mkdir('/libsokol');
    FS.mount(IDBFS, {}, '/libsokol');
    FS.syncfs(true, function(err) { /* loaded */ });
});
// After writes, call FS.syncfs(false, cb) to flush.
```

---

## C# Bindings (Sokol.NET Bindgen)

`bindgen/c/sokol_filesystem.c` is the bindgen entry point:

```c
#if defined(IMPL)
#define SOKOL_FILESYSTEM_IMPL
#endif
#include "sokol_defines.h"
#include "ext/sokol/sokol_filesystem.h"
```

Add `"sokol_filesystem"` to the module list in `bindgen/gen.py` to generate C# P/Invoke wrappers for every `sfs_*` function.

### Key binding notes

- All `sfs_get_*` path functions return `IntPtr` (marshalled as `char*`). Use `Marshal.PtrToStringUTF8` then call `sfs_free_path`.
- `bool`-returning functions (`sfs_path_exists`, `sfs_is_file`, `sfs_is_directory`, `sfs_eof_file`) require `[return: MarshalAs(UnmanagedType.I1)]` on native platforms; on Web they are wrapped to return `int` and compared to `!= 0`.
- `sfs_enumerate_callback_t` is passed as `IntPtr` with a `[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]` static method.
- `sfs_open_file` / `sfs_close_file` / file I/O functions take/return `IntPtr` (opaque `sfs_file_t*`).
- `sfs_path_info_t` is a blittable sequential struct:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct sfs_path_info_t {
    public sfs_path_type_t type;
    public long size;
    public long create_time;
    public long modify_time;
    public long access_time;
}
```

---

## Usage Example (C)

```c
#define SOKOL_FILESYSTEM_IMPL
#include "sokol_filesystem.h"

// Get the app's writable data dir
char* pref = sfs_get_pref_path("MyOrg", "MyApp");
if (pref) {
    // Write a save file
    char path[512];
    snprintf(path, sizeof(path), "%ssave.dat", pref);

    sfs_file_t* f = sfs_open_file(path, SFS_OPEN_CREATE_WRITE);
    if (f) {
        const char* data = "hello";
        sfs_write_file(f, data, 5);
        sfs_close_file(f);
    }
    sfs_free_path(pref);
}

// Enumerate *.png files in the base dir
char* base = sfs_get_base_path();
int count = 0;
char** pngs = sfs_glob_directory(base, "*.png", SFS_GLOB_NONE, &count);
for (int i = 0; i < count; i++) printf("%s\n", pngs[i]);
sfs_free_glob_results(pngs, count);
sfs_free_path(base);
```

