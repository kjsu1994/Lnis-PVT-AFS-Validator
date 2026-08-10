#!/usr/bin/env bash
set -euo pipefail
STAGE="${1:?staging root is required}"
x86_64-w64-mingw32-gcc -O2 -shared -static-libgcc \
  -I"$STAGE/lans/ldpc" -I"$STAGE/lans/rtklib" -I"$STAGE/lans/pocketsdr" \
  "$STAGE/wrapper/lnis_afs_codec.c" "$STAGE/lans/afs_nav.c" \
  "$STAGE/lans/rtklib/rtkcmn.c" "$STAGE/lans/pocketsdr/pocketsdr.c" \
  "$STAGE/pocketlib/libsdr.a" "$STAGE/pocketlib/libldpc.a" -lm \
  -o "$STAGE/LnisAfsCodec.dll"
