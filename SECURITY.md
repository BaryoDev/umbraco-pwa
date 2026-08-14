# Security

## Reporting

**Please do not open a public issue.** Use
[GitHub's private vulnerability reporting](https://github.com/BaryoDev/umbraco-pwa/security/advisories/new),
or the contact route in the [BaryoDev policy](https://github.com/BaryoDev/.github/blob/main/SECURITY.md).

Expect an acknowledgement within a few days. This is a small project, so please allow reasonable
time for a fix before disclosing.

## What this package touches

Worth knowing when judging whether something is a vulnerability here.

**One anonymous endpoint exists.** `POST /umbraco/pwa/api/report` is deliberately unauthenticated,
because it is called by every visitor's browser. It always returns `202` with no body, whatever
happens. That is not laziness: a distinguishable response would let an anonymous caller probe
which device ids exist. Reports of "the endpoint accepts junk" are expected behaviour; reports
that it *leaks* something are not.

**`deviceId` is attacker-controlled and rendered to an administrator.** It arrives from that
anonymous endpoint and appears in the backoffice dashboard, which is a stored-XSS path. The
server whitelists and truncates on the way in, and the dashboard escapes every interpolated value
on the way out. Both halves are load-bearing. If you find a way through either, that is a real
finding.

**Nothing identifying is stored.** The table holds a browser-generated id, platform, display mode,
install state, timestamps and a launch count. No IP address, no user agent, no user id. If a
change would let the table answer "who visited", that is a security bug in this project's terms
even if it leaks nothing externally.

**Nothing leaves the server.** There is no outbound call to any third party at runtime. A version
of this package that phoned home would be a supply-chain issue, so if you see one, report it.

**The backoffice endpoints require the Settings section.** `/summary`, `/installs` and
`/readiness` are behind `SectionAccessSettings`. Reaching any of them without a backoffice session
is a vulnerability.

## Supported versions

Fixes go to the latest published version. The package supports Umbraco 16, 17 and 18, and CI runs
the suite against all three.

## Known non-issues

- **The service worker is served from `/sw.js`.** It must be, because a worker only controls pages
  at or below its own URL. This is required, not an oversight.
- **`gitleaks` findings in `tests/TestSite/appsettings.json`.** Those are deliberate local-only
  placeholders. `Umbraco:CMS:Imaging:HMACSecretKey` in particular must stay valid Base64, because
  Umbraco regenerates it when blank and would write a real secret into a contributor's working
  tree.
