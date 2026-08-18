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
  file_digest()   { sha256sum "$1" | cut -d' ' -f1; }
  stream_digest() { sha256sum      | cut -d' ' -f1; }
else
  # macOS, for running this by hand
  file_digest()   { shasum -a 256 "$1" | cut -d' ' -f1; }
  stream_digest() { shasum -a 256      | cut -d' ' -f1; }
fi

# Build outputs are excluded because they are not source and are not synced to the VM. AppleDouble
# files are excluded because a macOS rsync can carry them and a Linux checkout never has them.
#
# Each file becomes one fixed-width record: its own digest, two spaces, then its path relative to
# ROOT. Hashing per file rather than streaming the bytes is what makes the format unambiguous.
# Concatenating "path\n" with raw contents is not: with files a and b, a="x" b="b\nz" and
# a="xb\n" b="z" both produce "a\nxb\nb\nz", so two different trees could agree and the gate
# would pass a playground running different source. A 64-character prefix cannot be confused with
# the content that follows.
#
# Paths are relative to ROOT so the answer does not depend on where the tree sits: the image hashes
# at /src and CI hashes at the workspace root. LC_ALL=C so the ordering does not depend on locale.
# A path is part of its record, so a rename changes the result.
#
# Paths containing a newline are not handled and never occur here; `find | read` could not carry
# them either.
find "$ROOT" -type f \
    ! -path '*/bin/*' \
    ! -path '*/obj/*' \
    ! -name '._*' \
  | LC_ALL=C sort \
  | while IFS= read -r file; do
      printf '%s  %s\n' "$(file_digest "$file")" "${file#"$ROOT"/}"
    done \
  | stream_digest
