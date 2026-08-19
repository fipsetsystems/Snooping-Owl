#pragma once

#include <QFile>
#include <QMutex>
#include <QString>
#include <QtGlobal>

namespace diagnostics {

// Bounded, rotating file logger installed as the Qt message handler.
//
// Logs are written as one line per message:
//   ISO8601-UTC [level] [category] message
// Messages are sanitized (control characters stripped) and never contain
// credentials or config contents.
class FileLogger
{
public:
    static FileLogger& instance();

    // Creates the log directory if needed and installs this logger as the
    // Qt message handler. Safe to call once at startup.
    void initialize(const QString& directory, const QString& fileName,
                    qint64 maxBytes, int maxFiles, bool mirrorToStderr);

    // Logs a single message, rotating files when the size limit is hit.
    void handleMessage(QtMsgType type, const QMessageLogContext& context,
                       const QString& message);

    // Sets the minimum severity written to the file. Names: debug, info,
    // warn, critical. Anything unrecognized maps to info.
    void setMinimumLevel(const QString& levelName);

private:
    FileLogger() = default;

    void rotateIfNeeded();
    QString lineFor(QtMsgType type, const QString& category,
                    const QString& message) const;
    QString sanitize(const QString& text) const;
    bool passesLevelFilter(QtMsgType type) const;

    QFile m_file;
    QMutex m_mutex;
    QString m_directory;
    QString m_fileName;
    qint64 m_maxBytes = 0;
    int m_maxFiles = 0;
    QtMsgType m_minLevel = QtInfoMsg;
    bool m_initialized = false;
    bool m_mirrorToStderr = false;
};

// Resolves the platform-appropriate log directory:
//   Windows: %ProgramData%\SnoopingOwl\Logs
//   Linux:   ~/.local/share/SnoopingOwl/logs (dev subset)
QString defaultLogDirectory();

// Installs the file logger with default limits.
void initializeLogging(const QString& directory, bool mirrorToStderr = false);

// Removes the file logger as the Qt message handler.
void shutdownLogging();

} // namespace diagnostics