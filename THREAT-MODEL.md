# Threat model

What this package trusts, what it does not, and where the boundaries between those are. Written so
a security review has something to scope from, and so a change that quietly moves a boundary is
visible as a change to this file.

Current as of the 1.0 hardening pass. `SECURITY.md` states the promises; this says how they are
held and where they are thin.

## What it adds to a site

Seven routes, and nothing else reachable from outside.

| route | who can reach it |
| --- | --- |
| `POST /umbraco/pwa/api/report` | anyone, by necessity |
| `GET /manifest.webmanifest` | anyone |
| `GET /sw.js` | anyone |
| `GET /baryodev-pwa.js` | anyone |
| `GET /umbraco/management/api/v1/baryodev/pwa/summary` | Settings section |
| `GET .../baryodev/pwa/installs` | Settings section |
| `GET .../baryodev/pwa/readiness` | Settings section |

Plus one table, `BaryoDevPwaInstall`, in the site owner's own database, and one piece of middleware
that adds a response header.

## Assets

**The install table.** Every browser that has ever loaded the site, if `TrackInstalls` is on. It
holds no address, no user agent, no name and no location, so it cannot answer "who visited". It can
answer "how many, on what platform, how often", and that is the whole point of it.

**The administrator's browser session.** The dashboard renders values that arrived from an
anonymous endpoint. That is a stored-XSS path and it is the highest-value target here.

**The site's own network.** The readiness check makes an outbound request to an address from
configuration. Left ungoverned that is a way to ask the server what it can reach.

**Visitors' cached pages.** Cache Storage belongs to the browser profile, not the visitor, so
anything cached while one person was signed in is readable by whoever uses that browser next.

## Actors

- **An anonymous visitor.** Can reach the report endpoint and the three generated assets. Assumed
  hostile and assumed scripted.
- **A signed-in member,** or anyone signed in by any scheme the site uses. Same reach, plus
  whatever the site gives them.
- **A backoffice user without Settings access.** An editor. Signed in, so past Umbraco's own guard
  on the management API, and stopped only by this package's policy.
- **A backoffice user with Settings access.** Trusted to read the dashboard. Not assumed to be an
  infrastructure operator, which matters for the outbound probe.
- **Whoever configures the site.** Trusted. If they can edit `appsettings.json` they can usually
  open a shell, so config is not a boundary.

## The four boundaries

### 1. Anonymous visitor to the report endpoint

Everything in the body is hostile input.

Enforced: the request body is capped at 4KB before it is parsed. `deviceId` must match a browser
id shape and is cut to 100 characters. `platform` and `displayMode` are checked against known
sets. Reports are rate limited per caller address. Every well-formed report answers `202` with no
body whatever became of it, so the endpoint cannot be used as an oracle for which device ids exist.
A malformed request is rejected on shape alone (`400`, `415`, `413`), which says nothing about
what is stored.

Not enforced: nothing proves a report is genuine. Anyone can post plausible ones and inflate the
numbers. That is accepted: the data is adoption telemetry, not a ledger, and the alternative is
identifying visitors, which the package refuses to do.

Also not enforced: a known device id costs one UPDATE and a novel one an UPDATE plus an INSERT, so
there is a weak timing difference between them. Left alone deliberately. Knowing an id in the first
place means holding the browser that generated it, which is not an attacker who needs the oracle.

The rate limit partitions on the caller's address, so every visitor behind one proxy shares a
budget. The address is used and never stored.

### 2. Stored value to the administrator's browser

`deviceId` arrives from boundary 1, is stored, and is rendered into the dashboard.

Enforced on the way in: the character set check, which makes a value that could open a tag or an
attribute impossible to store. Enforced on the way out: every interpolated value goes through
`escapeHtml`. Both halves, and the browser test for the dashboard renders hostile values and
asserts no element and no handler is created.

Thin: the dashboard builds markup with `innerHTML`. That is safe only because every interpolation
is escaped, and it is one careless template literal away from not being.

### 3. Site configuration to an outbound request

The readiness check fetches a configured absolute icon URL, on every application start and whenever
the dashboard is opened.

Enforced: the connection is refused unless the resolved address is on the public internet.
Loopback, private, link-local including cloud metadata, carrier-grade NAT, their IPv6 equivalents,
IPv4-mapped and NAT64 forms are all blocked. The check runs in the connect callback, so it covers
every redirect hop and validates the address that is then dialled. Redirects are capped at three
and the request times out after five seconds. The failure detail is a fixed set of reasons rather
than the exception.

Not enforced: nothing stops a site owner pointing the probe at a public host that is slow. The
timeout bounds it.

Worth knowing: this is the only outbound request the package makes, and using a site-relative icon
path removes it entirely.

### 4. One visitor's session to the next person on the device

Cache Storage is shared by everyone who uses the browser profile.

Enforced: a response rendered while any identity on the request is authenticated is marked
`Cache-Control: private`, and the service worker declines to store it. Every identity is checked,
not just the first, which is what makes preview safe: Umbraco appends the backoffice identity
behind the framework's anonymous one, so a draft render would otherwise read as anonymous and be
cached under the published URL.

`SkipPaths` excludes the backoffice by default. Paths are compared case-insensitively and after
percent-decoding, matching how ASP.NET routes, so `/Umbraco/` and `/%75mbraco/` are excluded too.

Not enforced: content gated in code that does not sign the visitor in. Nothing can infer that those
pages are private. The site owner has to list them in `SkipPaths`, and a readiness check warns when
the setting is off and nothing has been excluded.

Also not enforced: the package does not ask Umbraco whether a path is protected. A member reading
protected content is signed in, so the rule above already covers them, and Umbraco redirects an
unauthenticated visitor to a login page before the response is rendered. An earlier version claimed
to check this and did not: it passed a URL path where the service wanted a comma-separated node id
path, so the check never fired. See #117.

## Out of scope

Umbraco's own security. A site owner who has misconfigured public access, or an administrator
account that is compromised, is not something this package can compensate for.

The site's TLS, its hosting, and its other packages.

`RetentionDays` defaults to zero, meaning keep everything. That is a data-retention decision for the
site owner under their own lawful basis, not a security control this package sets for them.

## Known thin spots

These are the honest answers to "where would you attack it", and each is either filed or explained.

1. **A backoffice user without Settings access has never been tested against the read side.** The
   anonymous case is covered, but that passes because Umbraco guards the whole management API
   surface, not because of this package's policy. Filed as #104. This is the first place to look.
2. **The dashboard's `innerHTML` construction.** Correct today, structurally fragile.
3. **The service worker caches first with no revalidation,** so a stale page persists longer than
   it should even when it is correctly cached. Filed as #32.
4. **No penetration test has been run.** For a package with no measurable install base a paid
   engagement is not proportionate, and a timeboxed test against this document is. When one
   happens, its scope and date go here; the absence of a result is not a claim that there is
   nothing to find.

## Reporting

Not here. `SECURITY.md` has the private route.
