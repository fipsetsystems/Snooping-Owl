#include "configuration/configuration.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonDocument>
#include <QJsonObject>

namespace {

// Current configuration schema version. Bump when incompatible changes are
// made; the loader must then handle older versions explicitly.
constexpr int kCurrentSchemaVersion = 1;

const char* kSchemaVersionKey = "schemaVersion";
const char* kLoggingKey = "logging";
const char* kLoggingLevelKey = "level";
const char* kConnectionKey = "connection";
const char* kConnectionUrlKey = "url";
const char* kConnectionTokenKey = "token";

QJsonObject defaultsObject()
{
    QJsonObject logging;
    logging.insert(QLatin1String(kLoggingLevelKey),
                   QLatin1String("info"));

    QJsonObject connection;
    connection.insert(QLatin1String(kConnectionUrlKey),
                      QLatin1String("ws://127.0.0.1:8080/ws/agent"));
    connection.insert(QLatin1String(kConnectionTokenKey),
                      QLatin1String("dev-token"));

    QJsonObject root;
    root.insert(QLatin1String(kSchemaVersionKey), kCurrentSchemaVersion);
    root.insert(QLatin1String(kLoggingKey), logging);
    root.insert(QLatin1String(kConnectionKey), connection);
    return root;
}

bool writeDefaults(const QString& filePath)
{
    QDir().mkpath(QFileInfo(filePath).absolutePath());
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        return false;
    }
    return file.write(QJsonDocument(defaultsObject()).toJson(
                          QJsonDocument::Indented)) >= 0;
}

} // namespace

namespace configuration {

Configuration& Configuration::instance()
{
    static Configuration configuration;
    return configuration;
}

bool Configuration::load(const QString& filePath)
{
    m_filePath = filePath;

    if (!QFileInfo::exists(filePath)) {
        if (!writeDefaults(filePath)) {
            return false;
        }
    }

    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly)) {
        return false;
    }

    const QJsonDocument document = QJsonDocument::fromJson(file.readAll());
    if (!document.isObject()) {
        return false;
    }

    const QJsonObject root = document.object();
    m_schemaVersion = root.value(QLatin1String(kSchemaVersionKey)).toInt(0);

    const QJsonObject logging = root.value(QLatin1String(kLoggingKey)).toObject();
    m_logging.level =
        logging.value(QLatin1String(kLoggingLevelKey)).toString(
            QLatin1String("info"));

    const QJsonObject connection =
        root.value(QLatin1String(kConnectionKey)).toObject();
    m_connection.url = connection.value(QLatin1String(kConnectionUrlKey))
                           .toString(QStringLiteral("ws://127.0.0.1:8080/ws/agent"));
    m_connection.token =
        connection.value(QLatin1String(kConnectionTokenKey))
            .toString(QStringLiteral("dev-token"));

    return true;
}

QString Configuration::filePath() const
{
    return m_filePath;
}

LoggingSettings Configuration::logging() const
{
    return m_logging;
}

ConnectionSettings Configuration::connection() const
{
    return m_connection;
}

QString defaultConfigFilePath()
{
#ifdef Q_OS_WIN
    return QStringLiteral("%1/SnoopingOwl/config.json").arg(
        qEnvironmentVariable("ProgramData", QStringLiteral("C:/ProgramData")));
#else
    return QDir::homePath() + QStringLiteral("/.config/SnoopingOwl/config.json");
#endif
}

} // namespace configuration