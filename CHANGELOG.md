# Changelog

Notable changes to `BaryoDev.Umbraco.Pwa`. Follows [Keep a Changelog](https://keepachangelog.com)
and [semantic versioning](https://semver.org).

Additions to the public surface are a minor. Anything removed or changed in place is a major, and
gets a migration note here. The `.approved.txt` files in
`BaryoDev.Umbraco.Pwa.ApiApproval.Tests` are what decide which of those applies.

## [Unreleased]

### Added

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
