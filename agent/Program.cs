using System.ServiceProcess;

namespace SnoopingOwl.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        AgentLog.RegisterCrashHandlers();

        // If /runasconsole passed, run as console app for testing
        if (args.Length > 0 && args[0] == "/runasconsole")
        {
            await RunAsConsole();
            return;
        }

        // Otherwise run as Windows Service
        ServiceBase.Run(new[] { new AgentService() });
    }

    static async Task RunAsConsole()
    {
        AgentLog.Info("Agent console mode started");
        Console.WriteLine($"WSS Endpoint: {Configuration.BackendUrl}");
        Console.WriteLine($"GitHub repo: {Configuration.GitHubRepo}");
        Console.WriteLine("Press Ctrl+C to exit");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => cts.Cancel();

        var svc = new AgentService();
        await svc.StartAsync(cts.Token);
    }
}