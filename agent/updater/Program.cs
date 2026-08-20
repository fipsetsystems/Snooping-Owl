using System.Diagnostics;

namespace SnoopingOwl.Updater;

// SnoopingOwl.Updater.exe - standalone update helper.
// Spawned by the agent (runs as LocalSystem, so NO admin needed):
//   1. waits for the agent service to stop (releases the exe lock)
//   2. backs up the current exe, copies the new one in place
//   3. starts the service again
//   4. watches for an immediate crash (stability window); if the service
//      dies, rolls back to the backup and restarts it.
// Usage: SnoopingOwl.Updater.exe <newExePath> <currentExePath> <backupPath> <serviceName>
public static class Program
{
    private const int StabilitySeconds = 30;

    public static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Log("ERROR: usage: <newExe> <currentExe> <backup> <serviceName>");
            return 1;
        }

        var newExe = args[0];
        var currentExe = args[1];
        var backup = args[2];
        var serviceName = args[3];

        try
        {
            if (!File.Exists(newExe))
            {
                Log($"ERROR: new exe not found: {newExe}");
                return 1;
            }

            StopService(serviceName);
            WaitForFileUnlock(currentExe);

            // Backup current exe for rollback
            if (File.Exists(currentExe))
            {
                File.Copy(currentExe, backup, overwrite: true);
                Log($"Backed up current exe to {backup}");
            }

            File.Copy(newExe, currentExe, overwrite: true);
            Log($"Replaced {currentExe}");

            StartService(serviceName);

            if (!WaitForStableRunning(serviceName, TimeSpan.FromSeconds(StabilitySeconds)))
            {
                Log("Service did not stay running - rolling back to previous version.");
                StopService(serviceName);
                WaitForFileUnlock(currentExe);
                if (File.Exists(backup))
                {
                    File.Copy(backup, currentExe, overwrite: true);
                }
                StartService(serviceName);
                Log("Rollback complete. Service restarted with previous version.");
                return 2;
            }

            TryDelete(newExe);
            TryDelete(backup);
            Log("Update applied successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"ERROR: update failed: {ex}");
            return 1;
        }
    }

    private static void StopService(string name)
    {
        var result = RunSc($"stop {name}");
        Log($"sc stop: {result}");
    }

    private static void StartService(string name)
    {
        var result = RunSc($"start {name}");
        Log($"sc start: {result}");
    }

    private static string RunSc(string arguments)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
        return $"(exit {p.ExitCode}) {p.StandardOutput.ReadToEnd().Trim()} {p.StandardError.ReadToEnd().Trim()}";
    }

    private static bool WaitForStableRunning(string name, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        var wasRunning = false;
        var runningSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            var state = QueryState(name);
            if (state == "RUNNING")
            {
                if (!wasRunning)
                {
                    wasRunning = true;
                    runningSince = DateTime.UtcNow;
                    Log("Service reached RUNNING state.");
                }
                if (DateTime.UtcNow - runningSince >= TimeSpan.FromSeconds(5))
                {
                    return true; // stable for 5s = not an immediate crash
                }
            }
            else
            {
                if (wasRunning)
                {
                    Log($"Service left RUNNING state ({state}) - treating as crash.");
                    return false;
                }
            }
            Thread.Sleep(2000);
        }

        Log($"Service never reached stable RUNNING within {window.TotalSeconds}s (last state: {QueryState(name)}).");
        return false;
    }

    private static string QueryState(string name)
    {
        var psi = new ProcessStartInfo("sc.exe", $"query {name}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(15_000);
        var output = p.StandardOutput.ReadToEnd();
        var line = output.Split('\n').FirstOrDefault(l => l.Contains("STATE", StringComparison.OrdinalIgnoreCase));
        return line?.Trim() ?? "UNKNOWN";
    }

    private static void WaitForFileUnlock(string path)
    {
        for (var i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    return; // lock acquired = file not in use anymore
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(1000); // still locked, keep waiting
            }
        }
        Log("WARNING: could not acquire exe lock within timeout.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log($"WARNING: could not delete {path}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "updater.log"),
                $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // never crash the updater on logging failure
        }
    }
}