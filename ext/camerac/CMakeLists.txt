cmake_minimum_required(VERSION 3.16 FATAL_ERROR)

project(camerac C CXX)

# -----------------------------------------------------------------------
# Global settings
# -----------------------------------------------------------------------
set(CMAKE_C_STANDARD   11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED   ON)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS   OFF)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(CMAKE_OSX_DEPLOYMENT_TARGET "11.0" CACHE STRING "Minimum macOS deployment version")

# Default output dirs (overridden when used as sub-project)
if(CMAKE_CURRENT_SOURCE_DIR STREQUAL CMAKE_SOURCE_DIR)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
endif()

# Support for 16 KB page sizes on Android 15+ (API 35+)
if(ANDROID)
    add_link_options(-Wl,-z,max-page-size=16384)
endif()

# -----------------------------------------------------------------------
# Source files
# -----------------------------------------------------------------------
set(CAMERAC_CORE_SOURCES
    src/camerac.c
)

set(CAMERAC_ALL_SOURCES ${CAMERAC_CORE_SOURCES})
set(CAMERAC_LINK_LIBS   "")
set(CAMERAC_COMPILE_DEFS "")

# -----------------------------------------------------------------------
# Platform-specific backends
# -----------------------------------------------------------------------

if(APPLE)
    # ---- macOS & iOS: CoreMedia / AVFoundation -------------------------
    enable_language(OBJC)   # only probe ObjC compiler on Apple platforms
    list(APPEND CAMERAC_ALL_SOURCES
        src/coremedia/camerac_coremedia.m
    )
    # ARC is required for the Objective-C backend (bridge casts, retain props)
    set_source_files_properties(src/coremedia/camerac_coremedia.m
        PROPERTIES COMPILE_FLAGS "-fobjc-arc"
    )
    list(APPEND CAMERAC_COMPILE_DEFS CAMERAC_BACKEND_COREMEDIA)

    find_library(FOUNDATION_LIB  Foundation   REQUIRED)
    find_library(AVFOUNDATION_LIB AVFoundation REQUIRED)
    find_library(COREMEDIA_LIB   CoreMedia    REQUIRED)
    find_library(COREVIDEO_LIB   CoreVideo    REQUIRED)
    list(APPEND CAMERAC_LINK_LIBS
        ${FOUNDATION_LIB}
        ${AVFOUNDATION_LIB}
        ${COREMEDIA_LIB}
        ${COREVIDEO_LIB}
    )

    if(IOS)
        # UIKit is required for UIDevice orientation (USE_UIKIT_ROTATION)
        find_library(UIKIT_LIB UIKit REQUIRED)
        list(APPEND CAMERAC_LINK_LIBS ${UIKIT_LIB})
    else()
        # macOS only: AppKit for availability checks
        find_library(APPKIT_LIB AppKit)
        if(APPKIT_LIB)
            list(APPEND CAMERAC_LINK_LIBS ${APPKIT_LIB})
        endif()
    endif()

elseif(WIN32)
    # ---- Windows: Media Foundation ------------------------------------
    list(APPEND CAMERAC_ALL_SOURCES
        src/mediafoundation/camerac_mediafoundation.c
    )
    list(APPEND CAMERAC_COMPILE_DEFS CAMERAC_BACKEND_MEDIAFOUNDATION)
    list(APPEND CAMERAC_LINK_LIBS mf mfplat mfreadwrite mfuuid ole32 oleaut32)

elseif(ANDROID)
    # ---- Android: Camera2 NDK -----------------------------------------
    list(APPEND CAMERAC_ALL_SOURCES
        src/android/camerac_android.c
    )
    list(APPEND CAMERAC_COMPILE_DEFS CAMERAC_BACKEND_ANDROID)
    list(APPEND CAMERAC_LINK_LIBS log)   # Android log

elseif(EMSCRIPTEN)
    # ---- Emscripten: getUserMedia -------------------------------------
    list(APPEND CAMERAC_ALL_SOURCES
        src/emscripten/camerac_emscripten.c
    )
    list(APPEND CAMERAC_COMPILE_DEFS CAMERAC_BACKEND_EMSCRIPTEN)
    # EM_ASM / MAIN_THREAD_EM_ASM require GNU extensions (-std=gnu11).
    # Override only for the Emscripten source file so other files are unaffected.
    set_source_files_properties(src/emscripten/camerac_emscripten.c
        PROPERTIES COMPILE_FLAGS "-std=gnu11"
    )

elseif(UNIX)
    # ---- Linux: V4L2 --------------------------------------------------
    list(APPEND CAMERAC_ALL_SOURCES
        src/v4l2/camerac_v4l2.c
    )
    list(APPEND CAMERAC_COMPILE_DEFS CAMERAC_BACKEND_V4L2)
    list(APPEND CAMERAC_LINK_LIBS pthread)
endif()

# -----------------------------------------------------------------------
# Library target
# -----------------------------------------------------------------------
# Build shared by default (dylib/.so/.dll), but allow static for cases like
# embedding into an iOS app without a separate framework-embed step.
if(EMSCRIPTEN OR (NOT BUILD_SHARED_LIBS AND NOT CAMERAC_FORCE_SHARED))
    add_library(camerac STATIC ${CAMERAC_ALL_SOURCES})
else()
    add_library(camerac SHARED ${CAMERAC_ALL_SOURCES})
endif()

target_include_directories(camerac
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_definitions(camerac
    PRIVATE
        CAMERAC_BUILDING_DLL
        ${CAMERAC_COMPILE_DEFS}
)

if(CAMERAC_LINK_LIBS)
    # PUBLIC so system frameworks propagate to the consumer when camerac is
    # linked as a static library (required on Apple platforms).
    if(APPLE)
        target_link_libraries(camerac PUBLIC ${CAMERAC_LINK_LIBS})
    else()
        target_link_libraries(camerac PRIVATE ${CAMERAC_LINK_LIBS})
    endif()
endif()

# Emscripten linker flags: export the permission callback symbol
if(EMSCRIPTEN)
    target_link_options(camerac PRIVATE
        "SHELL:-s EXPORTED_FUNCTIONS=['_camerac_emscripten_permission_outcome']"
    )
endif()

# Hide all symbols on non-Windows; only CAMERAC_API symbols are exported
if(NOT WIN32 AND NOT EMSCRIPTEN)
    set_target_properties(camerac PROPERTIES
        C_VISIBILITY_PRESET   hidden
        CXX_VISIBILITY_PRESET hidden
    )
endif()

# macOS: set install name for relocation
if(APPLE AND NOT IOS)
    set_target_properties(camerac PROPERTIES
        INSTALL_NAME_DIR "@loader_path"
        BUILD_WITH_INSTALL_RPATH ON
    )
endif()

# iOS: produce a proper .framework bundle (same pattern as box2d)
if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set_target_properties(camerac PROPERTIES
        FRAMEWORK                   TRUE
        FRAMEWORK_VERSION           A
        MACOSX_FRAMEWORK_IDENTIFIER com.camerac.camerac
        XCODE_ATTRIBUTE_IPHONEOS_DEPLOYMENT_TARGET "13.0"
        PUBLIC_HEADER               "${CMAKE_CURRENT_SOURCE_DIR}/include/camerac.h"
    )
endif()

# -----------------------------------------------------------------------
# Optional: simple test/example
# -----------------------------------------------------------------------
option(CAMERAC_BUILD_SAMPLE "Build the camerac sample application" OFF)

if(CAMERAC_BUILD_SAMPLE AND NOT EMSCRIPTEN AND NOT IOS AND NOT ANDROID)
    add_executable(camerac_sample samples/camerac_sample.c)
    target_link_libraries(camerac_sample PRIVATE camerac)
    target_include_directories(camerac_sample PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    )
endif()

# -----------------------------------------------------------------------
# Install rules
# -----------------------------------------------------------------------
install(TARGETS camerac
    FRAMEWORK DESTINATION lib
    LIBRARY   DESTINATION lib
    ARCHIVE   DESTINATION lib
    RUNTIME   DESTINATION bin
)
install(FILES include/camerac.h DESTINATION include)

# -----------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------
message(STATUS "")
message(STATUS "camerac configuration:")
message(STATUS "  Platform................: ${CMAKE_SYSTEM_NAME}")
if(ANDROID)
    message(STATUS "  ABI.....................: ${ANDROID_ABI}")
endif()
message(STATUS "  Backends................: ${CAMERAC_COMPILE_DEFS}")
message(STATUS "  Build type..............: ${CMAKE_BUILD_TYPE}")
message(STATUS "  Shared library..........: $<TARGET_FILE:camerac>")
message(STATUS "")
