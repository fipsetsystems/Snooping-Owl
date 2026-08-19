#pragma once

#include <QString>

namespace identity {

// Stable per-machine identifier used in the `hello` handshake.
//
// Uses the platform machine ID where available (Windows MachineGuid), else
// a random value; persists the result next to the configuration file so it
// survives restarts and upgrades.
QString deviceId();

} // namespace identity