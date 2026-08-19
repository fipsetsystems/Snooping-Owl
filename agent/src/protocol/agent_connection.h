#pragma once

#include <QAbstractSocket>
#include <QObject>
#include <QTimer>
#include <QUrl>
#include <QWebSocket>

namespace protocol {

// Connection lifecycle states; transitions are emitted as stateChanged.
enum class ConnectionState
{
    Disconnected, // no connection, retrying (or stopped)
    Connecting,   // socket open in progress
    Connected,    // handshake accepted (hello_ack received)
};

// WebSocket link to the SnoopingOwl server, per protocol v1
// (docs/protocol.md). Owns reconnection with exponential backoff and
// periodic heartbeats.
class AgentConnection : public QObject
{
    Q_OBJECT

public:
    explicit AgentConnection(QObject* parent = nullptr);
    ~AgentConnection() override;

    // Reads the endpoint from the agent configuration and begins the
    // connect/reconnect cycle.
    void start();

    // Closes the socket and cancels reconnection and heartbeats.
    void stop();

    ConnectionState state() const;
    int attempts() const;

signals:
    void stateChanged(ConnectionState state);

private slots:
    void onConnected();
    void onDisconnected();
    void onError(QAbstractSocket::SocketError error);
    void onTextMessageReceived(const QString& message);
    void sendHeartbeat();
    void beginConnect();

private:
    int backoffMs() const;
    void setState(ConnectionState state);
    void sendJson(const QJsonObject& object);

    QWebSocket m_socket;
    QTimer m_heartbeatTimer;
    QTimer m_reconnectTimer;
    QUrl m_url;
    QString m_token;
    QString m_deviceId;
    ConnectionState m_state = ConnectionState::Disconnected;
    int m_attempts = 0;
    int m_heartbeatSeq = 0;
};

// Creates a started connection bound to the agent's configuration.
// The parent owns it; the agent stops it when the application quits.
AgentConnection* startAgentConnection(QObject* parent);

} // namespace protocol