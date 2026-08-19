#pragma once

namespace service {

// Runs the agent as a Windows service: blocks until the service stops.
// Returns 0 on clean shutdown, non-zero when the SCM could not be reached
// (for example, when the binary was started directly without --run).
int runService();

} // namespace service