#include "diagnostics/file_logger.h"

#include <QDateTime>
#include <QDir>
#include <QFileInfo>
#include <QMessageLogContext>

namespace {

// Named limits instead of scattered magic numbers.
constexpr qint64 kDefaultMaxBytes = 5 * 1024 * 1024; // 5 MB per file
constexpr int kDefaultMaxFiles = 5;

const char* levelName(QtMsgType type)
{
    switch (type) {
    case QtDebugMsg:    return "debug";
    case QtInfoMsg:     return "info";
    case QtWarningMsg:  return "warn";
    case QtCriticalMsg: return "critical";
    case QtFatalMsg:    return "fatal";
    }
    return "unknown";
}

QtMsgType minLevelForName(const QString& name)
{
    if (name == QLatin1String("debug"))     return QtDebugMsg;
    if (name == QLatin1String("warn"))      return QtWarningMsg;
    if (name == QLatin1String("critical"))  return QtCriticalMsg;
    return QtInfoMsg; // default: info
}

void logToFile(QtMsgType type, const QMessageLogContext& context,
               const QString& message)
{
    diagnostics::FileLogger::instance().handleMessage(type, context, message);
}

} // namespace

namespace diagnostics {

FileLogger& FileLogger::instance()
{
    static FileLogger logger;
    return logger;
}

void FileLogger::initialize(const QString& directory, const QString& fileName,
                            qint64 maxBytes, int maxFiles, bool mirrorToStderr)
{
    QMutexLocker lock(&m_mutex);

    if (m_initialized) {
        return;
    }

    if (!QDir().mkpath(directory)) {
        // Fall back to stderr if the log directory cannot be created; the
        // service is otherwise silent and failures would be invisible.
        qInstallMessageHandler(nullptr);
        qWarning("Cannot create log directory: %s", qPrintable(directory));
        qInstallMessageHandler(&logToFile);
        m_initialized = false;
        return;
    }

    m_directory = directory;
    m_fileName = fileName;
    m_maxBytes = maxBytes > 0 ? maxBytes : kDefaultMaxBytes;
    m_maxFiles = maxFiles > 0 ? maxFiles : kDefaultMaxFiles;
    m_mirrorToStderr = mirrorToStderr;

    m_file.setFileName(QDir(m_directory).filePath(m_fileName));
    if (!m_file.open(QIODevice::WriteOnly | QIODevice::Append)) {
        m_initialized = false;
        return;
    }

    m_initialized = true;
    qInstallMessageHandler(&logToFile);
}

void FileLogger::handleMessage(QtMsgType type, const QMessageLogContext& context,
                               const QString& message)
{
    QMutexLocker lock(&m_mutex);

    if (!m_initialized) {
        return;
    }
    if (!passesLevelFilter(type)) {
        return;
    }

    rotateIfNeeded();

    const QString category = context.category && *context.category
        ? QString::fromUtf8(context.category)
        : QStringLiteral("default");
    const QString line = lineFor(type, category, message);
    const QByteArray bytes = line.toUtf8();

    if (m_file.isOpen()) {
        m_file.write(bytes);
        m_file.flush();
    }
    if (m_mirrorToStderr) {
        fputs(bytes.constData(), stderr);
    }
}

bool FileLogger::passesLevelFilter(QtMsgType type) const
{
    // QtMsgType ordering: debug(0) < info(1) < warning(2) < critical(3).
    return type >= m_minLevel;
}

void FileLogger::setMinimumLevel(const QString& levelName)
{
    QMutexLocker lock(&m_mutex);
    m_minLevel = minLevelForName(levelName);
}

QString FileLogger::lineFor(QtMsgType type, const QString& category,
                            const QString& message) const
{
    const QString timestamp =
        QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs);
    return QStringLiteral("%1 [%2] [%3] %4\n")
        .arg(timestamp, QString::fromLatin1(levelName(type)),
             category, sanitize(message));
}

QString FileLogger::sanitize(const QString& text) const
{
    QString clean = text;
    clean.replace(QLatin1Char('\r'), QLatin1Char(' '));
    clean.replace(QLatin1Char('\n'), QLatin1Char(' '));
    return clean;
}

void FileLogger::rotateIfNeeded()
{
    if (!m_file.isOpen() || m_file.size() < m_maxBytes) {
        return;
    }

    m_file.close();

    // Shift agent.log.N -> agent.log.N+1, oldest dropped.
    for (int i = m_maxFiles - 1; i >= 1; --i) {
        const QString from =
            QDir(m_directory).filePath(QStringLiteral("%1.%2").arg(m_fileName).arg(i));
        const QString to =
            QDir(m_directory).filePath(QStringLiteral("%1.%2").arg(m_fileName).arg(i + 1));
        QFile::remove(to);
        QFile::rename(from, to);
    }
    QFile::remove(QDir(m_directory).filePath(QStringLiteral("%1.1").arg(m_fileName)));
    QFile::rename(m_file.fileName(),
                  QDir(m_directory).filePath(QStringLiteral("%1.1").arg(m_fileName)));

    if (!m_file.open(QIODevice::WriteOnly | QIODevice::Append)) {
        qWarning("Log rotation failed to reopen %s",
                 qPrintable(m_file.fileName()));
    }
}

QString defaultLogDirectory()
{
#ifdef Q_OS_WIN
    // LocalSystem has no meaningful user profile; %ProgramData% is the
    // machine-wide location for service data.
    return QStringLiteral("%1/SnoopingOwl/Logs").arg(
        qEnvironmentVariable("ProgramData", QStringLiteral("C:/ProgramData")));
#else
    return QDir::homePath() + QStringLiteral("/.local/share/SnoopingOwl/logs");
#endif
}

void initializeLogging(const QString& directory, bool mirrorToStderr)
{
    FileLogger::instance().initialize(directory, QStringLiteral("agent.log"),
                                      kDefaultMaxBytes, kDefaultMaxFiles,
                                      mirrorToStderr);
}

void shutdownLogging()
{
    qInstallMessageHandler(nullptr);
}

} // namespace diagnostics