using Microsoft.Win32.SafeHandles;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnoopingOwl.Agent;

public class Program
{
    // Priority: env var > registry (HKLM\Software\SnoopingOwl\BackendUrl) > default
    private static readonly string WsUrl = Environment.GetEnvironmentVariable("SNOOPINGOWL_WS_URL")
        ?.Trim()
        ?? GetBackendFromRegistry()
        ?? "wss://YOUR_CF_TUNNEL_ID.trycloudflare.com/ws";

    private static string GetBackendFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\SnoopingOwl");
            if (key != null && key.GetValue("BackendUrl") is string url)
            {
                return url;
            }
        }
        catch
        {
            // Registry not available (e.g., running as console) - fall through to default
        }
        return null;
    }

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
        Console.WriteLine($"SnoopingOwl Agent - Console Mode");
        Console.WriteLine($"WSS Endpoint: {WsUrl}");
        Console.WriteLine("Press Ctrl+C to exit");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => cts.Cancel();

        var svc = new AgentService();
        await svc.StartAsync(cts.Token);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
}