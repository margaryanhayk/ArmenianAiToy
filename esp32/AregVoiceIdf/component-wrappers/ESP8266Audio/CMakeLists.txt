# IDF component wrapper for ESP8266Audio (upstream ships none). Mirrors the
# Arduino library build: every .c/.cpp under src/ (recursive), src/ included.
# Copied into the fetched library by tools/firmware/idf_fetch_deps.py.
file(GLOB_RECURSE srcs "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c" "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp")
idf_component_register(SRCS ${srcs}
                       INCLUDE_DIRS "src"
                       REQUIRES arduino esp_driver_i2s)
# Arduino builds libraries with -w (warnings=none) -> never -Werror here.
target_compile_options(${COMPONENT_LIB} PRIVATE -w)
