using System.Net.WebSockets;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace SnoopingOwl.Agent;

public class AgentService : ServiceBase
{
    private ClientWebSocket? _ws;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _wsUrl;
    private int _reconnectDelayMs = 1000;
    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private UpdateChecker? _updateChecker;

    private const int HeartbeatIntervalMs = 30000;
    private const int MaxReconnectDelayMs = 30000;

    public AgentService()
    {
        ServiceName = "SnoopingOwlAgent";
        _wsUrl = Configuration.BackendUrl;
        _updateChecker = new UpdateChecker();
    }

    protected override void OnStart(string[] args)
    {
        AgentLog.Info($"Agent service starting... URL={_wsUrl}");

        _ = Task.Run(() => StartAsync(_cts.Token));
        base.OnStart(args);
    }

    protected override void OnStop()
    {
        AgentLog.Info("Agent service stopping...");
        _cts.Cancel();
        base.OnStop();
    }

    public async Task StartAsync(CancellationToken token)
    {
        _ = _updateChecker!.RunLoopAsync(token);
        await ConnectionLoop(token);
    }

    private async Task ConnectionLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AgentLog.Error($"Connection error: {ex.Message}", ex);
            }

            if (!token.IsCancellationRequested)
            {
                AgentLog.Warn($"Reconnecting in {_reconnectDelayMs}ms...");
                await Task.Delay(_reconnectDelayMs, token);
                _reconnectDelayMs = Math.Min(MaxReconnectDelayMs, _reconnectDelayMs * 2);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken token)
    {
        AgentLog.Info($"Connecting to {_wsUrl}...");
        _ws = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(_wsUrl), token);
            _reconnectDelayMs = 1000;
            AgentLog.Info("Connected!");

            await SendAsync("{\"type\":\"agent-connect\",\"machineId\":\"placeholder-pc-01\"}");

            var receive = ReceiveLoopAsync(token);
            await HeartbeatLoop(token);
            await receive;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentLog.Error($"Failed to connect: {ex.Message}", ex);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            }
            catch (WebSocketException)
            {
                throw; // connection lost - outer loop reconnects
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(WebSocketError.NotAWebSocket, "Server closed the connection");
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandleMessage(text);
        }
    }

    private void HandleMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                return;
            }

            switch (typeEl.GetString())
            {
                case "update-available":
                    _updateChecker?.TriggerImmediate();
                    break;
                default:
                    AgentLog.Info($"Unknown server message type: {typeEl.GetString()}");
                    break;
            }
        }
        catch (JsonException)
        {
            AgentLog.Warn("Malformed JSON from server - ignored.");
        }
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        var sendBuffer = Encoding.UTF8.GetBytes("{\"type\":\"heartbeat\"}");

        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastHeartbeat).TotalMilliseconds;

            if (elapsed >= HeartbeatIntervalMs)
            {
                if (_ws == null || _ws.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not open");
                }

                try
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(sendBuffer),
                        WebSocketMessageType.Text,
                        true,
                        token);
                    _lastHeartbeat = now;
                }
                catch
                {
                    throw;
                }
            }
            else
            {
                await Task.Delay(1000, token);
            }
        }
    }

    private async Task SendAsync(string message)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            return;

        var buffer = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(
            new ArraySegment<byte>(buffer),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }
}