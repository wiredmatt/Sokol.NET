cmake_minimum_required(VERSION 3.16 FATAL_ERROR)
project(camerac_sample C)

# -----------------------------------------------------------------------
# Build the camerac library if it hasn't already been pulled in
# (handles both: cmake samples/ standalone, and add_subdirectory from parent)
# -----------------------------------------------------------------------
if(NOT TARGET camerac)
    if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
        # Build as static when pulled in as a sub-project on iOS.
        # A dynamic framework built here would bake the Mac build-dir into its
        # rpath, causing a dyld crash on device.  Static linking avoids the
        # whole framework-embedding problem.
        set(BUILD_SHARED_LIBS OFF)
    endif()
    add_subdirectory(.. camerac_lib)
endif()

# -----------------------------------------------------------------------
# Sample executable
# -----------------------------------------------------------------------
add_executable(camerac_sample camerac_sample.c)

target_include_directories(camerac_sample PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../include
)

target_link_libraries(camerac_sample PRIVATE camerac)

# nanosleep / struct timespec are POSIX extensions; expose them under strict C11
if(NOT WIN32 AND NOT EMSCRIPTEN)
    target_compile_definitions(camerac_sample PRIVATE _POSIX_C_SOURCE=200809L)
endif()

# -----------------------------------------------------------------------
# iOS: must be a proper .app bundle
# -----------------------------------------------------------------------
if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set_target_properties(camerac_sample PROPERTIES
        MACOSX_BUNDLE                        TRUE
        MACOSX_BUNDLE_BUNDLE_NAME            "camerac_sample"
        MACOSX_BUNDLE_GUI_IDENTIFIER         "com.camerac.sample"
        MACOSX_BUNDLE_BUNDLE_VERSION         "1.0"
        MACOSX_BUNDLE_SHORT_VERSION_STRING   "1.0"
        XCODE_ATTRIBUTE_IPHONEOS_DEPLOYMENT_TARGET "13.0"
        # Let Xcode manage signing – set your Team in Xcode or via
        # -DCMAKE_XCODE_ATTRIBUTE_DEVELOPMENT_TEAM=XXXXXXXXXX on the cmake line
        XCODE_ATTRIBUTE_CODE_SIGN_STYLE      "Automatic"
        # Required privacy key – without this iOS kills the app on cam_open()
        MACOSX_BUNDLE_INFO_PLIST             "${CMAKE_CURRENT_SOURCE_DIR}/Info.plist"
    )
    # Force-load all ObjC object files from the static camerac archive
    target_link_options(camerac_sample PRIVATE -ObjC)
endif()

# -----------------------------------------------------------------------
# Usage
# -----------------------------------------------------------------------
# Desktop (macOS):
#   cmake -S . -B build -G Xcode -DCMAKE_BUILD_TYPE=Release
#   cmake --build build --config Release
#
# iOS device (replace XXXXXXXXXX with your Apple Developer Team ID):
#   cmake -S . -B build-ios -G Xcode \
#         -DCMAKE_SYSTEM_NAME=iOS \
#         -DCMAKE_OSX_ARCHITECTURES=arm64 \
#         -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0 \
#         -DCMAKE_XCODE_ATTRIBUTE_DEVELOPMENT_TEAM=XXXXXXXXXX
#   cmake --build build-ios --config Release
#   (or open build-ios/camerac_sample.xcodeproj and run from Xcode)
