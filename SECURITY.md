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

**Nothing is reported anywhere.** No telemetry, no analytics, no phone-home. A version of this
package that reported anything about your site or your visitors to anyone would be a supply-chain
issue, so if you see one, report it.

**One outbound request exists, and it is not that.** If you configure a manifest icon at an
absolute `http` or `https` URL, the readiness check fetches it to answer "why is my site not
offering to install?", because an icon that 404s makes Chrome decline silently. It runs when the
dashboard is opened and once when the application starts. It goes to the address you configured
and nowhere else, it sends nothing about your site, and it refuses to connect to anything that is
not on the public internet: loopback, link-local including cloud instance metadata, private
ranges, and their IPv6 and NAT64 equivalents. The check runs on the resolved address at connect
time rather than on the URL, so a hostname that resolves inward is refused too, on every redirect
hop. Configure icons with site-relative paths and no outbound request happens at all.

This paragraph used to say there was no outbound call at runtime. That was not true, and it is
the kind of claim worth checking rather than trusting: see #88.

**Pages rendered for a signed-in visitor are not cached.** Cache Storage belongs to the browser
profile, not to the visitor, so anything cached while one person was signed in would be served to
whoever uses that browser next. The package marks those responses `Cache-Control: private` and the
service worker declines to store them. This covers content protected through Umbraco's public
access, and any other sign-in, because the check is whether anyone is authenticated rather than
which scheme authenticated them. It never overrides a `Cache-Control` header the site set itself.

If your site gates content in code rather than through public access and does not sign the visitor
in, nothing can infer that those pages are private: add their paths to
`BaryoDev:Pwa:ServiceWorker:SkipPaths`. A readiness warning appears if you turn the protection off
while the site has protected content and has excluded nothing.

**The backoffice endpoints require the Settings section.** `/summary`, `/installs` and
`/readiness` are behind `SectionAccessSettings`. Reaching any of them without a backoffice session
is a vulnerability.

## Supported versions

Fixes go to the latest published version. The package supports Umbraco 16, 17 and 18, and CI runs
the suite against all three on every pull request. [VERSIONING.md](VERSIONING.md) holds the full
policy, including what counts as a breaking change to each of the five public surfaces.

## Known non-issues

- **The service worker is served from `/sw.js`.** It must be, because a worker only controls pages
  at or below its own URL. This is required, not an oversight.
- **`gitleaks` findings in `tests/TestSite/appsettings.json`.** Those are deliberate local-only
  placeholders. `Umbraco:CMS:Imaging:HMACSecretKey` in particular must stay valid Base64, because
  Umbraco regenerates it when blank and would write a real secret into a contributor's working
  tree.
