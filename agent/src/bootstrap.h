#pragma once

namespace agent {

// Loads the configuration and installs file logging. Called exactly once at
// startup from every run mode (service, --run). Returns false when the
// configuration cannot be loaded; the caller should abort startup.
bool initializeFoundation(bool mirrorToStderr);

} // namespace agent