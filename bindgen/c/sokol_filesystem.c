/*
    sokol_filesystem.c -- bindgen wrapper for sokol_filesystem.h

    This file triggers the single-header implementation and exposes every
    sfs_* symbol so that the Sokol.NET bindgen pipeline can parse the
    declarations and generate P/Invoke C# bindings.

    Usage in bindgen/gen.py: add "sokol_filesystem" to the module list.
*/
#if defined(IMPL)
#define SOKOL_FILESYSTEM_IMPL
#endif
#include "sokol_defines.h"
#ifdef __APPLE__
/* sokol_filesystem.h uses Objective-C APIs on Apple platforms,
   so this file must be compiled as Objective-C (.m) or you must
   include the .m wrapper below.  When building through CMake the
   file is already set to compile as Objective-C via
   set_source_files_properties. */
#endif
#include "ext/sokol/sokol_filesystem.h"
