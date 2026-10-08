# IDF component wrapper for Adafruit NeoPixel (upstream ships none).
# Copied into the fetched library by tools/firmware/idf_fetch_deps.py.
file(GLOB srcs "${CMAKE_CURRENT_SOURCE_DIR}/*.c" "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp")
idf_component_register(SRCS ${srcs}
                       INCLUDE_DIRS "."
                       REQUIRES arduino)
target_compile_options(${COMPONENT_LIB} PRIVATE -w)
