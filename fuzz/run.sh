#!/usr/bin/env bash
# Build, instrument and run the fuzz target under libFuzzer.
#
#   fuzz/run.sh <target> <seconds> [corpus dir] [extra libFuzzer flags...]
#   fuzz/run.sh replay <target> <file>...      # run saved inputs once, without libFuzzer (reproduce a finding)
#
# Targets: shape   (see fuzz/README.md)
#
# Environment (nothing is baked into the script):
#   ONEFONT_FUZZ_WORK    required: where builds, corpora and findings go. Keep it outside the checkout.
#   ONEFONT_FUZZ_TOOLS   directory holding `fuzz/libfuzzer-dotnet` (the libFuzzer host) and `sharpfuzz/sharpfuzz`
#                        (the instrumenter). LIBFUZZER_DOTNET and SHARPFUZZ override the two paths individually.
#   ONEFONT_ARTIFACTS    build output root, as for every other project in the repo
#   DOTNET               the dotnet executable (default: dotnet from PATH)
#   FUZZ_TIMEOUT         per-input timeout in seconds (default 10)
#   FUZZ_RSS_MB          memory limit in MB (default 1024)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${DOTNET:-dotnet}"
work="${ONEFONT_FUZZ_WORK:?set ONEFONT_FUZZ_WORK to a scratch directory outside the checkout}"
case "$(readlink -f "$work")/" in
  "$root"/*) echo "ONEFONT_FUZZ_WORK must be outside the checkout" >&2; exit 2 ;;
esac
tools="${ONEFONT_FUZZ_TOOLS:-}"
host="${LIBFUZZER_DOTNET:-$tools/fuzz/libfuzzer-dotnet}"
instrument="${SHARPFUZZ:-$tools/sharpfuzz/sharpfuzz}"

mode=fuzz
if [[ "${1:-}" == replay ]]; then
  mode=replay
  shift
fi
target="${1:?usage: run.sh <target> <seconds> [corpus dir] | run.sh replay <target> <file>...}"
shift

# Maximum input length per target.
case "$target" in
  shape) maxlen=262144 ;;
  *) echo "unknown target '$target'" >&2; exit 2 ;;
esac

bin="$work/bin"
rm -rf "$bin"
mkdir -p "$bin"

# Build once per run: release, then instrument only the library assembly (the harness itself needs no coverage).
"$dotnet_bin" build "$root/fuzz/OneFont.Fuzz/OneFont.Fuzz.csproj" -c Release -v quiet -clp:NoSummary -o "$bin"

export ONEFONT_FUZZ_FONTS="$root/tests/testdata/fonts"

if [[ "$mode" == replay ]]; then
  ONEFONT_FUZZ_WORK="$work" "$dotnet_bin" "$bin/OneFont.Fuzz.dll" replay "$target" "$@"
  exit $?
fi

seconds="${1:?usage: run.sh <target> <seconds> [corpus dir]}"
shift
extra=""
if [[ $# -gt 0 && "${1:0:1}" != "-" ]]; then
  extra="$1"
  shift
fi
[[ -x "$host" ]] || { echo "libFuzzer host not found at '$host' (set ONEFONT_FUZZ_TOOLS)" >&2; exit 2; }
[[ -x "$instrument" ]] || { echo "sharpfuzz not found at '$instrument' (set ONEFONT_FUZZ_TOOLS)" >&2; exit 2; }

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v "$dotnet_bin")")")}"
corpus="$work/corpus/$target"
findings="$work/findings/$target"
mkdir -p "$corpus" "$findings"

# Seeds come from the uninstrumented build (instrumented code needs libFuzzer's shared memory).
"$dotnet_bin" "$bin/OneFont.Fuzz.dll" seeds "$target" "$corpus" "$ONEFONT_FUZZ_FONTS" >/dev/null
[[ -n "$(ls -A "$corpus")" ]] || printf '\x00' >"$corpus/seed-empty"

"$instrument" "$bin/OneFont.dll" >/dev/null

# An extra seed directory is read-only for libFuzzer: new finds go to $corpus.
dirs=("$corpus")
[[ -z "$extra" ]] || dirs+=("$extra")

# Run. -max_total_time bounds the run; crashes, hangs and out-of-memory inputs are written to the findings folder.
ONEFONT_FUZZ_TARGET="$target" ONEFONT_FUZZ_WORK="$work" "$host" \
  --target_path="$(command -v "$dotnet_bin")" --target_arg="$bin/OneFont.Fuzz.dll" \
  -max_total_time="$seconds" -timeout="${FUZZ_TIMEOUT:-10}" -rss_limit_mb="${FUZZ_RSS_MB:-1024}" \
  -max_len="$maxlen" -artifact_prefix="$findings/" -print_final_stats=1 "$@" "${dirs[@]}"
