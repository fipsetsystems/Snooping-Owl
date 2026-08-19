#pragma once

#include <QString>

namespace configuration {

// Logging configuration, per schema version 1.
struct LoggingSettings
{
    QString level = QStringLiteral("info"); // debug | info | warn | critical
};

// Server connection configuration, per schema version 1.
struct ConnectionSettings
{
    QString url = QStringLiteral("ws://127.0.0.1:8080/ws/agent");
    QString token = QStringLiteral("dev-token");
};

// The agent's configuration boundary.
//
// Holds a versioned JSON document at a well-known machine-wide path so the
// schema can evolve without ambiguity:
//   Windows: %ProgramData%\SnoopingOwl\config.json
//   Linux:   ~/.config/SnoopingOwl/config.json (dev subset)
//
// Only fields with an established purpose exist today (logging, server
// connection). Identity and enrollment state are added in later phases.
class Configuration
{
public:
    static Configuration& instance();

    // Loads from filePath, creating it with defaults on first run.
    // Returns false if the file exists but cannot be parsed.
    bool load(const QString& filePath);

    // Returns the path the configuration was loaded from (or would be
    // written to).
    QString filePath() const;

    // Logging section, with defaults when absent.
    LoggingSettings logging() const;

    // Server connection section, with defaults when absent.
    ConnectionSettings connection() const;

private:
    Configuration() = default;

    QString m_filePath;
    LoggingSettings m_logging;
    ConnectionSettings m_connection;
    int m_schemaVersion = 0;
};

// Platform-appropriate configuration file path.
QString defaultConfigFilePath();

} // namespace configuration