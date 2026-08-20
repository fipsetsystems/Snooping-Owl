using System.Reflection;

namespace SnoopingOwl.Agent;

// Single source of truth for the agent's own version (read from the
// assembly version embedded at build time by -p:Version=...).
public static class VersionInfo
{
    public static string Current => GetFileVersion();

    // Returns 1 if tag is newer than current, -1 if older, 0 if equal.
    public static int CompareToTag(string tag)
    {
        var tagVersion = TryParseTag(tag);
        if (tagVersion == null)
        {
            return 0; // unparseable tag - do not act on it
        }

        return tagVersion.CompareTo(CurrentVersion());
    }

    public static bool IsNewer(string tag) => CompareToTag(tag) > 0;

    private static string GetFileVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static Version CurrentVersion()
    {
        return Version.TryParse(Current, out var v) ? v : new Version(0, 0, 0);
    }

    private static Version? TryParseTag(string tag)
    {
        var cleaned = tag.Trim().TrimStart('v', 'V');
        var cut = cleaned.IndexOf('-');
        if (cut >= 0)
        {
            cleaned = cleaned[..cut]; // drop prerelease suffix
        }

        return Version.TryParse(cleaned, out var parsed) ? parsed : null;
    }
}