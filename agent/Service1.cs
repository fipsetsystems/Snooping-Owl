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
    private readonly string _wsUrl;
    private bool _isConnected = false;
    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private const int HeartbeatIntervalMs = 30000;
    private const int ReconnectBaseDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;

    public AgentService()
    {
        ServiceName = "SnoopingOwlAgent";
        // Read URL at service construction time (same logic as Program.cs)
        _wsUrl = Program.WsUrl;
    }

    protected override void OnStart(string[] args)
    {
        Console.WriteLine($"{DateTime.UtcNow:O} Agent service starting... URL={_wsUrl}");

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
                ReconnectBaseDelayMs = Math.Min(MaxReconnectDelayMs, ReconnectBaseDelayMs * 2);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken token)
    {
        Console.WriteLine($"{DateTime.UtcNow:O} Connecting to {_wsUrl}...");

        _ws = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(_wsUrl), token);
            _isConnected = true;
            ReconnectBaseDelayMs = 1000; // reset backoff on successful connect
            Console.WriteLine($"{DateTime.UtcNow:O} Connected!");

            // Send initial identity/event
            await SendAsync("{\"type\":\"agent-connect\",\"machineId\":\"placeholder-pc-01\"}");

            // Heartbeat + event loop
            await HeartbeatLoop(token);
        }
        catch (OperationCanceledException)
        {
            throw; // service stopping
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} Failed to connect: {ex.Message}");
            throw;
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
                    // Connection lost - will be handled in ConnectionLoop reconnect logic
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

    public void ReportOnline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent online");
    public void ReportOffline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent offline");
}