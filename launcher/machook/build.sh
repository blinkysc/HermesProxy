#!/usr/bin/env bash
# Builds wotlk343hook.dylib (universal arm64 + x86_64) on Linux with clang + ld64.lld, no macOS SDK:
# the .tbd stubs here stand in for the system libraries it links against.
set -euo pipefail
cd "$(dirname "$0")"
for a in arm64 x86_64; do
  clang -target $a-apple-macos11 -O2 -fno-stack-protector -fvisibility=hidden -c hook.c -o hook_$a.o
  ld64.lld -arch $a -dylib -platform_version macos 11.0 14.0 -install_name @rpath/wotlk343hook.dylib \
    -o hook_$a.dylib hook_$a.o libSystem.tbd Security.tbd CoreFoundation.tbd
done
python3 fat.py wotlk343hook.dylib hook_arm64.dylib hook_x86_64.dylib
rm -f hook_*.o hook_*.dylib
