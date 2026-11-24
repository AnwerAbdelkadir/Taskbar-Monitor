using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaskbarMonitor;

/// <summary>
/// Checks the latest GitHub release and swaps this exe for it. A running exe cannot
/// be overwritten but it can be renamed, so the old file moves to <c>.old</c> and the
/// downloaded one takes its place before the new instance starts with <c>--replace</c>.
/// </summary>
internal static class UpdateService
{
    private const string Api = "https://api.github.com/repos/AnwerAbdelkadir/Taskbar-Monitor/releases/latest";
    public const string ReleasesUrl = "https://github.com/AnwerAbdelkadir/Taskbar-Monitor/releases/latest";

    public static bool UpdateAvailable { get; private set; }
    public static string Latest { get; private set; } = "";

    private static string? _url;
    private static string? _digest;

    private static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        // The GitHub API answers 403 without a user agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaskbarMonitor/" + AppVersion.Display);
        return http;
    }

    /// <summary>Never throws, so the daily timer can fire and forget it; null means GitHub was unreachable.</summary>
    public static async Task<bool?> CheckAsync()
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(8));
            using var json = JsonDocument.Parse(await http.GetStringAsync(Api));
            var release = json.RootElement;
            var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";

            _url = null;
            _digest = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "TaskbarMonitor.exe") continue;
                _url = asset.GetProperty("browser_download_url").GetString();
                // Older releases carry no digest; the size alone is not worth checking.
                _digest = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
            }

            UpdateAvailable = _url is not null && IsNewer(AppVersion.Display, tag);
            Latest = tag;
            return UpdateAvailable;
        }
        catch (Exception ex)
        {
            Log.Error("update check", ex);
            return null;
        }
    }

    public static bool IsNewer(string current, string candidate) =>
        Version.TryParse(current, out var a) && Version.TryParse(candidate, out var b) && b > a;

    public static async Task InstallAsync(IProgress<(string Text, int Percent)> progress, CancellationToken token)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running exe.");
        if (_url is null) throw new InvalidOperationException("No download for the newer release.");

        var download = exe + ".new";
        var old = exe + ".old";

        try
        {
            // Download beside the exe first: any failure must leave the running copy untouched.
            using (var http = Client(Timeout.InfiniteTimeSpan))
            using (var response = await http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var target = File.Create(download);

                var buffer = new byte[81920];
                long total = 0;
                var reported = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                    if (total / 1048576 != reported)
                    {
                        reported = (int)(total / 1048576);
                        var percent = length > 0 ? (int)(total * 100 / length) : 0;
                        progress.Report(($"Downloading {reported} of {length / 1048576} MB…", percent));
                    }
                }
            }

            if (_digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                progress.Report(("Verifying the download…", 100));
                await using var file = File.OpenRead(download);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!string.Equals(actual, digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The download does not match the release checksum.");
            }

            progress.Report(("Installing and restarting…", 100));
            try { File.Delete(old); } catch { /* the previous update still holds it */ }
            File.Move(exe, old);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                File.Move(old, exe); // restore the original before reporting the failure
                throw;
            }

            // Keep the silent startup quiet; --replace alone reopens the settings window.
            var args = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                ? "--replace --silent"
                : "--replace";
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        }
        catch
        {
            try { File.Delete(download); } catch { }
            throw;
        }
    }
}
