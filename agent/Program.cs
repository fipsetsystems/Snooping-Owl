using Microsoft.Win32.SafeHandles;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnoopingOwl.Agent;

public class Program
{
    private static readonly string WsUrl = Environment.GetEnvironmentVariable("SNOOPINGOWL_WS_URL")
        ?? "wss://localhost:8432/ws";

    public static async Task Main(string[] args)
    {
        // If /runasconsole passed, run as console app for testing
        if (args.Length > 0 && args[0] == "/runasconsole")
        {
            await RunAsConsole();
            return;
        }

        // Otherwise run as Windows Service
        var servicesToRun = new[] { new AgentService() };
        ServiceBase.Run(servicesToRun);
    }

    static async Task RunAsConsole()
    {
        Console.WriteLine("Running SnoopingOwl Agent as console (testing mode)");
        Console.WriteLine($"Using WSS endpoint: {WsUrl}");
        Console.WriteLine("Press Ctrl+C to exit");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => cts.Cancel();

        var svc = new AgentService();
        await svc.StartAsync(cts.Token);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
}