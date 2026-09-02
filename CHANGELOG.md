# Changelog

Notable changes to `BaryoDev.Umbraco.Pwa`. Follows [Keep a Changelog](https://keepachangelog.com)
and [semantic versioning](https://semver.org).

Additions to the public surface are a minor. Anything removed or changed in place is a major, and
gets a migration note here.

There are five public surfaces, not one: the assembly API, the configuration keys, the report
contract, the database table and the generated asset URLs. [VERSIONING.md](VERSIONING.md) says what
each covers and which test gates it.

## [Unreleased]

## [0.5.0] - 2026-09-02

### Security

- **The readiness check can no longer be used to probe the network the server sits on.** It fetches
  whatever URL the icon configuration names, and it followed redirects with no check on where they
  led, so cloud instance metadata and anything on a private subnet were reachable, with open,
  closed and filtered distinguishable from the reply. The guard now runs in the connection callback
  rather than on the URL, so it covers every redirect hop and validates the address it then dials.
  ([#88], [#95])
- **Failure detail no longer carries the exception.** `ex.Message` reached the backoffice and the
  application log, and `HttpClient` messages name hosts and ports. ([#88], [#95])
- **`deviceId` is checked against a character set, not only truncated.** It arrives from an
  anonymous endpoint and is rendered into an administrator's dashboard. The escaping there was the
  only thing holding, while `SECURITY.md` described both halves as load-bearing. ([#98])
- **Pages rendered for a signed-in visitor are no longer cached by the service worker.** Cache
  Storage belongs to the browser profile rather than the visitor, so a member's pages were served
  to whoever picked the device up next. Umbraco sends no cache headers of its own, measured against
  a real instance, so the worker had nothing telling it to decline. The package now marks those
  responses `Cache-Control: private` and the worker's existing check does the rest, so the service
  worker is unchanged. A response that already carries a `Cache-Control` header is never
  overridden. `BaryoDev:Pwa:MarkSignedInResponsesPrivate` turns it off. ([#89])
- **A readiness warning** when a site has member-protected content, has turned that setting off,
  and has excluded nothing through `SkipPaths`. ([#89])
- **The report endpoint is rate limited per caller.** It inserts a row for each novel `deviceId`,
  which the client generates, so a loop with fresh ids inserted rows without limit. Default 120 a
  minute per address, `BaryoDev:Pwa:MaxReportsPerMinute`, zero to turn it off. The address
  partitions the limiter and is never stored, so `SECURITY.md`'s promise that no column identifies
  a visitor still holds. Declining still answers `202`, because a distinguishable response would
  say where the limit is and when it resets. ([#36])
- **The report body is capped at 4KB.** `deviceId` is cut to 100 characters only after the JSON has
  been parsed. ([#98])
- **`System.Security.Cryptography.Xml` is pinned per target framework.** Umbraco resolves 8.0.0 on
  net10.0 and 9.0.4 on net9.0 through Examine, and both are inside the range of eight advisories,
  one of them a signature-verification bypass. Umbraco pin it internally, but the published
  Examine.Lucene nuspec does not carry the pin, so it never reached anyone installing this package.
  The pin is in this package's nuspec, so upgrading raises the floor for your site too. ([#90])

### Added

- **[THREAT-MODEL.md](THREAT-MODEL.md).** The four trust boundaries, what is enforced at each, and
  the thin spots named rather than left to be discovered. Written so a review has something to
  scope from and so a change that moves a boundary shows up as a change to that file.
- **[VERSIONING.md](VERSIONING.md).** What a version number promises from 1.0, named surface by
  surface, with the test that gates each one. Four of the five are not the assembly API and only
  that one had a gate.
- **A schema gate on the install table.** Every column, type, nullability and both indexes are
  pinned. A property added to the DTO without a migration step reaches a fresh install and never
  reaches an upgraded one, and until now nothing noticed. It also holds the schema half of
  `SECURITY.md`'s promise that no column identifies a visitor.
- `PwaOptions.MarkSignedInResponsesPrivate`, an addition to the public surface. ([#89])
- `PwaOptions.MaxReportsPerMinute`, and the filter attribute on the report action. Both are
  additions to the public surface. ([#36])
- `[RequestSizeLimit(4096)]` on the report action, which is an addition to the public surface.
  ([#98])
- **An SBOM and a build provenance attestation** on every published release. CycloneDX lists the
  dependency tree, which is what an agency's procurement asks for before a package goes into a
  client site. The attestation proves the bytes came from this workflow at this commit, and is
  checkable against the artifact attached to the GitHub release. ([#108])

### Changed

- **`SECURITY.md` said there was no outbound call to any third party at runtime.** That was not
  true: the readiness check probes a configured absolute icon URL, and it runs on every application
  start rather than only when an administrator asks. It now describes what actually happens, and
  what to configure to avoid it entirely. `CLAUDE.md`'s constraint list says the same. ([#88])
- Dependency advisories now fail the build in a dedicated CI job rather than only warning, and the
  job asserts that restore actually audited every project. ([#90])
- **The release path now runs every gate CI runs**, not the unit tests alone. The approval test, the
  advisory audit and the browser suite were all outside it. They run on pushes to main, so in
  practice they usually had run, and "usually" is not what a release gate means: a tag can be pushed
  at any commit. ([#108])

### Testing

- **The readiness endpoint is covered, and the authorization test can now fail.** It passed because
  Umbraco guards the whole management API, not because of this package's policy, so it would have
  stayed green with the policy removed. ([#103])


## [0.4.0] - 2026-08-27

### Added

- **`launch_handler` in the manifest.** Controls whether launching the installed app opens a new
  window or navigates one that is already open. Off unless configured: the key is omitted by
  default, so upgrading from 0.3.0 changes nothing on its own. A value the spec does not define is
  dropped rather than written out, because a browser silently ignores a `launch_handler` it cannot
  parse and an emitted typo would behave exactly like the feature working. ([#71])

### Changed

- **The dashboard summary is aggregated in the database** rather than by fetching every row and
  counting in memory. ([#75])

### Fixed

- **Concurrent first reports for the same device no longer race into duplicate rows.** The previous
  read-then-write pair could interleave across application processes. ([#76])

### Testing

None of this changes what the package does, and all of it changes what a green suite means.

- **The dashboard is now driven in a real browser.** The XSS regression test it replaces asserted
  that `dashboard.js` *contained the string* `escapeHtml(...)`, which is a claim about source text
  rather than behaviour and would have kept passing with the escaping applied to the wrong value.
  The replacement renders hostile values and asserts no element and no handler is created. ([#73])
- **Offline behaviour runs through a local forwarding proxy**, so disabling it takes the network
  away from the page and the service worker alike. The fallback test has a mirror that empties the
  caches and proves the navigation fails, which is what keeps the first one honest. ([#78])
- Startup handler guards and configuration switches covered. ([#72], [#74])
- Two suites had been sharing one page constant with opposite requirements, which turned `main` red
  after two individually green pull requests merged. ([#79])

## [0.3.0] - 2026-08-18

### Added

- **Browser tests.** The package generates JavaScript that runs in a browser, and until now nothing
  executed a line of it: every assertion was against generated text. That is how all three fixes
  below reached 0.2.0 with a green suite. `BaryoDev.Umbraco.Pwa.Browser.Tests` starts a real
  Umbraco on a loopback port and drives Chromium at it. ([#41])
- `CLAUDE.md`, recording the conventions this repository already followed but had never written
  down.

### Changed

- **Marketplace listing metadata corrected.** `umbraco-marketplace.json` carried `BugsUrl` and
  `SourceCodeUrl`, neither of which is in the Marketplace schema, so both were silently ignored and
  the listing carried no issue tracker link at all. The supported field is `IssueTrackerUrl`.
  `LicenseType` moved to `LicenseTypes`, and the package is now flagged as looking for
  contributors. ([#42])
- **`PackageReleaseNotes` is set for the first time**, so NuGet shows a changelog. The Marketplace
  schema has no field for one, which makes this the only place a release is described to someone
  deciding whether to upgrade.

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


## [0.2.0] - 2026-08-17

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

[Unreleased]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/BaryoDev/umbraco-pwa/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/BaryoDev/umbraco-pwa/releases/tag/v0.1.0
[#5]: https://github.com/BaryoDev/umbraco-pwa/issues/5
[#16]: https://github.com/BaryoDev/umbraco-pwa/issues/16
[#4]: https://github.com/BaryoDev/umbraco-pwa/issues/4
[#30]: https://github.com/BaryoDev/umbraco-pwa/issues/30
[#31]: https://github.com/BaryoDev/umbraco-pwa/issues/31
[#32]: https://github.com/BaryoDev/umbraco-pwa/issues/32
[#41]: https://github.com/BaryoDev/umbraco-pwa/pull/41
[#42]: https://github.com/BaryoDev/umbraco-pwa/pull/42
[#71]: https://github.com/BaryoDev/umbraco-pwa/pull/71
[#72]: https://github.com/BaryoDev/umbraco-pwa/pull/72
[#73]: https://github.com/BaryoDev/umbraco-pwa/pull/73
[#74]: https://github.com/BaryoDev/umbraco-pwa/pull/74
[#75]: https://github.com/BaryoDev/umbraco-pwa/pull/75
[#76]: https://github.com/BaryoDev/umbraco-pwa/pull/76
[#78]: https://github.com/BaryoDev/umbraco-pwa/pull/78
[#79]: https://github.com/BaryoDev/umbraco-pwa/pull/79
[#36]: https://github.com/BaryoDev/umbraco-pwa/issues/36
[#88]: https://github.com/BaryoDev/umbraco-pwa/issues/88
[#89]: https://github.com/BaryoDev/umbraco-pwa/issues/89
[#90]: https://github.com/BaryoDev/umbraco-pwa/pull/90
[#95]: https://github.com/BaryoDev/umbraco-pwa/pull/95
[#98]: https://github.com/BaryoDev/umbraco-pwa/pull/98
[#103]: https://github.com/BaryoDev/umbraco-pwa/pull/103
[#108]: https://github.com/BaryoDev/umbraco-pwa/pull/108
