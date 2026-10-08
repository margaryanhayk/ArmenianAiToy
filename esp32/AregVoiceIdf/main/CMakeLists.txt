# The staged sketch (see ../CMakeLists.txt): every root .cpp/.c of
# esp32/AregVoiceMvp plus the .ino wrapper -- the same translation units
# arduino-cli compiles. No REQUIRES: main then sees every component, the
# same global include view an Arduino sketch has.
idf_build_get_property(areg_build_dir BUILD_DIR)
set(stage "${areg_build_dir}/areg_sketch")
file(GLOB sketch_srcs "${stage}/*.cpp" "${stage}/*.c")
idf_component_register(SRCS ${sketch_srcs}
                       INCLUDE_DIRS "${stage}")
# arduino-cli never makes a warning fatal except -Werror=return-type
# (platform.txt compiler.common_werror_flags). IDF's global -Werror=all is
# relaxed for the sketch only; warnings stay visible.
target_compile_options(${COMPONENT_LIB} PRIVATE
    -include "${stage}/areg_build_defs.h"
    -Wno-error=all -Wno-error=format-truncation -Werror=return-type)
