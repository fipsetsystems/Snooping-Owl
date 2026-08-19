using Microsoft.Win32.SafeHandles;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnoopingOwl.Agent;

public class AgentService : ServiceBase
{
    private readonly ManualResetEvent _runCompleteEvent = new(false);
    private WebSocket? _ws;
    private readonly CancellationTokenSource _cts = new();
    private bool _isConnected = false;
    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private const int HeartbeatIntervalMs = 30000;
    private const int ReconnectBaseDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;

    public AgentService()
    {
        // Name for Services MMC snap-in
        ServiceName = "SnoopingOwlAgent";
    }

    protected override void OnStart(string[] args)
    {
        // Log start event (simple console output; in production use Event Log)
        Console.WriteLine($"{DateTime.UtcNow:O} Agent service starting...");

        // Start the connection loop on a thread pool thread
        _ = Task.Run(() => ConnectionLoop(_cts.Token));

        base.OnStart(args);
    }

    protected override void OnStop()
    {
        Console.WriteLine($"{DateTime.UtcNow:O} Agent service stopping...");
        _cts.Cancel();
        _runCompleteEvent.WaitOne();
        base.OnStop();
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
                // Service is stopping, exit
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} Connection error: {ex.Message}");
            }

            // Reconnect with backoff unless stopping
            if (!token.IsCancellationRequested)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} Reconnecting in {ReconnectBaseDelayMs}ms...");
                await Task.Delay(ReconnectBaseDelayMs, token);
                // Increase delay for next time, capped at MaxReconnectDelayMs
                ReconnectBaseDelayMs = Math.Min(MaxReconnectDelayMs, ReconnectBaseDelayMs * 2);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken token)
    {
        var uri = "wss://localhost:8432/ws"; // TODO: configure from appsettings
        _ws = new ClientWebSocket();

        Console.WriteLine($"{DateTime.UtcNow:O} Connecting to {uri}...");

        try
        {
            await _ws.ConnectAsync(new Uri(uri), token);
            _isConnected = true;
            ReconnectBaseDelayMs = 1000; // reset backoff on successful connect
            Console.WriteLine($"{DateTime.UtcNow:O} Connected!");

            // Send initial identity/event
            await SendAsync("{\"type\":\"agent-connect\",\"machineId\":\"placeholder\"}");

            // Heartbeat + event loop
            await HeartbeatLoop(token);
        }
        catch (WebSocketException wsEx) when (wsEx.WebSocketError == System.Net.WebSockets.WebSocketError.SocketError)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} WebSocket error: {wsEx.Message}");
            throw;
        }
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        var sendBuffer = Encoding.UTF8.GetBytes("{\"type\":\"heartbeat\"}");

        while (!token.IsCancellationRequested)
        {
            // Check if we need to send heartbeat
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastHeartbeat).TotalMilliseconds;

            if (elapsed >= HeartbeatIntervalMs)
            {
                try
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(sendBuffer),
                        WebSocketMessageType.Text,
                        true,
                        token);
                    _lastHeartbeat = now;
                    Console.WriteLine($"{DateTime.UtcNow:O} Heartbeat sent");
                }
                catch
                {
                    // Connection lost, will be detected in ConnectionLoop
                    throw;
                }
            }
            else
            {
                // Small sleep to avoid busy-wait
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
            token);
    }

    // For testing/console mode: manually trigger a state change
    public void ReportOnline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent online");
    public void ReportOffline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent offline");
}