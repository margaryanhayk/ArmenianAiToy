// -------------------------------------------------------------
// AregVoiceMvp / fw_version_marker.cpp -- the in-image version marker.
//
//   "AREGFWV1:<AREG_FW_VERSION>:<AREG_BOARD_MODEL>:<AREG_PROFILE_NAME>"
//
// Lives inside the Secure-Boot-signed body, so the signature covers it.
// ota_apply.cpp scans a downloaded image for it (fw_marker_rules.h);
// tools/firmware/check_release_image.py requires exactly one copy with the
// expected version and profile "release" before an image may be signed or
// staged. `used` keeps the linker from dropping it even though no code
// reads it by name. Never put this magic in any other string literal.
// -------------------------------------------------------------
#include "fw_version_marker.h"

#include "fw_marker_rules.h"
#include "security_profile.h"

extern "C" __attribute__((used)) const char kAregFwMarker[] =
    "AREGFWV1:" AREG_FW_VERSION ":" AREG_BOARD_MODEL ":" AREG_PROFILE_NAME;

static_assert(sizeof("AREGFWV1:") - 1 == fw_marker::kMagicLen, "marker magic length");

const char *fw_marker_magic() { return kAregFwMarker; }
