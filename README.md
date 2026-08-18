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

The report endpoint always returns `202` with no body. It is best-effort telemetry, so a client
should never retry over it, and a distinguishable response would let an anonymous caller probe
which device ids exist.

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

57 tests run against a real Umbraco booting on SQLite, covering the migration, the endpoints, the
generated assets and the dashboard registration. CI runs the whole suite against **every supported
major**, because a version range in a csproj is a claim and this is the evidence for it:

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
