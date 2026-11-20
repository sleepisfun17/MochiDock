using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MochiDock.Core;

namespace MochiDock.Services;

public enum UpdateCheckState { UpToDate, Available, Failed }

public sealed record UpdateCheckResult(UpdateCheckState State, UpdateOffer? Offer, string Message);

/// <summary>
/// Asks GitHub whether a newer MochiDock release exists. Read-only: it sends no identifiers (only a generic User-Agent) and never
/// downloads or runs anything; the user opens the release page and runs the new setup themselves.
/// </summary>
public sealed class UpdateCheckService
{
    public const string FeedUrl = "https://api.github.com/repos/sleepisfun17/MochiDock/releases?per_page=10";

    private readonly HttpClient _http;

    public UpdateCheckService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (http is null)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MochiDock");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
    }

    /// <summary>The running build's version, from the assembly (set by <c>&lt;Version&gt;</c> in the project file).</summary>
    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ReleaseVersion.TryParse(info, out var v) ? v.ToString() : "0.0.0";
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(FeedUrl, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(UpdateCheckState.Failed, null, "Could not check: no public releases found yet.");
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckState.Failed, null, "Could not check for updates right now.");
            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var offer = UpdateFeed.Newest(json, currentVersion);
            return offer is null
                ? new UpdateCheckResult(UpdateCheckState.UpToDate, null, "You have the latest version.")
                : new UpdateCheckResult(UpdateCheckState.Available, offer, $"Version {offer.Version} is available.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Warn("Update", ex);
            return new UpdateCheckResult(UpdateCheckState.Failed, null, "Could not check for updates right now.");
        }
    }
}
