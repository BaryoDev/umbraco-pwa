# What a version number means here

From 1.0 onwards this package follows [semantic versioning](https://semver.org). That is only
worth anything if it says which surfaces the promise covers, because a package like this one has
five, and four of them are not the assembly API.

## The five surfaces

Breaking any of these is a major. Adding to them is a minor.

**1. The assembly API.** Every public type and member in `BaryoDev.Umbraco.Pwa`. Gated by an
approval test: `tests/BaryoDev.Umbraco.Pwa.ApiApproval.Tests` holds a snapshot, and any change to
the surface fails the build until the snapshot is deliberately updated. This includes attributes on
public members, which is less obvious than it sounds and has caught three changes already.

**2. The configuration keys.** Everything under `BaryoDev:Pwa`. These live in a site owner's
`appsettings.json` and a rename breaks their deployment rather than their build, which is worse,
because nothing tells them. Renaming a key is a major. Changing a default is a minor and gets a
changelog note saying what changes on upgrade.

**3. The report contract.** The JSON body posted to `POST /umbraco/pwa/api/report`:

```json
{ "deviceId": "string", "displayMode": "string", "platform": "string", "installed": true }
```

Shared with `@baryodev/pwa-kit`, so the same client works against this package and any other
backend implementing it. Removing a field or changing what one means is a major. The endpoint
answers `202` to everything by design, so a client cannot detect a break: it just stops being
recorded, silently, which is the reason this is on the list.

**4. The database table.** `BaryoDevPwaInstall` in the site owner's own Umbraco database. Gated by
`InstallTableSchemaTests`, which pins every column, its type, its nullability and both indexes.

The gate matters more here than anywhere else. The migration plan creates the table and skips when
it already exists, so adding a property to the DTO changes what a *fresh* install creates and does
nothing at all to a site that already ran the first step. The two diverge silently, and the symptom
lands on whoever has been running longest. A new column needs a new migration step chained after
the previous state, never an edit to an existing one.

That test also holds the schema half of a promise in `SECURITY.md`: no column identifies a visitor.
It fails on a column that looks like an address, an agent, a name or a location.

**5. The generated asset URLs.** `/manifest.webmanifest`, `/sw.js` and `/baryodev-pwa.js`. A
service worker only controls pages at or below its own URL, so `/sw.js` cannot move without
breaking offline support for every site already serving it. Moving any of these is a major.

## Supported Umbraco versions

| Umbraco | Runtime | Status |
| --- | --- | --- |
| 16 | .NET 9 | supported, standard-term |
| 17 | .NET 10 | supported, current LTS into late 2027 |
| 18 | .NET 10 | supported, latest |

CI runs the whole suite against all three on every pull request. A version that is not in that
matrix is not supported, whatever the package reference range happens to allow.

15 is deliberately excluded. It is the last major with the synchronous `MigrationBase`, which 16
replaced with `AsyncMigrationBase`. That break lands between 15 and 16 while the runtime break
lands between 16 and 17, and the two do not align, so one assembly cannot span both.

## Fixes

Fixes go to the latest published version. There are no long-term support branches, and there will
not be until somebody is running this in a way that needs one.

Dropping an Umbraco major is a major version of this package, and gets a changelog note saying so
before it happens rather than after.

## What is not covered

Anything `internal`, the generated service worker's source text, the exact wording of a readiness
check, and the dashboard's markup. Tests assert on some of these because they are the cheapest way
to hold behaviour in place, not because they are promises to anyone outside this repository.
