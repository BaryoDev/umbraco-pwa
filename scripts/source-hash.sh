#!/usr/bin/env sh
#
# A deterministic hash of the package source.
#
# The publish gate compares what the playground is running against what is about to be published.
# It used to compare assembly MVIDs, which cannot work here: the playground builds from source in
# Docker with no `.git` present, while CI packs on a runner where SourceLink embeds repository
# metadata. Same source, different assembly, different MVID, and no way to reconcile the two.
#
# This hashes the source instead, which is what the gate actually means to ask about. Both sides
# run this same script, so the answer cannot drift between them.
#
# POSIX sh on purpose: it runs inside the dotnet SDK image and on a GitHub runner.
set -eu

ROOT=${1:-src/BaryoDev.Umbraco.Pwa}

[ -d "$ROOT" ] || { echo "source-hash: no such directory: $ROOT" >&2; exit 1; }

if command -v sha256sum >/dev/null 2>&1; then
  digest() { sha256sum | cut -d' ' -f1; }
else
  # macOS, for running this by hand
  digest() { shasum -a 256 | cut -d' ' -f1; }
fi

# Build outputs are excluded because they are not source and are not synced to the VM. AppleDouble
# files are excluded because a macOS rsync can carry them and a Linux checkout never has them.
#
# LC_ALL=C so the ordering is the same everywhere rather than depending on the machine's locale,
# and each path goes into the digest before its contents, so a rename changes the hash.
#
# Paths are recorded relative to ROOT, so the answer does not depend on where the tree happens to
# sit. The image builds it at /src and CI builds it at the workspace root; without this they would
# hash the same files to different values and the gate would fail for no reason.
find "$ROOT" -type f \
    ! -path '*/bin/*' \
    ! -path '*/obj/*' \
    ! -name '._*' \
  | LC_ALL=C sort \
  | while IFS= read -r file; do
      printf '%s\n' "${file#"$ROOT"/}"
      cat "$file"
    done \
  | digest
