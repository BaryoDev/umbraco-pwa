# Contributing

Contributions are welcome, including small ones. This adds to the
[BaryoDev-wide guide](https://github.com/BaryoDev/.github/blob/main/CONTRIBUTING.md); where the two
disagree, this file wins.

## Getting it running

```sh
git clone https://github.com/BaryoDev/umbraco-pwa.git
cd umbraco-pwa
dotnet test
```

That boots a real Umbraco on SQLite and runs 59 tests. First run takes a while because Umbraco
cold-boots; after that it is seconds.

To see it in a browser:

```sh
cd tests/TestSite
dotnet run --urls http://localhost:5199
```

Then open `http://localhost:5199/demo.html`. The backoffice is at `/umbraco` with the credentials
in `tests/TestSite/appsettings.json`, which are an obvious local-only placeholder.

## What this package is for

Two things, and it is worth being explicit because they decide most design questions:

**It makes an Umbraco site installable with no build step.** A site owner adds two lines to a
layout. If a change would require them to run npm, write JavaScript, or add a bundler, it is
probably the wrong change.

**The install data never leaves their server.** No third-party call, no analytics endpoint, no
telemetry. This is the reason to choose it over a hosted service, so a feature that phones anywhere
is not a feature this package can have.

## Things worth knowing before you change something

**The service worker has to be served from `/sw.js`.** A worker only controls pages at or below its
own URL, so moving it under `App_Plugins` would silently make it control nothing. Same for
`/manifest.webmanifest`.

**`deviceId` arrives from an anonymous endpoint and is rendered in an administrator's browser.**
That is a stored-XSS path. The server whitelists and truncates on the way in, and the dashboard
escapes every interpolated value on the way out. Both halves are load-bearing; neither is
decoration.

**The report endpoint always returns 202 with no body.** It is best-effort telemetry, so a client
should never retry over it, and a distinguishable response would let an anonymous caller probe
which device ids exist.

**The dashboard is a plain custom element with no build step.** The backoffice already ships the
`uui-*` components it uses. A package that needs npm and a bundler to render one table is a package
that breaks the first time the toolchain moves. Please keep it that way.

**No new columns that identify a visitor.** No IP address, no user agent, no user id. The table can
answer "how many people installed this, on what" and is structurally incapable of answering "who
visited". That guarantee is the product.

## Tests

Tests boot a real Umbraco rather than a mock, on purpose. The migration, the DI registrations and
the route registrations are only exercised by a real boot, and a test double passes with all three
broken.

**Every change needs a test that fails without it.** If you cannot write one, say so in the
description and explain why. Two of the bugs in this repo's history were found only because a test
turned red for a reason nobody predicted, and one test was passing *because* of a bug.

CI runs the whole suite against **Umbraco 16, 17 and 18**. A change that only works on one of them
needs a conditional, not a version bump.

## The public API is a contract

`BaryoDev.Umbraco.Pwa.ApiApproval.Tests` pins the public surface to a checked-in text file. Renaming
an export or changing a signature is a breaking change for someone even when every behavioural test
passes.

A failure there is not necessarily wrong. Read the diff, then approve it by copying the
`.received.txt` over the `.approved.txt` and committing both, along with whatever version bump the
change implies: additions are a minor, anything removed or changed in place is a major.

## Secrets

`appsettings.json` in the test site carries deliberate placeholders. **Umbraco regenerates
`Umbraco:CMS:Imaging:HMACSecretKey` whenever the value is absent or empty**, so it will write a real
secret into your working tree and it will ride along in your next commit. The committed placeholder
is valid Base64 precisely so Umbraco leaves it alone. Please do not blank it.

`gitleaks` runs in CI over the full history. If it fails on your branch, do not allowlist the value:
allowlisting by value creates a permanent blind spot for exactly the string that leaked. Rotate the
secret and allowlist by path if it is genuinely not sensitive.

## Good first issues

Look for [`good first issue`](https://github.com/BaryoDev/umbraco-pwa/labels/good%20first%20issue).
If nothing fits and you want to help, opening an issue describing how you use Umbraco and what is
missing is genuinely useful.

## Reporting a security issue

Do not open a public issue. See
[SECURITY.md](https://github.com/BaryoDev/.github/blob/main/SECURITY.md).
