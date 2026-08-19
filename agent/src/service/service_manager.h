#pragma once

namespace service {

// Installs and starts the SnoopingOwl Windows service using the Service
// Control Manager API. Used for development/testing on Windows machines;
// the production installer (WiX MSI) performs the same registration
// transactionally. Requires administrator privileges.
// Returns 0 on success, non-zero on failure.
int installService();

// Stops and removes the service. Requires administrator privileges.
// Returns 0 on success, non-zero on failure.
int uninstallService();

} // namespace service