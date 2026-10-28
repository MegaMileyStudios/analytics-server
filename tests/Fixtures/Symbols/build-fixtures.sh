#!/usr/bin/env bash
# Builds the symbol fixtures for the symbolication tests from fixture.c, the way the game's CI archives them:
#   game-linux-x64-symbols.tar.gz   linux-x64/Game.so (stripped) + linux-x64/Game.so.dbg
#   game-win-x64-symbols.tar.gz     win-x64/Game.dll + win-x64/Game.pdb
#   game-osx-arm64-symbols.tar.gz   osx-arm64/Game.dylib + osx-arm64/Game.dylib.dSYM
# and fixtures.json with each file's debug ID and the address of every function.
#
# Needs clang-18, lld-18 (lld-link-18, ld64.lld-18), llvm-18 and binutils. Run it from anywhere; the outputs are committed.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp "$here/fixture.c" "$work/"
cd "$work"

# Debug info gets the CI checkout prefixes, so the tests also cover stripping them
linux_prefix=/workspace/MegaMileyStudios/Game
windows_prefix='C:\Users\NAS\.cache\act\0123456789abcdef\hostexecutor'
macos_prefix=/Users/runner/.cache/act/0123456789abcdef/hostexecutor

# Linux: shared library with a GNU build ID, split into a stripped library and its .dbg
mkdir -p linux-x64
clang-18 --target=x86_64-linux-gnu -g -O0 -fPIC -shared -Wl,--build-id=sha1 \
    -fdebug-prefix-map="$work=$linux_prefix" -nostdlib -o linux-x64/Game.so fixture.c
llvm-objcopy-18 --only-keep-debug linux-x64/Game.so linux-x64/Game.so.dbg
llvm-objcopy-18 --strip-all linux-x64/Game.so
linux_id=$(readelf -n linux-x64/Game.so.dbg | sed -n 's/.*Build ID: \([0-9a-f]*\).*/\1/p')

# Windows: DLL with a PDB
mkdir -p win-x64
clang-18 --target=x86_64-pc-windows-msvc -g -gcodeview -O0 -c -fdebug-prefix-map="$work=$windows_prefix" -o fixture.obj fixture.c
lld-link-18 /dll /noentry /nodefaultlib /debug /pdb:win-x64/Game.pdb /out:win-x64/Game.dll fixture.obj
windows_guid=$(llvm-pdbutil-18 dump --summary win-x64/Game.pdb | sed -n 's/.*GUID: {\([0-9A-F-]*\)}.*/\1/p' | tr -d -)
windows_age=$(llvm-pdbutil-18 dump --summary win-x64/Game.pdb | sed -n 's/^ *Age: \([0-9]*\).*/\1/p')
windows_id="${windows_guid}$(printf '%X' "$windows_age")"

# macOS: dylib with a dSYM bundle
mkdir -p osx-arm64
clang-18 --target=arm64-apple-macos11 -g -O0 -c -fdebug-prefix-map="$work=$macos_prefix" -o fixture.o fixture.c
ld64.lld-18 -dylib -arch arm64 -platform_version macos 11.0 11.0 -undefined dynamic_lookup -o osx-arm64/Game.dylib fixture.o
dsymutil-18 osx-arm64/Game.dylib -o osx-arm64/Game.dylib.dSYM
macos_id=$(llvm-dwarfdump-18 --uuid osx-arm64/Game.dylib.dSYM | sed -n 's/UUID: \([0-9A-F-]*\) (arm64).*/\1/p' | tr -d -)

# Addresses as llvm-symbolizer expects them: virtual addresses for ELF and Mach-O, RVAs for PE
functions_elf() { llvm-nm-18 --defined-only "$1" | awk '$3 ~ /^(Game_|godotsharp)/ { printf "\"%s\": \"0x%s\", ", $3, $1 }'; }
functions_macho() { llvm-nm-18 --defined-only "$1" | awk '$3 ~ /^_(Game_|godotsharp)/ { printf "\"%s\": \"0x%s\", ", substr($3, 2), $1 }'; }
functions_pdb() {
    local text_rva
    text_rva=$(llvm-pdbutil-18 dump --section-headers "$1" | awk '/SECTION HEADER #1$/ { found = 1 } found && /virtual address/ { print $1; exit }')
    llvm-pdbutil-18 dump --publics "$1" | awk -v base="$((16#$text_rva))" '
        /S_PUB32/ { match($0, /`[^`]+`/); name = substr($0, RSTART + 1, RLENGTH - 2) }
        /addr = 0001:/ && name ~ /^(Game_|godotsharp)/ { split($0, parts, "0001:"); printf "\"%s\": \"0x%x\", ", name, base + parts[2]; name = "" }'
}
json_object() { local body="$1"; echo "{ ${body%, } }"; }

for platform in linux-x64 win-x64 osx-arm64; do
    COPYFILE_DISABLE=1 tar -czf "$here/game-$platform-symbols.tar.gz" --owner=0 --group=0 --sort=name --mtime='2026-01-01' "$platform"
done

cat > "$here/fixtures.json" <<JSON
{
  "linux-x64": { "debugId": "$linux_id", "functions": $(json_object "$(functions_elf linux-x64/Game.so.dbg)") },
  "win-x64": { "debugId": "$windows_id", "functions": $(json_object "$(functions_pdb win-x64/Game.pdb)") },
  "osx-arm64": { "debugId": "$macos_id", "functions": $(json_object "$(functions_macho osx-arm64/Game.dylib.dSYM/Contents/Resources/DWARF/Game.dylib)") }
}
JSON

echo "Fixtures written to $here"
cat "$here/fixtures.json"
