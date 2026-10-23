#!/bin/sh
# Build the Linux x64 ICU runtime and the same filtered data as Build-IcuData.ps1.
# Requires a Linux x64 host, g++, make, curl, binutils and standard Unix utilities.
set -eu

script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
compiler_target=$(
    # CXX may include a compiler wrapper or flags. Split words without globbing.
    set -f
    # shellcheck disable=SC2086
    ${CXX:-g++} -dumpmachine
)
output="$script_dir/../../Native/icu78/$compiler_target"
work="${TMPDIR:-/tmp}/mht-icu78-linux"
jobs=$(getconf _NPROCESSORS_ONLN)
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output) output=$2; shift 2 ;;
        --work) work=$2; shift 2 ;;
        --jobs) jobs=$2; shift 2 ;;
        *) echo "Usage: sh $0 [--output DIR] [--work DIR] [--jobs N]" >&2; exit 2 ;;
    esac
done
if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
    echo 'Run this build on Linux x64 with a toolchain matching the publish target.' >&2
    exit 1
fi
mkdir -p -- "$work" "$output"
work=$(CDPATH='' cd -- "$work" && pwd)
output=$(CDPATH='' cd -- "$output" && pwd)

release=78.3
archive="$work/icu4c-$release-sources.tgz"
if [ ! -f "$archive" ]; then
    curl -fL --retry 3 -o "$archive" "https://github.com/unicode-org/icu/releases/download/release-$release/icu4c-$release-sources.tgz"
fi
printf '%s  %s\n' 3a2e7a47604ba702f345878308e6fefeca612ee895cf4a5f222e7955fabfe0c0 "$archive" | sha256sum -c -
mkdir -p -- "$work/source" "$work/build-$compiler_target"
if [ ! -f "$work/source/source/configure" ]; then
    tar -xzf "$archive" --strip-components=1 -C "$work/source"
fi
build="$work/build-$compiler_target"
if [ ! -f "$build/Makefile" ]; then
    (cd -- "$build" && ../source/source/configure --disable-tests --disable-samples --disable-extras --disable-static --with-data-packaging=library)
fi
make -C "$build" -j"$jobs"

lao="$work/chromium-laodict.txt"
revision=6ebb40c594776cc2c21ea14df85a2a89a328b364
if [ ! -s "$lao" ]; then
    curl -fL --retry 3 -o "$work/chromium-laodict.base64" "https://chromium.googlesource.com/chromium/deps/icu/+/$revision/source/data/brkitr/dictionaries/laodict.txt?format=TEXT"
    base64 -d "$work/chromium-laodict.base64" > "$lao.tmp"
    mv -- "$lao.tmp" "$lao"
fi
printf '%s  %s\n' d2c4f10b0e80abaa59ef7dcdac0d1adedb785e2f4f40f4f7ff79f87e36bb55f2 "$lao" | sha256sum -c -

# A fresh staging directory prevents stale items surviving changes to items.txt.
stage=$(mktemp -d "$work/filtered.XXXXXXXX")
mkdir -p -- "$stage/items" "$stage/tmp" "$stage/lib"
awk '{ sub(/\r$/, ""); if (length && $0 !~ /^#/) print }' "$script_dir/items.txt" > "$stage/items.txt"

icu_tool() {
    icu_program="$build/bin/$1"
    shift
    env LD_LIBRARY_PATH="$build/lib:$build/stubdata${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" "$icu_program" "$@"
}
icu_tool icupkg -x "$stage/items.txt" -d "$stage/items" "$work/source/source/data/in/icudt78l.dat"
icu_tool gendict --bytes --transform offset-0e80 "$lao" "$stage/items/brkitr/laodict.dict"
icu_tool pkgdata -O "$build/data/icupkg.inc" -q -m static -p icudt78l -e icudt78 -L icudata -r "$release" \
    -s "$stage/items" -d "$stage/lib" -T "$stage/tmp" "$stage/items.txt"

# libicuuc with custom data. Its SONAME differs from a system libicuuc.so.78's
${CXX:-g++} -O2 -shared -Wl,-soname,libmht-icuuc.so.78 -Wl,-Bsymbolic -Wl,-z,defs \
    -Wl,--exclude-libs,libicudata.a -o "$output/libicuuc.so.78" "$build"/common/*.o \
    -Wl,--whole-archive "$stage/lib/libicudata.a" -Wl,--no-whole-archive -lpthread -lm
rm -f -- "$output/libicudata.so.78"
strip --strip-unneeded "$output/libicuuc.so.78"
sha256sum "$output/libicuuc.so.78"
