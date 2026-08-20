using System.Diagnostics;

namespace SnoopingOwl.Agent;

// Logs answer WHAT/WHEN/WHERE/WHY (rule 24). Event Log for services,
// rolling file log for detail. Never logs secrets (rule 24/9).
public static class AgentLog
{
    private const string SourceName = "SnoopingOwlAgent";
    private const long MaxLogBytes = 1_000_000;

    private static readonly object Sync = new();
    private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");
    private static string LogFile => Path.Combine(LogDir, "agent.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} :: {ex}");

    public static void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error("Unhandled exception", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        WriteFile(line);
        WriteEventLog(level, message);
    }

    private static void WriteFile(string line)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile, line + Environment.NewLine);
                if (new FileInfo(LogFile).Length > MaxLogBytes)
                {
                    File.Move(LogFile, LogFile + ".old", overwrite: true);
                }
            }
        }
        catch
        {
            // Logging must never take the agent down.
        }
    }

    private static void WriteEventLog(string level, string message)
    {
        try
        {
            if (!EventLog.SourceExists(SourceName))
            {
                EventLog.CreateEventSource(SourceName, "Application");
            }
            var type = level == "ERROR" ? EventLogEntryType.Error
                     : level == "WARN" ? EventLogEntryType.Warning
                     : EventLogEntryType.Information;
            EventLog.WriteEntry(SourceName, message, type);
        }
        catch
        {
            // Event log unavailable (console test) - file log is enough.
        }
    }
}