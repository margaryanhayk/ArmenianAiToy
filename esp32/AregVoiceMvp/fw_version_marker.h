#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / fw_version_marker.h -- this image's own signed version
// marker, and the magic other images are scanned for (fw_marker_rules.h).
// -------------------------------------------------------------
#include <stddef.h>

extern "C" const char kAregFwMarker[];

// The first fw_marker::kMagicLen bytes of kAregFwMarker. Taking the magic
// from the marker itself keeps exactly ONE copy of it in the image.
const char *fw_marker_magic();
