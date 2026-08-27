using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Playwright;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// A real Umbraco on a real socket, with a real browser pointed at it.
/// </summary>
/// <remarks>
/// The rest of the suite asserts on the text of the generated service worker, which is why three
/// behavioural defects reached a release: the strings were all present and correct while the
/// behaviour was wrong. A service worker cannot register against WebApplicationFactory's
/// in-memory TestServer, so proving behaviour means an out-of-process host.
///
/// Plain HTTP is fine and TLS is not needed: Chromium treats 127.0.0.1 as a trustworthy origin,
/// which is the only condition service worker registration actually cares about.
/// </remarks>
public class LiveSiteFixture : IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), $"pwa-browser-{Guid.NewGuid():N}");

    private readonly List<string> _siteOutput = [];
    private readonly Lock _outputLock = new();

    private Process? _site;
    private IPlaywright? _playwright;
    private LocalForwardingProxy? _proxy;

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_outputLock)
        {
            // Bounded: Umbraco is chatty and the interesting part of a failed boot is the end.
            if (_siteOutput.Count > 400) _siteOutput.RemoveAt(0);
            _siteOutput.Add(line);
        }
    }

    private string RecentOutput()
    {
        lock (_outputLock) return string.Join(Environment.NewLine, _siteOutput.TakeLast(40));
    }

    public IBrowser Browser { get; private set; } = default!;

    public string BaseUrl { get; private set; } = default!;

    /// <summary>The cache the worker is configured to use, so a test can look inside it.</summary>
    public const string ShellCache = "browsertest-shell-bt1";

    /// <summary>What the worker falls back to offline. Never navigated to by any test.</summary>
    public const string Fallback = "/demo.html";

    /// <summary>The page tests load to get the worker installed. Not the fallback.</summary>
    public const string EntryPage = "/test-entry";

    /// <summary>
    /// The page hosting the dashboard element. Deliberately separate from <see cref="EntryPage"/>:
    /// that one exists to be a navigation target the service worker has never seen, so pointing the
    /// dashboard tests at it too would couple two suites with opposite requirements to one constant.
    /// </summary>
    public const string DashboardPage = "/dashboard-preview.html";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        _site = StartSite(port);
        await WaitUntilServing();

        _proxy = new LocalForwardingProxy();
        await _proxy.StartAsync();

        // Idempotent, and quick when the browser is already there. Doing it here rather than in a
        // CI step keeps `dotnet test` the single command that runs the suite anywhere.
        var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0) throw new InvalidOperationException($"Could not install chromium (exit {exit}).");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Proxy = new Proxy { Server = _proxy.Server, Bypass = string.Empty },
        });
    }

    /// <summary>A page with no worker and no caches, so each test starts from a cold install.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();
        return page;
    }

    public void DisableNetwork() => _proxy!.ForwardingEnabled = false;

    public void EnableNetwork() => _proxy!.ForwardingEnabled = true;

    private Process StartSite(int port)
    {
        var meta = typeof(LiveSiteFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value ?? string.Empty);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = meta["TestSiteDirectory"],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(meta["TestSiteDirectory"]);
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add(meta["TestSiteConfiguration"]);
        // Built already, as a project reference of this one. Rebuilding here would race the
        // outputs the test host is running from.
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-launch-profile");
        start.ArgumentList.Add($"-p:UmbracoVersion={meta["TestSiteUmbracoVersion"]}");

        var env = start.Environment;
        env["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        env["ASPNETCORE_ENVIRONMENT"] = "Development";

        var dbPath = Path.Combine(_dataDirectory, "Umbraco.sqlite.db");
        env["ConnectionStrings__umbracoDbDSN"] =
            $"Data Source={dbPath};Cache=Shared;Foreign Keys=True;Pooling=True";
        env["ConnectionStrings__umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite";

        env["Umbraco__CMS__Unattended__InstallUnattended"] = "true";
        env["Umbraco__CMS__Unattended__UnattendedUserName"] = "Test Admin";
        env["Umbraco__CMS__Unattended__UnattendedUserEmail"] = "test@example.com";
        env["Umbraco__CMS__Unattended__UnattendedUserPassword"] = "LocalOnly-ChangeMe-1234!";

        env["BaryoDev__Pwa__Manifest__Name"] = "Browser Fixture";
        env["BaryoDev__Pwa__Manifest__StartUrl"] = "/";
        env["BaryoDev__Pwa__ServiceWorker__CachePrefix"] = "browsertest";
        env["BaryoDev__Pwa__ServiceWorker__Version"] = "bt1";
        // A static file rather than "/", deliberately. A freshly installed Umbraco with no
        // published content does not reliably serve the root, and a fallback that will not fetch
        // is correctly not cached, so the tests would fail for a reason that is not the worker's.
        env["BaryoDev__Pwa__ServiceWorker__NavigationFallback"] = Fallback;

        var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the test site.");

        // Kept, not discarded. The pipe has to be drained or a full buffer deadlocks the child
        // once Umbraco starts logging, and the first version of this threw the output away, which
        // made a host that would not start impossible to diagnose from the test failure alone.
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>Umbraco cold-boots and runs its migrations, which is slow on a CI agent.</summary>
    private async Task WaitUntilServing()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddMinutes(4);

        while (DateTime.UtcNow < deadline)
        {
            if (_site!.HasExited)
                throw new InvalidOperationException(
                    $"The test site exited early with {_site.ExitCode}.{Environment.NewLine}{RecentOutput()}");

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/sw.js");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(1000);
        }

        throw new TimeoutException(
            $"The test site never served {BaseUrl}/sw.js.{Environment.NewLine}{RecentOutput()}");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_proxy is not null) await _proxy.DisposeAsync();

        if (_site is { HasExited: false })
        {
            // The tree, not the process: `dotnet run` launches the host as a child, and killing
            // only the launcher leaves a live Umbraco holding the port and the database file.
            _site.Kill(entireProcessTree: true);
            await _site.WaitForExitAsync();
        }
        _site?.Dispose();

        try { Directory.Delete(_dataDirectory, recursive: true); }
        catch { /* a locked SQLite file should not fail an otherwise green run */ }
    }
}

[CollectionDefinition(Name)]
public class LiveSiteCollection : ICollectionFixture<LiveSiteFixture>
{
    public const string Name = "live-site";
}
