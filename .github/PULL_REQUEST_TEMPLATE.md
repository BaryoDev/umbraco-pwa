## What this changes

<!-- One or two sentences. The diff says what; say why. -->

## Related issue

<!-- Fixes #123, or "none, this is a typo fix". -->

## Checklist

- [ ] A test fails without this change
- [ ] `dotnet test tests/BaryoDev.Umbraco.Pwa.Tests` passes locally
- [ ] Still works on Umbraco 16, 17 and 18, or the difference is handled with a conditional

## If the public API changed

`BaryoDev.Umbraco.Pwa.ApiApproval.Tests` pins the public surface to a checked-in file, because
other people's sites compile against it. A failure there is not necessarily wrong; read the diff.

- [ ] `.approved.txt` updated by copying the `.received.txt` over it, and both committed
- [ ] Additions only, so this is a minor bump
- [ ] Something was removed or changed in place, so this is a major

## If it touches the browser side

- [ ] No build step added. Plain custom elements, no npm, no bundler
- [ ] Anything rendered from `deviceId` is still escaped, since it arrives from an anonymous endpoint
- [ ] Checked in a real browser, not only in tests. Both silent failures found so far were invisible to the suite

## If it touches what gets stored

- [ ] No new column that could identify a visitor. No IP, no user agent, no user id
- [ ] The report endpoint still returns 202 with no body, whatever happens
