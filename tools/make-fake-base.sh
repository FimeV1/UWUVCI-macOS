#!/bin/bash
# Creates a stand-in Wii U base (code/content/meta) for exercising the injector without a real base dump.
# The result has the right shape but none of Nintendo's files, so injects made from it will not boot.
# usage: tools/make-fake-base.sh <output-folder> <wii|nes|snes|n64|nds|gba|msx|tg16>
set -euo pipefail
OUT="$1"; KIND="${2:-wii}"
rm -rf "$OUT"; mkdir -p "$OUT/code" "$OUT/content" "$OUT/meta"

cat > "$OUT/code/app.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<app type="complex" access="777">
  <version type="unsignedInt" length="4">16</version>
  <os_version type="hexBinary" length="8">000500101000400A</os_version>
  <title_id type="hexBinary" length="8">0005000010101010</title_id>
  <title_version type="hexBinary" length="2">0000</title_version>
  <sdk_version type="unsignedInt" length="4">21204</sdk_version>
  <app_type type="hexBinary" length="4">80000000</app_type>
  <group_id type="hexBinary" length="4">00001010</group_id>
  <os_mask type="hexBinary" length="32">0000000000000000000000000000000000000000000000000000000000000000</os_mask>
  <common_id type="hexBinary" length="8">0000000000000000</common_id>
</app>
XML
echo '<?xml version="1.0" encoding="utf-8"?><app type="complex" access="777"><argstr type="string" length="4096">game.rpx</argstr></app>' > "$OUT/code/cos.xml"

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<menu type="complex" access="777">'
  echo '  <product_code type="string" length="32">WUP-N-TEST</product_code>'
  echo '  <title_id type="hexBinary" length="8">0005000010101010</title_id>'
  echo '  <group_id type="hexBinary" length="4">00001010</group_id>'
  echo '  <drc_use type="unsignedInt" length="4">1</drc_use>'
  echo '  <reserved_flag2 type="hexBinary" length="4">00000000</reserved_flag2>'
  for l in ja en fr de it es zhs ko nl pt ru zht; do
    echo "  <longname_$l type=\"string\" length=\"512\">Base</longname_$l>"
    echo "  <shortname_$l type=\"string\" length=\"256\">Base</shortname_$l>"
  done
  echo '</menu>'
} > "$OUT/meta/meta.xml"

case "$KIND" in
  wii)
    head -c 16 /dev/urandom > "$OUT/code/htk.bin"
    head -c 2097152 /dev/urandom > "$OUT/code/fw.img"
    head -c 1024 /dev/urandom > "$OUT/code/rvlt.tmd"
    head -c 1024 /dev/urandom > "$OUT/content/hif_000000.nfs"
    ;;
  n64)
    mkdir -p "$OUT/content/rom" "$OUT/content/config"
    head -c 4096 /dev/urandom > "$OUT/content/rom/UNMXE0.716"
    echo "; ini" > "$OUT/content/config/UNMXE0.716.ini"
    ;;
  nds)
    mkdir -p "$OUT/content/0010"
    head -c 4096 /dev/urandom > "$OUT/WUP-N-DAAE.srl"
    (cd "$OUT" && zip -q content/0010/rom.zip WUP-N-DAAE.srl && rm WUP-N-DAAE.srl)
    echo '{"configuration":{"Display":{"Brightness":80,"PixelArtUpscaler":0,"RenderScale":1}}}' > "$OUT/content/0010/configuration_cafe.json"
    ;;
  msx)
    mkdir -p "$OUT/content/msx"
    head -c 400000 /dev/urandom > "$OUT/content/msx/msx.pkg"
    ;;
  tg16)
    mkdir -p "$OUT/content/pceemu"
    head -c 1000 /dev/urandom > "$OUT/content/pceemu/pce.pkg"
    ;;
  *)
    echo dummy > "$OUT/content/dummy.bin"
    ;;
esac
echo "fake $KIND base in $OUT"
