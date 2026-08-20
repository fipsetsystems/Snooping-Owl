using Microsoft.Win32;

namespace SnoopingOwl.Agent;

// Configuration is external (registry + env vars), never hardcoded (rule 25).
// Priority: environment variable > registry > default.
public static class Configuration
{
    private const string RegistryRoot = @"Software\SnoopingOwl";

    public static string BackendUrl => Get("SNOOPINGOWL_WS_URL", "BackendUrl", "ws://localhost:8432/ws");

    public static string GitHubRepo => Get("SNOOPINGOWL_GITHUB_REPO", "GitHubRepo", "fipsetsystems/Snooping-Owl");

    // PAT for private repo access. May be empty -> update checks are skipped.
    public static string GitHubToken => Get("SNOOPINGOWL_GITHUB_TOKEN", "GitHubToken", "");

    public static TimeSpan UpdateInterval => TimeSpan.FromMinutes(GetInt("SNOOPINGOWL_UPDATE_INTERVAL_MIN", "UpdateIntervalMin", 10));

    public static string UpdateDirectory => Path.Combine(AppContext.BaseDirectory, "update");

    private static string Get(string envName, string regName, string fallback)
    {
        var env = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryRoot);
            if (key?.GetValue(regName) is string reg && !string.IsNullOrWhiteSpace(reg))
            {
                return reg.Trim();
            }
        }
        catch
        {
            // Registry unavailable (e.g., console test) - fall back to default
        }

        return fallback;
    }

    private static int GetInt(string envName, string regName, int fallback)
    {
        var raw = Get(envName, regName, fallback.ToString());
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}