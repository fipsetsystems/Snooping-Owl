using System;
using System.Net.WebSockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnoopingOwl.Agent;

public class AgentService : ServiceBase
{
    private ClientWebSocket? _ws;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _wsUrl;
    private int _reconnectDelayMs = 1000;
    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private const int HeartbeatIntervalMs = 30000;
    private const int MaxReconnectDelayMs = 30000;

    public AgentService()
    {
        ServiceName = "SnoopingOwlAgent";
        _wsUrl = Program.WsUrl;
    }

    protected override void OnStart(string[] args)
    {
        Console.WriteLine($"{DateTime.UtcNow:O} Agent service starting... URL={_wsUrl}");
        _ = Task.Run(() => ConnectionLoop(_cts.Token));
        base.OnStart(args);
    }

    protected override void OnStop()
    {
        Console.WriteLine($"{DateTime.UtcNow:O} Agent service stopping...");
        _cts.Cancel();
        base.OnStop();
    }

    public async Task StartAsync(CancellationToken token)
    {
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
                Console.WriteLine($"{DateTime.UtcNow:O} Connection error: {ex.Message}");
            }

            if (!token.IsCancellationRequested)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} Reconnecting in {_reconnectDelayMs}ms...");
                await Task.Delay(_reconnectDelayMs, token);
                _reconnectDelayMs = Math.Min(MaxReconnectDelayMs, _reconnectDelayMs * 2);
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
            _reconnectDelayMs = 1000;
            Console.WriteLine($"{DateTime.UtcNow:O} Connected!");

            await SendAsync("{\"type\":\"agent-connect\",\"machineId\":\"placeholder-pc-01\"}");
            await HeartbeatLoop(token);
        }
        catch (OperationCanceledException)
        {
            throw;
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
                    Console.WriteLine($"{DateTime.UtcNow:O} Heartbeat sent");
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

    public void ReportOnline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent online");
    public void ReportOffline() => Console.WriteLine($"[{DateTime.UtcNow:O}] Agent offline");
}