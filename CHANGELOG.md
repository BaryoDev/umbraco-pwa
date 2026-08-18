# Changelog

Notable changes to `BaryoDev.Umbraco.Pwa`. Follows [Keep a Changelog](https://keepachangelog.com)
and [semantic versioning](https://semver.org).

Additions to the public surface are a minor. Anything removed or changed in place is a major, and
gets a migration note here. The `.approved.txt` files in
`BaryoDev.Umbraco.Pwa.ApiApproval.Tests` are what decide which of those applies.

## [Unreleased]

### Added

- **`IPwaAssetGenerator.Client(string pathBase)`.** An overload rather than a default argument on
  the existing `Client()`, because rewriting a method in place would have made a bug fix a major.
  It carries a default interface implementation that returns the root-relative script, so an
  outside class implementing only `Client()` still compiles and keeps the behaviour it already had.

- **Readiness now checks the start URL resolves to something.** A site could install cleanly with
  every other check green and open on Umbraco's "no published content" page, because `StartUrl`
  defaults to `/`. Found on a real iPhone. The check distinguishes a static file under `wwwroot`,
  a published Umbraco route, and neither, and it is blocking rather than advisory. ([#16])
- **PWA readiness is now checked once at application startup.** Failed installability checks are
  written to the application log with their actionable details, making configuration problems
  visible without opening the backoffice dashboard. ([#4])
- `LICENSE`, `SECURITY.md`, this changelog, and issue and pull request templates.

### Changed

- Marketplace listing description rewritten so the first sentence survives truncation on a
  listing card, which cuts around 120 characters. Reported by @bharathh866. ([#5])

### Fixed

- **The service worker precached nothing, so the offline page was usually missing.** The install
  handler was `self.skipWaiting()` and nothing else, which meant the navigation fallback was only
  in the cache if the visitor happened to have loaded it while online. Someone who installed the
  app from a deep page and then lost connection got the browser's own error page. The fallback is
  now precached during `install`, and a fallback that will not fetch is caught rather than left to
  abort the install. ([#30])

- **The navigation branch cached error responses.** Its two neighbouring branches checked
  `resp.ok` before writing to the cache and this one checked nothing, so a 404 or a maintenance
  page served during a deploy was stored under that URL and served offline until the cache version
  changed. The test added with the fix asserts structurally that no cache write anywhere in the
  worker is unguarded, so a branch added later is covered too. ([#31])

- **The worker stored responses the server said not to store.** Nothing anywhere in it read a
  response header, so `Cache-Control: no-store` and `private` were ignored in all three branches,
  as was a redirect. Cache Storage enforces no HTTP semantics of its own; the worker now applies
  them itself in one place. Two parts of [#32] are still open: whether `/media/` belongs in the
  default `SkipPaths`, and whether plain assets should stay cache-first. ([#32])

- **The client wrote both of its URLs from the domain root.** `fetch("/umbraco/pwa/api/report")`
  and `navigator.serviceWorker.register("/sw.js")` ignored the application's path base, so on a
  site served under a prefix install reports went nowhere and the service worker never registered,
  silently costing the site its offline support. Both are now written from the path base, and the
  registration states its scope.
- **A browser in fullscreen counted as an install.** `(display-mode: fullscreen)` matches a browser
  someone pressed F11 in, not only an installed app, so any visitor who went fullscreen was
  recorded as installed. It now counts only when the site's own manifest asks for fullscreen.
- The Docker build for the demo site failed with `NU1015` because it never copied
  `tests/Directory.Packages.props`.

## [0.1.0] - 2026-08-13

First release.

### Added

- Generated `/manifest.webmanifest`, `/sw.js` and `/baryodev-pwa.js`, all served from the
  application root because a service worker only controls pages at or below its own URL.
- Install prompt with platform-specific behaviour. Android and desktop Chrome wait for
  `beforeinstallprompt`; iOS shows Share then Add to Home Screen instructions, since it has no
  install API. Suppressed in the backoffice, and dismissal is remembered.
- A **PWA** dashboard in the Settings section showing installed devices, install rate, 30-day
  active count and a device table.
- Install tracking that stays in the site's own database. One row per browser, keyed by an id the
  browser generates for itself. No IP address, no user agent, no visitor identity of any kind.
- Installability preflight, which names the condition a browser is enforcing silently. Built after
  a deployment where the manifest pointed at icons returning 404 and Chrome quietly declined to
  offer installation.
- Support for Umbraco 16, 17 and 18, with CI running the suite against each.

[Unreleased]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/BaryoDev/umbraco-pwa/releases/tag/v0.1.0
[#5]: https://github.com/BaryoDev/umbraco-pwa/issues/5
[#16]: https://github.com/BaryoDev/umbraco-pwa/issues/16
[#4]: https://github.com/BaryoDev/umbraco-pwa/issues/4
[#30]: https://github.com/BaryoDev/umbraco-pwa/issues/30
[#31]: https://github.com/BaryoDev/umbraco-pwa/issues/31
[#32]: https://github.com/BaryoDev/umbraco-pwa/issues/32
