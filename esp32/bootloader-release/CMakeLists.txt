# Areg RELEASE bootloader (ESP32-S3, ESP-IDF v5.5.4 @ 735507283d).
# Built ONCE per hardware generation by tools/firmware/build_release_bootloader.sh,
# signed OFFLINE with both Secure Boot keys (tools/firmware/sign_release.py),
# flashed only by the factory station. It can never be updated on a locked toy.
# See docs/firmware-security.md s2 and s6.
cmake_minimum_required(VERSION 3.16)
include($ENV{IDF_PATH}/tools/cmake/project.cmake)
idf_build_set_property(MINIMAL_BUILD ON)
project(areg_bootloader)
