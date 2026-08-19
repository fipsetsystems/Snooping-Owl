#include "identity/device_id.h"

#include "configuration/configuration.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QRandomGenerator>
#include <QSysInfo>

namespace {

QString machineIdHex()
{
    const QByteArray id = QSysInfo::machineUniqueId();
    if (id.isEmpty()) {
        return {};
    }
    return QString::fromLatin1(id.toHex());
}

QString randomId()
{
    QByteArray bytes(16, Qt::Uninitialized);
    QRandomGenerator::system()->fillRange(
        reinterpret_cast<quint32*>(bytes.data()),
        static_cast<int>(bytes.size() / sizeof(quint32)));
    return QString::fromLatin1(bytes.toHex());
}

QString persistencePath()
{
    return QFileInfo(configuration::defaultConfigFilePath()).absolutePath()
        + QStringLiteral("/device.id");
}

} // namespace

namespace identity {

QString deviceId()
{
    const QString path = persistencePath();

    QFile file(path);
    if (file.exists() && file.open(QIODevice::ReadOnly)) {
        const QString existing = QString::fromLatin1(file.readAll()).trimmed();
        if (!existing.isEmpty()) {
            return existing;
        }
    }

    QString id = machineIdHex();
    if (id.isEmpty()) {
        id = randomId();
    }

    QDir().mkpath(QFileInfo(path).absolutePath());
    if (file.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        file.write(id.toLatin1());
    }
    return id;
}

} // namespace identity