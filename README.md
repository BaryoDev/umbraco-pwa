# PWA for Umbraco

Turn an Umbraco site into an installable, offline-capable app, and see who installed it **from the
backoffice you are already signed into**.

Free, MIT, and self-hosted. The install data lands in your own database and never leaves your
server, so there is no third-party dashboard to log into and no processor to declare.

## Install

```sh
dotnet add package BaryoDev.Umbraco.Pwa
```

That is the whole installation. On the next start the package creates its table, registers its
endpoints, and adds a **PWA** dashboard to the Settings section.

Then add two lines to your site layout:

```html
<link rel="manifest" href="/manifest.webmanifest" />
<script src="/baryodev-pwa.js" defer></script>
```

The script registers the service worker and reports install status. There is no build step, no npm
dependency and no JavaScript to write.

## Configure

Everything has a working default. A site that adds nothing to `appsettings.json` still gets a
manifest, a service worker and install tracking.

```jsonc
{
  "BaryoDev": {
    "Pwa": {
      "Manifest": {
        "Name": "Contoso",
        "ShortName": "Contoso",
        "ThemeColor": "#1b1721",
        "Icons": [
          { "Src": "/media/icon-192.png", "Sizes": "192x192", "Type": "image/png" },
          { "Src": "/media/icon-512.png", "Sizes": "512x512", "Type": "image/png" },
          { "Src": "/media/icon-maskable.png", "Sizes": "512x512", "Type": "image/png", "Purpose": "maskable" }
        ]
      },
      "ServiceWorker": {
        "CachePrefix": "contoso",
        "ApiPrefix": "/api/",
        "SkipPaths": ["/umbraco/"]
      }
    }
  }
}
```

**Chrome will not offer to install a site without a 192px and a 512px icon**, so those two entries
are the difference between the package working and appearing not to.

| Setting | Default | Notes |
| --- | --- | --- |
| `TrackInstalls` | `true` | Off leaves the app behaviour and collects nothing |
| `TrackInstalledOnly` | `false` | On stores nothing until someone actually installs |
| `RetentionDays` | `0` | Zero keeps rows forever |
| `ServeAssets` | `true` | Off if the site already ships its own `sw.js` |
| `ServiceWorker.Version` | assembly version | Change per deploy to purge stale cached assets |
| `InstallPrompt.MinVisits` | `2` | The visit on which the install prompt may first show. Visits are counted by day. `0` or `1` allows the first visit |
| `InstallPrompt.MinScrollPercent` | `50` | Percent of the page the visitor must scroll first |
| `InstallPrompt.MinSecondsOnPage` | `30` | Seconds on one page that also count. Scroll or time is enough |

The install prompt waits for an engaged visitor. It shows on their second visit or later, and only
after they scroll half a page or stay 30 seconds. Set `MinScrollPercent` or `MinSecondsOnPage` to
`0` to drop the page wait (either one at `0` counts as already met), and `MinVisits` to `0` to show it on the first visit. If the browser
blocks storage, visits cannot be counted and the prompt stays hidden unless `MinVisits` is `0` or
`1`. Dismissing the prompt still hides it for good.

### Extending the service worker

To add your own handlers (push, background sync), list scripts for the worker to import:

```json
"ServiceWorker": {
  "ImportScripts": [ "/App_Plugins/BaryoDev.PostAlerts/push-sw.js" ]
}
```

They are loaded with `importScripts` as the first thing the worker does. Only paths on your own
site that start with a single `/` are accepted, and anything else is dropped and flagged in the
readiness check. Imported scripts run with the worker's full scope, so list only scripts you trust.

## What the worker caches

- **Pages** are fetched from the network first. A copy is kept only to answer when the visitor is offline, and the offline page is used when there is no copy.
- **API responses** under `ApiPrefix` (default `/api/`) are fetched live. A copy is kept only as an offline fallback, and offline requests with no copy get a 503.
- **Other same-origin files** (scripts, styles, images, media) are served from the cache when there is a copy, and revalidated in the background on the same request with a conditional request (an unchanged file costs a 304), so a changed file is current by the next view.
- **Never stored:** anything under `SkipPaths` (the backoffice by default), other origins, error responses, redirects, partial (206) responses, and any response with `Cache-Control: no-store` or `private`. Responses for signed-in visitors are marked `private` by default.
- **How long:** until the next refresh replaces it, or until `ServiceWorker.Version` changes, which clears everything.

Media files are served publicly by Umbraco, since public access protects content pages, not files in `/media/`. If you protect files some other way, add their path to `SkipPaths`.

## It tells you why it is not working

Browsers enforce installability silently. Miss a 512px icon, or point at one that 404s, and Chrome
simply never offers to install your site. Nothing errors and nothing logs.

The dashboard runs a preflight and names the failing condition:

- served over HTTPS (a service worker will not register otherwise, localhost excepted)
- manifest has a name
- display mode is app-like rather than `browser`
- a 192px and a 512px icon are configured **and actually reachable**
- a maskable icon, advisory: without one Android crops your icon into a white circle

This check exists because it caught a real failure while this package's own demo was being
deployed.

## What it records

One row per browser, keyed by an id the browser generates for itself:

platform, display mode, whether it has ever run installed, first and last seen, and a launch count.

**No IP address, no user agent string, no visitor identity.** The table answers "how many people
installed this, on what" without becoming a record of who visited.

## Endpoints

| Method | Route | Access |
| --- | --- | --- |
| `POST` | `/umbraco/pwa/api/report` | anonymous, by necessity |
| `GET` | `/umbraco/management/api/v1/baryodev/pwa/summary` | Settings section |
| `GET` | `/umbraco/management/api/v1/baryodev/pwa/installs` | Settings section |
| `GET` | `/umbraco/management/api/v1/baryodev/pwa/readiness` | Settings section |
| `GET` | `/manifest.webmanifest`, `/sw.js`, `/baryodev-pwa.js` | anonymous |

A well-formed report always returns `202` with no body, whether it was stored or rate limited. It
is best-effort telemetry, so a client should never retry over it, and an answer that varied with
the report's content would let an anonymous caller probe which device ids exist. Malformed
requests are rejected on shape: `400`, `415` or `413`.

## Using it with a decoupled front end

The report contract is identical to
[`@baryodev/pwa-kit`](https://github.com/BaryoDev/pwa-kit), so a Next.js or Nuxt front end talking
to Umbraco headless can use the npm package and post to the same endpoint:

```ts
import { reportPwaStatus } from "@baryodev/pwa-kit";

reportPwaStatus((report) =>
  fetch("https://cms.example.com/umbraco/pwa/api/report", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(report),
  }),
);
```

The dashboard does not care which client sent the report.

## Verified, not assumed

The integration tests run against a real Umbraco booting on SQLite, covering the migration, the
endpoints, the generated assets and the dashboard registration. CI runs the whole suite against
**every supported major**, because a version range in a csproj is a claim and this is the evidence
for it:

| Umbraco | Runtime | Support | Tests |
| --- | --- | --- | --- |
| 16 | .NET 9 | standard-term | 57 / 57 |
| 17 | .NET 10 | **current LTS**, into late 2027 | 57 / 57 |
| 18 | .NET 10 | latest | 57 / 57 |

Beyond that, the package was deployed to a live Umbraco 18 behind nginx and driven with a real
browser, which confirmed end to end:

| | |
| --- | --- |
| Service worker | registered at root scope, `https://.../sw.js` |
| Caching | shell populated; the backoffice stayed out of it after a live `/umbraco/` fetch |
| Install prompt | `beforeinstallprompt` fired and the banner rendered with a working Install button |
| Tracking | the visit reached the database, and a second visit incremented the launch count rather than adding a row |

## Requirements

Umbraco 16, 17 or 18. The package multi-targets .NET 9 and .NET 10 and NuGet picks the right one.

**Umbraco 15 is not supported.** It is the last major with the synchronous `MigrationBase`, which
16 replaced with `AsyncMigrationBase`. That break lands between 15 and 16 while the runtime break
lands between 16 and 17, so the two do not align and one assembly cannot span both. 15 is also
standard-term support and past its one-year window.

## Versions

From 1.0 this package follows semantic versioning across five public surfaces, not just the
assembly API: the configuration keys, the report contract, the database table and the generated
asset URLs are equally public, and breaking any of them breaks a site rather than a build.
[VERSIONING.md](VERSIONING.md) says what each covers, which test gates it, and which Umbraco
versions are supported.

## Contributing

Contributions are welcome, and the issue list is written to be picked up by someone who has never
seen this code. Every issue says what is wrong, why it matters and where to start, rather than
leaving you to work that out first.

Good places to begin:

- [Good first issues](https://github.com/BaryoDev/umbraco-pwa/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22),
  which are genuinely small rather than nominally small.
- [#18](https://github.com/BaryoDev/umbraco-pwa/issues/18) needs no C# at all. The icons and logo
  here are developer placeholders and it shows, so a designer would move the needle further than
  another feature would.
- [#15](https://github.com/BaryoDev/umbraco-pwa/issues/15) and
  [#21](https://github.com/BaryoDev/umbraco-pwa/issues/21) are the roadmap, and they explain the
  reasoning rather than just listing features. Worth reading before picking anything up, and
  disagreeing with either of them on the issue is a useful contribution in itself.

Comment `/take` on an issue to assign yourself, or just say you are interested and we will sort it
out. Running the whole thing locally is one command, and [CONTRIBUTING.md](CONTRIBUTING.md) has
the rest.

There is a live demo at [dev-playground.baryo.dev](https://dev-playground.baryo.dev) if you would
rather see it work before checking anything out.

## Licence

MIT.
