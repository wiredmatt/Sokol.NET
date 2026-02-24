cmake_minimum_required(VERSION 3.22 FATAL_ERROR)
project(camerac_android_sample CXX)

# ---------------------------------------------------------------------------
# Locate the camerac root.
# CAMERAC_ROOT is injected by Gradle (app/build.gradle arguments).
# ---------------------------------------------------------------------------
if(NOT DEFINED CAMERAC_ROOT)
    # Fallback: six levels up from this file → ext/camerac/
    get_filename_component(CAMERAC_ROOT
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../../../../"
        ABSOLUTE)
endif()

message(STATUS "camerac root: ${CAMERAC_ROOT}")

# Build camerac as a shared .so (controlled by Gradle via -DBUILD_SHARED_LIBS=ON)
add_subdirectory(${CAMERAC_ROOT} ${CMAKE_CURRENT_BINARY_DIR}/camerac_lib)

# ---------------------------------------------------------------------------
# JNI sample library
# ---------------------------------------------------------------------------
add_library(native_sample SHARED native_sample.cpp)

target_include_directories(native_sample PRIVATE ${CAMERAC_ROOT}/include)

target_link_libraries(native_sample
    PRIVATE
        camerac
        log        # Android logging
)

# Android 15+ 16 KB page alignment
target_link_options(native_sample PRIVATE -Wl,-z,max-page-size=16384)
