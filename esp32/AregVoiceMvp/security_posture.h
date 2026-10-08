#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / security_posture.h -- one [sec] line per boot describing
// what the chip actually enforces (eFuses), plus the store mode. Formatting
// is pure (security_posture_rules.h); this reads the hardware.
// Call after secure_store_begin().
// -------------------------------------------------------------
void security_posture_print();

// False for a RELEASE image while ROM download mode is still enabled (the
// factory never finished -- security_posture_rules::network_allowed); true
// otherwise. Read from the eFuse once, then cached. net_transport.cpp and
// ble_provisioning.cpp refuse to go online when it is false.
bool security_network_allowed();
