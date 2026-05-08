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
    VERSION   0.1.0
    LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Position-Independent Code is required for the shared library AND
# for object files used by the test binary linked against the .so;
# enabling globally keeps the two consistent without per-target
# duplication.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Same flag set the standalone compile check used in RM-M3-01/02.
# RM-M3-09 (native-coverage gate / sanitizers) will broaden this.
add_compile_options(
    -Wall
    -Wextra
    -Wpedantic
    -Werror)

# The native library: shared so the .NET P/Invoke side (RM-M3-04)
# can dlopen it directly. SONAME tracks the project version so a
# system-wide install (out of scope for M3 part 1) would version
# the .so file.
add_library(battery_control_core SHARED
    src/compute.cpp)

target_include_directories(battery_control_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)

set_target_properties(battery_control_core PROPERTIES
    VERSION   ${PROJECT_VERSION}
    SOVERSION ${PROJECT_VERSION_MAJOR})

# Test binary. RM-M3-08 will replace the hand-rolled harness with a
# proper framework + sanitizers + coverage; today this just gives
# CMake something to depend on so `make native-test` can run.
enable_testing()
add_executable(test_battery_control_core
    tests/test_compute.cpp)
target_link_libraries(test_battery_control_core
    PRIVATE battery_control_core)
add_test(NAME test_battery_control_core
    COMMAND test_battery_control_core)
