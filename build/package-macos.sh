#!/usr/bin/env bash
# Собирает BreakReminder.app из результата dotnet publish и пакует в zip.
# usage: package-macos.sh <publish-dir> <rid> <version> <out-dir>
set -euo pipefail

PUB="$1"; RID="$2"; VER="$3"; OUT="$4"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$OUT/BreakReminder.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# всё из publish, кроме отладочных символов
find "$PUB" -maxdepth 1 -type f ! -name '*.pdb' -exec cp {} "$APP/Contents/MacOS/" \;
chmod +x "$APP/Contents/MacOS/BreakReminder"

# иконка .icns из icon.png
ICONSET="$(mktemp -d)/icon.iconset"
mkdir -p "$ICONSET"
SRC="$ROOT/src/BreakReminder.App/Assets/icon.png"
for s in 16 32 128 256 512; do
  sips -z $s $s "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2)); [ $d -le 512 ] && sips -z $d $d "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/icon.icns"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>BreakReminder</string>
  <key>CFBundleDisplayName</key><string>Отдыхалка</string>
  <key>CFBundleIdentifier</key><string>com.breakreminder.app</string>
  <key>CFBundleExecutable</key><string>BreakReminder</string>
  <key>CFBundleIconFile</key><string>icon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VER</string>
  <key>CFBundleVersion</key><string>$VER</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Ad-hoc подпись: без неё Apple Silicon не запустит бинарник вовсе.
codesign --force --deep -s - "$APP"

mkdir -p "$OUT"
(cd "$OUT" && ditto -c -k --keepParent BreakReminder.app "BreakReminder-$RID.zip")
rm -rf "$APP"
echo "OK: $OUT/BreakReminder-$RID.zip"
