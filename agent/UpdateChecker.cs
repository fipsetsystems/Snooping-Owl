using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SnoopingOwl.Agent;

// Polls GitHub Releases for the latest version every UpdateInterval.
// If newer: downloads the exe, verifies SHA-256 from the release body,
// then spawns the updater helper which swaps the exe and restarts the service.
// No admin needed: the service runs as LocalSystem, updater runs as the same user.
public sealed class UpdateChecker
{
    private const string AssetName = "SnoopingOwl.Agent.exe";
    private const string UpdaterName = "SnoopingOwl.Updater.exe";

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();

    public UpdateChecker()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(Configuration.GitHubToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Configuration.GitHubToken);
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SnoopingOwl-Agent");
    }

    public Task RunLoopAsync(CancellationToken token)
    {
        return Task.Run(() => Loop(token), CancellationToken.None);
    }

    // Triggered by the server's force-update signal over the WSS channel.
    public void TriggerImmediate()
    {
        AgentLog.Info("Force-update signal received; checking now.");
        _ = Task.Run(() => CheckAndUpdateAsync(_cts.Token), CancellationToken.None);
    }

    private async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await CheckAndUpdateAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Update check failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(Configuration.UpdateInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAndUpdateAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Configuration.GitHubToken))
        {
            AgentLog.Info("GitHub token not configured - update checks disabled.");
            return;
        }

        var (tag, body) = await FetchLatestReleaseAsync(token);
        if (tag == null)
        {
            AgentLog.Warn("No latest release found on GitHub.");
            return;
        }

        AgentLog.Info($"Latest GitHub release: {tag} (local: {VersionInfo.Current})");

        if (!VersionInfo.IsNewer(tag))
        {
            return; // up to date
        }

        AgentLog.Info($"Update available: {tag}. Downloading...");
        var expectedHash = ExtractSha256(body);
        var exePath = await DownloadAgentAsync(token);
        if (exePath == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            AgentLog.Error("No SHA-256 in release body - refusing to apply unverifiable update (rule 30).");
            TryDelete(exePath);
            return;
        }

        if (!VerifyHash(exePath, expectedHash))
        {
            AgentLog.Error($"Hash mismatch for {exePath} - refusing to apply update (rule 30).");
            TryDelete(exePath);
            return;
        }

        ApplyUpdate(exePath);
    }

    private async Task<(string? Tag, string? Body)> FetchLatestReleaseAsync(CancellationToken token)
    {
        var url = $"https://api.github.com/repos/{Configuration.GitHubRepo}/releases/latest";
        using var response = await _http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
        {
            AgentLog.Warn($"GitHub API returned {(int)response.StatusCode} for {url}");
            return (null, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
        return (tag, body);
    }

    private async Task<string?> DownloadAgentAsync(CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(Configuration.UpdateDirectory);
            var dest = Path.Combine(Configuration.UpdateDirectory, AssetName);
            var url = $"https://github.com/{Configuration.GitHubRepo}/releases/latest/download/{AssetName}";

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            await using var file = File.Create(dest);
            await stream.CopyToAsync(file, token);
            return dest;
        }
        catch (Exception ex)
        {
            AgentLog.Error($"Download failed: {ex.Message}", ex);
            return null;
        }
    }

    private static bool VerifyHash(string path, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            AgentLog.Warn("No SHA-256 in release body - cannot verify update integrity.");
            return false;
        }

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractSha256(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var marker = "SHA-256 of `SnoopingOwl.Agent.exe`: `";
        var idx = body.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + marker.Length;
        var end = body.IndexOf('`', start);
        return end > start ? body[start..end].Trim() : null;
    }

    private static void ApplyUpdate(string newExePath)
    {
        var appDir = AppContext.BaseDirectory;
        var updater = Path.Combine(appDir, UpdaterName);
        var backup = Path.Combine(appDir, "SnoopingOwl.Agent.exe.bak");

        if (!File.Exists(updater))
        {
            AgentLog.Error($"Updater not found at {updater} - update aborted.");
            TryDelete(newExePath);
            return;
        }

        var args = $"\"{newExePath}\" \"{Path.Combine(appDir, "SnoopingOwl.Agent.exe")}\" \"{backup}\" \"SnoopingOwlAgent\"";
        AgentLog.Info($"Spawning updater: {updater} {args}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(updater, args)
            {
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLog.Error($"Failed to start updater: {ex.Message}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}