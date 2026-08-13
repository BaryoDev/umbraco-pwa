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

## Requirements

Umbraco 18, .NET 10. Support for 15 to 17 is planned once it is tested rather than assumed.

## Licence

MIT.
