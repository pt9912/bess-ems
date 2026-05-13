# RM-M3-06 part 1: build wiring for the native control core.
#
# This CMake script builds the .so as a build artefact only — the
# Runtime-image path /app/native/ and the .NET routing both live
# in later slices (RM-M3-06 part 2 + RM-M3-04/05). Running cmake
# here yields:
#   - libbattery_control_core.so
#   - test_battery_control_core (links against the library)
# under the chosen build directory. The Make target
# `make native-build` exercises this through a dedicated Docker
# stage; `make native-test` runs the test binary.
cmake_minimum_required(VERSION 3.20)

project(battery_control_core
    VERSION   0.3.0
    LANGUAGES C CXX)

# RM-M3-09 follow-up: production source is C (compute.c), the test
# binary stays C++ so it can keep using doctest. C11 gets us the
# `<assert.h>`-provided `static_assert` macro plus stdint/stddef
# guarantees; C++17 keeps the doctest harness happy.
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Position-Independent Code is required for the shared library AND
# for object files used by the test binary linked against the .so;
# enabling globally keeps the two consistent without per-target
# duplication.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Same flag set the standalone compile check used in RM-M3-01/02
# plus three RM-M3-05 review (m-2) additions: -Wshadow catches a
# inner-vs-outer name reuse that would silently produce wrong
# values; -Wnull-dereference catches deref'd pointers that
# RM-M3-02's input validation would otherwise quietly mask;
# -Wdouble-promotion catches a stray float literal silently
# promoted into the double pipeline. RM-M3-09 (native-coverage
# gate / sanitizers) broadens further.
add_compile_options(
    -Wall
    -Wextra
    -Wpedantic
    -Werror
    -Wshadow
    -Wnull-dereference
    -Wdouble-promotion)

# RM-M3-09 quality knobs. Both default OFF; the dedicated Docker
# stages (native-sanitizer, native-coverage) flip them on per build.
# Each helper is per-target rather than a global add_compile_options
# so the production .so and the test binary can be instrumented
# together without polluting the default native-build path.
option(BCC_ENABLE_SANITIZERS
    "Build with AddressSanitizer + UndefinedBehaviorSanitizer instrumentation."
    OFF)
option(BCC_ENABLE_COVERAGE
    "Build with gcov coverage instrumentation (gcc only)."
    OFF)

function(bcc_enable_sanitizers target_name)
    if(NOT BCC_ENABLE_SANITIZERS)
        return()
    endif()
    if(NOT CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
        message(FATAL_ERROR
            "BCC_ENABLE_SANITIZERS requires a GNU- or Clang-compatible compiler")
    endif()
    # -fno-sanitize-recover=all turns every detected issue into a hard
    # exit so the test runner returns non-zero — without it ASan/UBSan
    # would log and continue, hiding the failure from ctest.
    set(_san_flags
        -fsanitize=address,undefined
        -fno-omit-frame-pointer
        -fno-sanitize-recover=all)
    target_compile_options(${target_name} PRIVATE ${_san_flags})
    target_link_options(${target_name} PRIVATE ${_san_flags})
endfunction()

function(bcc_enable_coverage target_name)
    if(NOT BCC_ENABLE_COVERAGE)
        return()
    endif()
    if(NOT CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
        message(FATAL_ERROR
            "BCC_ENABLE_COVERAGE requires a GNU- or Clang-compatible compiler")
    endif()
    target_compile_options(${target_name} PRIVATE -O0 -g --coverage)
    target_link_options(${target_name} PRIVATE --coverage)
endfunction()

# The native library: shared so the .NET P/Invoke side (RM-M3-04)
# can dlopen it directly. SONAME tracks the project version so a
# system-wide install (out of scope for M3 part 1) would version
# the .so file.
add_library(battery_control_core SHARED
    src/compute.c)

target_include_directories(battery_control_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)

set_target_properties(battery_control_core PROPERTIES
    VERSION   ${PROJECT_VERSION}
    SOVERSION ${PROJECT_VERSION_MAJOR})

bcc_enable_sanitizers(battery_control_core)
bcc_enable_coverage(battery_control_core)

# RM-M3-08: doctest as the C++ unit-test framework. Pinned via
# CMake FetchContent + URL_HASH SHA256 (the supply-chain analogue
# to NuGet locked-mode, see Directory.Build.props
# `RestorePackagesWithLockFile=true`). Bumping the version requires
# updating BOTH the URL tag AND the SHA256 below; a tampered
# upload at the same URL would fail the hash check and break the
# build instead of silently swapping the framework.
#
# DOWNLOAD_NO_EXTRACT TRUE keeps doctest.h as a single header in
# `${doctest_SOURCE_DIR}/doctest.h` (the URL points to the raw
# header, not a tarball), so the test binary just needs that
# include directory on its search path.
include(FetchContent)
FetchContent_Declare(doctest
    URL      https://raw.githubusercontent.com/doctest/doctest/v2.4.11/doctest/doctest.h
    URL_HASH SHA256=44faa038e9c3f9728efbda143748d01124ea0a27f4bf78f35a15d8fab2e039fb
    DOWNLOAD_NO_EXTRACT TRUE)
FetchContent_MakeAvailable(doctest)

# Test binary. RM-M3-09 plugs sanitizer + coverage gates onto this
# target; the doctest framework here is the seam those gates need.
enable_testing()
add_executable(test_battery_control_core
    tests/test_compute.cpp)
target_link_libraries(test_battery_control_core
    PRIVATE battery_control_core)
target_include_directories(test_battery_control_core
    PRIVATE ${doctest_SOURCE_DIR})
bcc_enable_sanitizers(test_battery_control_core)
bcc_enable_coverage(test_battery_control_core)
add_test(NAME test_battery_control_core
    COMMAND test_battery_control_core)
