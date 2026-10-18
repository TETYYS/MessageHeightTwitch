#!/usr/bin/env bash
# Build the Linux x64 ICU runtime and the same filtered data as Build-IcuData.ps1.
# Requires a glibc Linux x64 host, g++, make, curl, binutils and coreutils.
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
output="$script_dir/../../Native/icu78/linux-x64"
work="${TMPDIR:-/tmp}/mht-icu78-linux"
jobs=$(getconf _NPROCESSORS_ONLN)
while (($#)); do
    case "$1" in
        --output) output=$2; shift 2 ;;
        --work) work=$2; shift 2 ;;
        --jobs) jobs=$2; shift 2 ;;
        *) echo "Usage: bash $0 [--output DIR] [--work DIR] [--jobs N]" >&2; exit 2 ;;
    esac
done
if [[ $(uname -s) != Linux || $(uname -m) != x86_64 || ! -x /lib64/ld-linux-x86-64.so.2 ]]; then
    echo 'Run this build on glibc Linux x64 (or WSL), matching the linux-x64 publish target.' >&2
    exit 1
fi
mkdir -p -- "$work" "$output"
work=$(cd -- "$work" && pwd)
output=$(cd -- "$output" && pwd)

release=78.3
archive="$work/icu4c-$release-sources.tgz"
if [[ ! -f "$archive" ]]; then
    curl -fL --retry 3 -o "$archive" "https://github.com/unicode-org/icu/releases/download/release-$release/icu4c-$release-sources.tgz"
fi
printf '%s  %s\n' 3a2e7a47604ba702f345878308e6fefeca612ee895cf4a5f222e7955fabfe0c0 "$archive" | sha256sum -c -
mkdir -p -- "$work/source" "$work/build"
if [[ ! -f "$work/source/source/configure" ]]; then
    tar -xzf "$archive" --strip-components=1 -C "$work/source"
fi
build="$work/build"
if [[ ! -f "$build/Makefile" ]]; then
    (cd -- "$build" && ../source/source/configure --disable-tests --disable-samples --disable-extras --disable-static --with-data-packaging=library)
fi
make -C "$build" -j"$jobs"

lao="$work/chromium-laodict.txt"
revision=6ebb40c594776cc2c21ea14df85a2a89a328b364
if [[ ! -f "$lao" ]]; then
    curl -fL --retry 3 "https://chromium.googlesource.com/chromium/deps/icu/+/$revision/source/data/brkitr/dictionaries/laodict.txt?format=TEXT" | base64 --decode > "$lao"
fi
printf '%s  %s\n' d2c4f10b0e80abaa59ef7dcdac0d1adedb785e2f4f40f4f7ff79f87e36bb55f2 "$lao" | sha256sum -c -

# A fresh staging directory prevents stale items surviving changes to items.txt.
stage=$(mktemp -d "$work/filtered.XXXXXXXX")
mkdir -p -- "$stage/items" "$stage/tmp" "$stage/lib"
awk '{ sub(/\r$/, ""); if (length && $0 !~ /^#/) print }' "$script_dir/items.txt" > "$stage/items.txt"

# Invoke the build tools through the ELF loader with an explicit dependency path,
# without setting LD_LIBRARY_PATH. These paths are used only by the build tools.
icu_tool() {
    /lib64/ld-linux-x86-64.so.2 --library-path "$build/lib:$build/stubdata" "$build/bin/$@"
}
icu_tool icupkg -x "$stage/items.txt" -d "$stage/items" "$work/source/source/data/in/icudt78l.dat"
icu_tool gendict --bytes --transform offset-0e80 "$lao" "$stage/items/brkitr/laodict.dict"
icu_tool pkgdata -O "$build/data/icupkg.inc" -q -m dll -p icudt78l -e icudt78 -L icudata -r "$release" \
    -s "$stage/items" -d "$stage/lib" -T "$stage/tmp" "$stage/items.txt"

# Use real files with the dependency SONAMEs: publishing does not need symlinks,
# ldconfig, an RPATH patch, or a changed process environment.
cp -L -- "$stage/lib/libicudata.so.78" "$output/libicudata.so.78"
cp -L -- "$build/lib/libicuuc.so.78" "$output/libicuuc.so.78"
strip --strip-unneeded "$output/libicudata.so.78" "$output/libicuuc.so.78"
sha256sum "$output/libicudata.so.78" "$output/libicuuc.so.78"
