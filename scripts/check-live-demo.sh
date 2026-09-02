#!/bin/sh
# Smoke-check a deployed demo site.
#
# Run by CI after a release and on a schedule, and by hand against a local run:
#
#   sh scripts/check-live-demo.sh https://dev-playground.baryo.dev
#   sh scripts/check-live-demo.sh http://127.0.0.1:5399
#
# Why this exists. The demo served Umbraco's "Welcome to your Umbraco installation" screen at /
# for the whole of 0.1.0 and 0.2.0, because the site has no published content and nothing had ever
# asked what that URL returned. It was a valid 200, the test suite was green, and every listing
# that points people at the demo points at exactly that URL. The unit tests added with the fix
# cover the code. They cannot cover the deployment, and the deployment is where it broke.
#
# One rule throughout: a "must not contain" assertion only runs once a real body has been proven
# to arrive. Otherwise an empty response, a 502 or a DNS failure passes every negative check.

set -eu

EXPLICIT_BASE="${1:-}"
BASE="${1:-https://dev-playground.baryo.dev}"
BASE="${BASE%/}"

# Substantive, not cosmetic. The first two are the entire integration a site owner performs, so a
# landing page without them has nothing to install however good it looks. The third is what the
# root actually served before this was gated.
NEEDS_MANIFEST_LINK='rel="manifest"'
NEEDS_CLIENT='baryodev-pwa.js'
MUST_NOT='No Published Content'

# Copy, so it is expected to change. Update it here when the page is reworded; the point is that
# rewording is a deliberate edit rather than something that silently stops being checked.
DEMO_HEADLINE='This Umbraco site is an app'

# A response smaller than this is not a page. Guards every negative assertion below against
# passing simply because nothing arrived.
MIN_PAGE_BYTES=1000

failures=0
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

pass() { printf '  ok    %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; failures=$((failures + 1)); }

# Fetches into $tmp/body and echoes the status code. Never fails the script itself, so one dead
# endpoint reports as one failure rather than hiding every check after it.
fetch() {
    # One value on stdout, always. Writing the code with -w *and* echoing a fallback on failure
    # emitted "000000" and made the caller's "$code" != "200" comparison true for the wrong reason.
    : > "$tmp/body"
    _code="$(curl -sS -L --max-time 30 -o "$tmp/body" -w '%{http_code}' "$1" 2>"$tmp/err")" || _code=000
    [ -n "$_code" ] || _code=000
    echo "$_code"
}

printf '\nChecking %s\n\n' "$BASE"

# ---------------------------------------------------------------- the landing page
printf 'The root serves the demo\n'
code="$(fetch "$BASE/")"
bytes=$(wc -c < "$tmp/body" | tr -d ' ')

if [ "$code" != "200" ]; then
    fail "GET / returned $code"
elif [ "$bytes" -lt "$MIN_PAGE_BYTES" ]; then
    # Deliberately fatal for this section. Everything below is a content assertion, and content
    # assertions against a body this small are not measuring anything.
    fail "GET / returned only ${bytes}b, too small to assert against"
else
    pass "GET / is 200, ${bytes}b"
    cp "$tmp/body" "$tmp/root.html"

    grep -q "$NEEDS_MANIFEST_LINK" "$tmp/root.html" \
        && pass 'links a manifest' || fail 'no manifest link, nothing to install'

    grep -q "$NEEDS_CLIENT" "$tmp/root.html" \
        && pass 'loads the generated client' || fail 'no client script, nothing registers the worker'

    grep -q "$DEMO_HEADLINE" "$tmp/root.html" \
        && pass 'is the demo page' || fail "headline missing: $DEMO_HEADLINE"

    grep -q "$MUST_NOT" "$tmp/root.html" \
        && fail "serving Umbraco's no-published-content screen" || pass 'not the installer screen'
fi

# ---------------------------------------------------------------- the offline fallback
# The bug had two halves. NavigationFallback defaults to "/", so the worker precached the installer
# screen as the offline page. Read the fallback out of the deployed worker rather than assuming
# "/", so that changing the option keeps this honest instead of quietly making it vacuous.
printf '\nThe page the worker precaches offline\n'
code="$(fetch "$BASE/sw.js")"
if [ "$code" != "200" ]; then
    fail "GET /sw.js returned $code"
else
    pass 'GET /sw.js is 200'
    fallback="$(sed -n 's/.*NAV_FALLBACK[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "$tmp/body" | head -1)"

    if [ -z "$fallback" ]; then
        fail 'the worker declares no NAV_FALLBACK'
    else
        pass "declares a fallback: $fallback"
        code="$(fetch "$BASE$fallback")"
        bytes=$(wc -c < "$tmp/body" | tr -d ' ')

        if [ "$code" != "200" ]; then
            fail "the fallback $fallback returned $code, so offline visitors get nothing"
        elif [ "$bytes" -lt "$MIN_PAGE_BYTES" ]; then
            fail "the fallback $fallback returned only ${bytes}b"
        else
            grep -q "$MUST_NOT" "$tmp/body" \
                && fail 'the offline page is the Umbraco installer screen' \
                || pass 'the offline page is the demo'
        fi
    fi
fi

# ---------------------------------------------------------------- the manifest
printf '\nThe manifest\n'
code="$(fetch "$BASE/manifest.webmanifest")"
if [ "$code" != "200" ]; then
    fail "GET /manifest.webmanifest returned $code"
else
    start_url="$(python3 -c '
import json, sys
try:
    print(json.load(open(sys.argv[1]))["start_url"])
except Exception as e:
    print("", end="")
' "$tmp/body")"

    if [ -z "$start_url" ]; then
        fail 'the manifest does not parse, or declares no start_url'
    else
        pass "parses, start_url is $start_url"
        # An installed app opens at start_url and nothing else. A 404 here is invisible until
        # someone installs it, which is the point at which they stop trying.
        code="$(fetch "$BASE$start_url")"
        [ "$code" = "200" ] \
            && pass "start_url resolves" \
            || fail "start_url $start_url returns $code, so the installed app opens on an error"
    fi
fi

# ---------------------------------------------------------------- the rest of the surface
printf '\nEverything else the demo has to serve\n'
for path in /baryodev-pwa.js /umbraco /build-info /icon-192.png; do
    code="$(fetch "$BASE$path")"
    [ "$code" = "200" ] && pass "$path is 200" || fail "$path returned $code"
done

# build-info is what the publish gate compares against, so a malformed one breaks releases rather
# than the demo, and it breaks them at the least convenient moment.
#
# The source hash is written into the image by the Dockerfile. A local `dotnet run` has no such
# file and legitimately reports null, so it is only required of a deployed host. Requiring it
# everywhere made the local run fail for a reason that was not a defect, which is how a check
# earns the reputation that gets it ignored.
case "$BASE" in
    *localhost*|*127.0.0.1*) require_hash=0 ;;
    *)                       require_hash=1 ;;
esac

code="$(fetch "$BASE/build-info")"
if [ "$code" = "200" ]; then
    python3 -c '
import json, re, sys
d = json.load(open(sys.argv[1]))
require_hash = sys.argv[2] == "1"
h = d.get("sourceHash") or ""
if not d.get("version"):
    print("no version reported"); sys.exit(1)
if require_hash and not re.fullmatch(r"[0-9a-f]{64}", h):
    print("sourceHash is not a 64-hex digest: %r" % (h,)); sys.exit(1)
print("version %s, sourceHash %s" % (d["version"], (h[:12] + "...") if h else "(none, local run)"))
' "$tmp/body" "$require_hash" > "$tmp/bi" 2>&1 \
        && pass "build-info: $(cat "$tmp/bi")" \
        || fail "build-info: $(cat "$tmp/bi")"
fi

# ---------------------------------------------------------------- what we tell people
# The failure was never that the demo was broken. It was that the demo was fine and the URL we
# advertise did not reach it. Check the advertised URL, not the one we happen to know works.
printf '\nThe URL every listing points at\n'
if [ -n "$EXPLICIT_BASE" ]; then
    # Someone asked about a specific server: a local `dotnet run`, or the container from the VM
    # during a release. Public DNS, the certificate and nginx are all worth checking, and none of
    # them is what "did this deploy work" is asking. Left in, a release fails on a certificate
    # that expired overnight while the deploy it gated was fine. live-demo.yml passes no base, so
    # the advertised URL is still checked daily and on any PR touching the listing.
    printf '  skip  a base was given, so the advertised URL is out of scope here\n'
else
# Trailing punctuation is part of the surrounding prose, not the URL. The Marketplace
# description reads "...at https://dev-playground.baryo.dev, a real Umbraco running this
# package", and the comma made the first version of this check fetch a URL nobody published.
advertised="$(grep -ohE 'https://dev-playground\.baryo\.dev[^)"[:space:]]*' README.md umbraco-marketplace.json 2>/dev/null \
    | sed -e 's/[.,;:]*$//' | sort -u)"
if [ -z "$advertised" ]; then
    fail 'no demo URL found in README.md or umbraco-marketplace.json, so this check is measuring nothing'
else
    for url in $advertised; do
        code="$(fetch "$url")"
        bytes=$(wc -c < "$tmp/body" | tr -d ' ')
        if [ "$code" != "200" ] || [ "$bytes" -lt "$MIN_PAGE_BYTES" ]; then
            fail "advertised $url returned $code, ${bytes}b"
        elif grep -q "$MUST_NOT" "$tmp/body"; then
            fail "advertised $url shows the Umbraco installer screen"
        else
            pass "advertised $url reaches the demo"
        fi
    done
fi
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
    printf 'All checks passed against %s\n\n' "$BASE"
else
    printf '%s check(s) failed against %s\n\n' "$failures" "$BASE"
fi
exit "$([ "$failures" -eq 0 ] && echo 0 || echo 1)"
