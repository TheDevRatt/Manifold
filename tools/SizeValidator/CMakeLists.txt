cmake_minimum_required(VERSION 3.20)
project(ManifoldSizeValidator CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Steam SDK include path — relative to this CMakeLists.txt
set(STEAM_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../steamworks_sdk_164/sdk/public")

add_executable(size_validator size_validator.cpp)

target_include_directories(size_validator PRIVATE "${STEAM_SDK_ROOT}")

# Match the same pack pragma that ManifoldGen uses:
# VALVE_CALLBACK_PACK_SMALL on Linux/macOS, VALVE_CALLBACK_PACK_LARGE on Windows.
if(UNIX AND NOT APPLE)
    target_compile_definitions(size_validator PRIVATE VALVE_CALLBACK_PACK_SMALL=1)
elseif(APPLE)
    target_compile_definitions(size_validator PRIVATE VALVE_CALLBACK_PACK_SMALL=1)
else()
    target_compile_definitions(size_validator PRIVATE VALVE_CALLBACK_PACK_LARGE=1)
endif()

# Suppress noisy SDK warnings
target_compile_options(size_validator PRIVATE -w)
