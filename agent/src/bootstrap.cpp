#include "bootstrap.h"

#include "configuration/configuration.h"
#include "diagnostics/file_logger.h"

namespace agent {

bool initializeFoundation(bool mirrorToStderr)
{
    configuration::Configuration& config = configuration::Configuration::instance();
    if (!config.load(configuration::defaultConfigFilePath())) {
        return false;
    }

    diagnostics::initializeLogging(diagnostics::defaultLogDirectory(),
                                   mirrorToStderr);
    diagnostics::FileLogger::instance().setMinimumLevel(config.logging().level);
    return true;
}

} // namespace agent