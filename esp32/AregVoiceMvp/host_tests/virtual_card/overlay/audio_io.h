// Replaces the real audio_io.h inside the staged copy of the sketch only
// (the real one pulls in I2S and the MP3 decoder). The tests define these.
#pragma once
extern bool g_sd_available;
bool audio_sd_available();
bool audio_sd_has_file(const char *path);
