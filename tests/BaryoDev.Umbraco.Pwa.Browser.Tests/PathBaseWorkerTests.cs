using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;

namespace BaryoDev.Umbraco.Pwa.Browser.Tests;

/// <summary>
/// The generated worker's path handling at a domain root and under a path base.
/// </summary>
/// <remarks>
/// The worker text is generated for a given path base and run in a page against stand-ins for
/// <c>self</c>, <c>fetch</c> and <c>caches</c>, so the test sees what the worker decides for each
/// URL. Mounting the whole site under a prefix would test the same decision at far more cost.
/// </remarks>
[Collection(LiveSiteCollection.Name)]
public class PathBaseWorkerTests
{
    private const string Harness = """
        async ({ src, paths }) => {
          const listeners = {};
          const calls = [];
          const fakeSelf = {
            addEventListener: (type, fn) => { listeners[type] = fn; },
            location: { origin: location.origin },
            skipWaiting: () => {},
            clients: { claim: () => {} },
          };
          const fakeFetch = (req, init) => {
            calls.push({ url: typeof req === 'string' ? req : req.url, init: init || null });
            return Promise.resolve(new Response('x', { status: 200 }));
          };
          const fakeCaches = {
            open: async () => ({ put: async () => {} }),
            match: async () => undefined,
            keys: async () => [],
            delete: async () => true,
          };
          new Function('self', 'fetch', 'caches', src)(fakeSelf, fakeFetch, fakeCaches);

          let installed;
          listeners.install({ waitUntil: (p) => { installed = p; } });
          await installed;
          const installFetch = calls[0].url;

          const probe = async (path) => {
            calls.length = 0;
            let responded = false;
            listeners.fetch({
              request: { method: 'GET', url: location.origin + path, mode: 'cors' },
              respondWith: () => { responded = true; },
              waitUntil: () => {},
            });
            await new Promise((r) => setTimeout(r, 0));
            return { responded, cacheMode: calls[0]?.init?.cache ?? null };
          };

          const result = { installFetch, paths: {} };
          for (const path of paths) result.paths[path] = await probe(path);
          return JSON.stringify(result);
        }
        """;

    private readonly LiveSiteFixture _site;

    public PathBaseWorkerTests(LiveSiteFixture site) => _site = site;

    [Theory]
    [InlineData("")]
    [InlineData("/app")]
    public async Task The_worker_skips_the_backoffice_and_routes_the_api_under_its_path_base(string pathBase)
    {
        var page = await _site.NewPageAsync();
        await page.GotoAsync(LiveSiteFixture.EntryPage);

        var paths = new[]
        {
            pathBase + "/umbraco/",
            pathBase + "/umbraco",
            pathBase + "/Umbraco/section",
            pathBase + "/api/x",
            pathBase + "/img/a.png",
        };

        var source = await page.EvaluateAsync<string>(
            "async (b) => await (await fetch('/path-base-sw.js?b=' + encodeURIComponent(b))).text()", pathBase);
        var json = JsonDocument.Parse(await page.EvaluateAsync<string>(Harness, new { src = source, paths })).RootElement;

        // The precache goes to the site, not the domain root.
        json.GetProperty("installFetch").GetString().ShouldBe(pathBase + LiveSiteFixture.Fallback);

        var results = json.GetProperty("paths");

        // The backoffice is never handled, so it is never cached.
        results.GetProperty(pathBase + "/umbraco/").GetProperty("responded").GetBoolean().ShouldBeFalse();
        results.GetProperty(pathBase + "/umbraco").GetProperty("responded").GetBoolean().ShouldBeFalse();
        results.GetProperty(pathBase + "/Umbraco/section").GetProperty("responded").GetBoolean().ShouldBeFalse();

        // The API is network-first: fetched with the request as it is, no conditional cache mode.
        var api = results.GetProperty(pathBase + "/api/x");
        api.GetProperty("responded").GetBoolean().ShouldBeTrue();
        api.GetProperty("cacheMode").ValueKind.ShouldBe(JsonValueKind.Null);

        // A plain asset is the control: handled, and refreshed with a conditional request.
        var asset = results.GetProperty(pathBase + "/img/a.png");
        asset.GetProperty("responded").GetBoolean().ShouldBeTrue();
        asset.GetProperty("cacheMode").GetString().ShouldBe("no-cache");
    }
}
