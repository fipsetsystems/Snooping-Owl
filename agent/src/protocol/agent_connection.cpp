#include "protocol/agent_connection.h"

#include "configuration/configuration.h"
#include "identity/device_id.h"

#include <QJsonDocument>
#include <QJsonObject>
#include <QSysInfo>

#ifndef AGENT_VERSION
#define AGENT_VERSION "dev"
#endif

namespace {

constexpr int kHeartbeatIntervalMs = 15'000;
constexpr int kBackoffStartMs = 1'000;
constexpr int kBackoffMaxMs = 30'000;

} // namespace

namespace protocol {

AgentConnection::AgentConnection(QObject* parent)
    : QObject(parent)
{
    m_heartbeatTimer.setInterval(kHeartbeatIntervalMs);
    m_heartbeatTimer.setSingleShot(false);
    m_reconnectTimer.setSingleShot(true);

    connect(&m_socket, &QWebSocket::connected,
            this, &AgentConnection::onConnected);
    connect(&m_socket, &QWebSocket::disconnected,
            this, &AgentConnection::onDisconnected);
    connect(&m_socket, &QWebSocket::errorOccurred,
            this, &AgentConnection::onError);
    connect(&m_socket, &QWebSocket::textMessageReceived,
            this, &AgentConnection::onTextMessageReceived);
    connect(&m_heartbeatTimer, &QTimer::timeout,
            this, &AgentConnection::sendHeartbeat);
    connect(&m_reconnectTimer, &QTimer::timeout,
            this, &AgentConnection::beginConnect);
}

AgentConnection::~AgentConnection()
{
    stop();
}

void AgentConnection::start()
{
    const configuration::ConnectionSettings settings =
        configuration::Configuration::instance().connection();
    m_url = QUrl(settings.url);
    m_token = settings.token;
    m_deviceId = identity::deviceId();
    m_attempts = 0;
    beginConnect();
}

void AgentConnection::stop()
{
    m_heartbeatTimer.stop();
    m_reconnectTimer.stop();
    m_socket.close();
    setState(ConnectionState::Disconnected);
}

ConnectionState AgentConnection::state() const
{
    return m_state;
}

int AgentConnection::attempts() const
{
    return m_attempts;
}

void AgentConnection::beginConnect()
{
    if (m_state != ConnectionState::Disconnected) {
        return;
    }
    ++m_attempts;
    setState(ConnectionState::Connecting);
    qInfo("[connect] connecting to %s (attempt %d)", qPrintable(m_url.toString()),
          m_attempts);
    m_socket.open(m_url);
}

void AgentConnection::onConnected()
{
    setState(ConnectionState::Connected);
    qInfo("[connect] connected to %s", qPrintable(m_url.toString()));

    QJsonObject hello;
    hello.insert(QLatin1String("v"), 1);
    hello.insert(QLatin1String("type"), QLatin1String("hello"));
    hello.insert(QLatin1String("token"), m_token);
    hello.insert(QLatin1String("deviceId"), m_deviceId);
    hello.insert(QLatin1String("agentVersion"),
                 QStringLiteral(AGENT_VERSION));
    hello.insert(QLatin1String("os"), QSysInfo::kernelType());
    sendJson(hello);

    m_heartbeatSeq = 0;
    m_heartbeatTimer.start();
}

void AgentConnection::onDisconnected()
{
    const bool wasActive = m_state == ConnectionState::Connected
        || m_state == ConnectionState::Connecting;
    m_heartbeatTimer.stop();
    setState(ConnectionState::Disconnected);
    if (!wasActive) {
        return;
    }

    const int backoff = backoffMs();
    qInfo("[connect] disconnected; retrying in %d ms", backoff);
    m_reconnectTimer.start(backoff);
}

void AgentConnection::onError(QAbstractSocket::SocketError error)
{
    Q_UNUSED(error);
    qWarning("[connect] socket error: %s",
             qPrintable(m_socket.errorString()));
    // QWebSocket emits disconnected after a failed open; reconnection is
    // scheduled there.
}

void AgentConnection::onTextMessageReceived(const QString& message)
{
    const QJsonDocument document = QJsonDocument::fromJson(message.toUtf8());
    const QJsonObject object = document.object();
    if (object.value(QLatin1String("v")).toInt() != 1) {
        qWarning("[connect] ignoring message with unsupported protocol version");
        return;
    }

    const QString type = object.value(QLatin1String("type")).toString();
    if (type == QLatin1String("hello_ack")) {
        qInfo("[connect] server acknowledged registration (device %s)",
              qPrintable(m_deviceId));
    } else if (type == QLatin1String("heartbeat_ack")) {
        qDebug("[connect] heartbeat acknowledged (seq %d)",
               object.value(QLatin1String("seq")).toInt());
    } else {
        qWarning("[connect] unexpected server message type: %s", qPrintable(type));
    }
}

void AgentConnection::sendHeartbeat()
{
    if (m_state != ConnectionState::Connected) {
        return;
    }
    ++m_heartbeatSeq;

    QJsonObject heartbeat;
    heartbeat.insert(QLatin1String("v"), 1);
    heartbeat.insert(QLatin1String("type"), QLatin1String("heartbeat"));
    heartbeat.insert(QLatin1String("seq"), m_heartbeatSeq);
    sendJson(heartbeat);
}

int AgentConnection::backoffMs() const
{
    int backoff = kBackoffStartMs;
    for (int i = 1; i < m_attempts; ++i) {
        backoff *= 2;
        if (backoff >= kBackoffMaxMs) {
            return kBackoffMaxMs;
        }
    }
    return backoff;
}

void AgentConnection::setState(ConnectionState state)
{
    if (m_state == state) {
        return;
    }
    m_state = state;
    emit stateChanged(state);
}

void AgentConnection::sendJson(const QJsonObject& object)
{
    m_socket.sendTextMessage(
        QString::fromUtf8(QJsonDocument(object).toJson(QJsonDocument::Compact)));
}

AgentConnection* startAgentConnection(QObject* parent)
{
    auto* connection = new AgentConnection(parent);
    connection->start();
    return connection;
}

} // namespace protocol