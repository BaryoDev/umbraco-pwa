# BaryoDev.Umbraco.Pwa

An Umbraco package that turns a site into an installable, offline-capable app and reports installs
in the backoffice. Ships as a NuGet package and is listed on the Umbraco Marketplace.

Human-facing contribution rules are in `CONTRIBUTING.md`. This file is the working agreement for
anyone, person or agent, changing code here.

---

## 1. Layout

```
src/BaryoDev.Umbraco.Pwa/     The package. Composer, options, controllers, services, dashboard
  Services/                   Asset generation, readiness, install recording
  wwwroot/                    dashboard.js and umbraco-package.json, mapped to App_Plugins
tests/BaryoDev.Umbraco.Pwa.Tests/          Integration tests against a real Umbraco
tests/BaryoDev.Umbraco.Pwa.Browser.Tests/  Behaviour of the generated JavaScript, in a browser
tests/BaryoDev.Umbraco.Pwa.ApiApproval.Tests/  What decides minor vs major
tests/TestSite/               A real Umbraco host, used by the tests and by the public demo
```

## 2. The thing most likely to be got wrong

**This package's main deliverable is JavaScript that runs in a browser.** The service worker, the
install prompt and the client are all generated as text by C# and then executed somewhere no C#
test can see.

Three defects reached 0.2.0 while text assertions about the exact same lines were green: the
worker precached nothing, the navigation branch cached error responses, and nothing anywhere read
`Cache-Control`. A later fix for those introduced a fourth, an unguarded `addAll`, which the
structural test could not see because it matched on the old pattern rather than the concept.

So:

- **A change to generated JavaScript needs a test in `Browser.Tests`**, not only an assertion that
  the right string appears. String assertions are useful as regression pins and are not evidence
  of behaviour.
- **Assert the positive case alongside the negative.** A guard that refuses everything passes
  "the error was not cached" and breaks the package.
- When adding a guard, forbid the category rather than checking the known call sites. The
  `.addAll(` ban exists because enumerating call sites is how the gap appeared.

## 3. Testing

```bash
dotnet test tests/BaryoDev.Umbraco.Pwa.Tests            # integration, real Umbraco on SQLite
dotnet test tests/BaryoDev.Umbraco.Pwa.Browser.Tests    # real browser, real service worker
dotnet test tests/BaryoDev.Umbraco.Pwa.ApiApproval.Tests
```

Browser tests start the real `TestSite` as a child process on a loopback port and install Chromium
on first run, so `dotnet test` is the whole command with nothing to set up first. They are slower
than the rest: Umbraco cold-boots once per run.

CI runs the whole suite against **Umbraco 16, 17 and 18**. Override locally with
`dotnet test -p:UmbracoVersion=16.5.1`.

### A test for a bug fix must fail before the fix

Either write the failing test first, or revert the production change, confirm the test goes red,
and re-apply it. A test that passes both ways proves nothing, and this is the only cheap way to
tell a real test from a decorative one.

Mutation-test against `origin/main` rather than against the previous commit when a fix has already
been partly committed, or the measurement answers a different question than the one asked.

### The deployed demo is tested too, and separately

```bash
sh scripts/check-live-demo.sh                      # the real site
sh scripts/check-live-demo.sh http://127.0.0.1:5399  # a local dotnet run
```

Run by `.github/workflows/live-demo.yml` daily, on any PR touching `tests/TestSite/`, `README.md`
or `umbraco-marketplace.json`, and on demand.

It exists because the suite cannot see the deployment. The demo served Umbraco's "Welcome to your
Umbraco installation" screen at its root through 0.1.0 and 0.2.0: the site has no published
content, so that was a perfectly valid 200, and every listing that sends people to the demo points
at exactly that URL. The same screen was then precached as the offline page, because
`NavigationFallback` defaults to `/`. Nothing was broken in the code. The failure was entirely in
what the running site returned.

Two rules if you extend it:

- **Every "must not contain" assertion runs only after a real body has been proven to arrive.** An
  empty response, a 502 and a DNS failure all satisfy a negative grep. The script checks the status
  code and a minimum byte count first, for exactly the reason a guard that refuses everything
  passes its own negative test.
- **Check the URL we advertise, not the one we know works.** The script greps the demo URL out of
  `README.md` and `umbraco-marketplace.json` rather than hard-coding it. The original failure was
  never that the demo was broken, it was that the advertised address did not reach it.

### Naming

Test methods read as sentences: `An_error_response_is_never_cached`. Keep that style, it makes a
failure list readable.

## 4. Public API stability

External code compiles against these types, so within a major do not remove or change the
signature of a public member. Add an overload, mark the old one `[Obsolete]`, and have the old one
call the new one. Add interface members with a default implementation.

`tests/BaryoDev.Umbraco.Pwa.ApiApproval.Tests/approved-api/*.approved.txt` decides whether a change
is a minor or a major. Update it by copying `.received.txt` over `.approved.txt` and committing
both, along with the version bump it implies.

## 5. Constraints that do not move

1. **Nothing is reported anywhere.** No telemetry, no analytics, no phone-home. A feature that
   reports anything about a site or its visitors to anyone is not a feature this package can have.
   It is the whole pitch against hosted alternatives.

   The one exception is the readiness check fetching a manifest icon the site owner configured at
   an absolute URL, which is a request to an address they chose rather than a report to one we
   chose. It is bounded by `PwaIconProbe`, which refuses to connect to anything off the public
   internet. Do not add a second exception without amending this list in writing first: the value
   of the constraint is entirely in it having been kept. See #60 and #88.
2. **No column that identifies a visitor.** No IP, no user agent, no user id, no location, at any
   resolution. Every field is a counter or a timestamp hung off the browser-generated `deviceId`.
   `SECURITY.md` promises this and it is checkable against the schema.
3. **No build step for a site owner.** Plain custom elements against the `uui-*` components the
   backoffice already ships. A test-only Node dependency is fine, since nobody installing the
   package runs it. A change that makes a site owner run npm is the wrong change.
4. **The backoffice is never cached.** A cached backoffice is a stale editing experience and, on a
   shared machine, one user's data served to another.

## 6. Releasing

Publishing is gated and deliberately hard to do by accident:

- It runs only on a `v*` tag push, or a manual dispatch that defaults to a dry run.
- The version comes from `<Version>` in the csproj, never from an input, and the tag must match.
- **It refuses to publish a build the playground has not run.** Deploy first with
  `baryovm stack release umbraco-pwa`, which is also how the demo at `dev-playground.baryo.dev`
  stays in step with the package rather than drifting behind it.

Two pieces of metadata have to move with the version, and nothing fails if they do not:

- **`<PackageReleaseNotes>` in the csproj.** NuGet renders it on the package page and it is the
  only changelog anyone comparing versions will see, because the Umbraco Marketplace schema has no
  field for one. Stale release notes are worse than none.
- **`umbraco-marketplace.json` at the repository root.** Validate against
  `https://marketplace.umbraco.com/umbraco-marketplace-schema.json` after editing. An unknown key
  is not an error, it is silently ignored, which is how `BugsUrl` sat there looking correct while
  the listing carried no issue tracker link. The supported field is `IssueTrackerUrl`.

Listing changes are picked up by the "request package sync" button on the Marketplace `/validate`
page, not by the daily 04:00 UTC scan, and the record takes 25 to 30 minutes to update.

Do not tag a release without being asked to. Publishing to NuGet cannot be undone: a version
number is spent the moment it is used.

## 7. Comments

Default to no comment. A comment earns its place when it explains a non-obvious *why*, an
invariant the types cannot express, or a deliberate edge case. The generated JavaScript is the
exception worth spending comments on, because the reason a guard exists is invisible from the code.

No provenance noise: no `// fix for X`, no `// see PR #123`. That belongs in commit messages.

## 8. Commits and pull requests

- PR body carries `Fixes #123` on its own line, since GitHub only auto-closes from the body.
- Commit messages short and human. No AI attribution trailers, no `Co-Authored-By`.
- No em dashes anywhere, in code, comments, docs or issue text.
- One concern per PR. A bug fix and a behaviour decision do not travel together: say plainly on
  the issue which parts a PR does not address.

## 9. Security

- Secrets never enter the repository. `gitleaks` runs in CI over the full history, and a hit is a
  real incident because rotating is the only fix once something is pushed.
- `POST /umbraco/pwa/api/report` is anonymous by necessity and always returns 202 with no body. A
  distinguishable response would let an unauthenticated caller probe which device ids exist.
- Everything from that endpoint is public input. Whitelist it rather than storing it verbatim, and
  escape it again on the way out: it renders inside a signed-in administrator's browser.
