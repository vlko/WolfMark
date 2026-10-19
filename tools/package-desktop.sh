#!/usr/bin/env bash
# Packs a Native AOT publish of src/WolfMark.Desktop for download.
#   linux-*: WolfMark-<version>-<rid>.tar.gz  (wolfmark binary, native libraries, .desktop entry, icon)
#   osx-*:   WolfMark-<version>-<rid>.zip     (WolfMark.app, ad-hoc signed – Apple Silicon runs only signed code)
# Usage: tools/package-desktop.sh <rid> <version> <publish-dir> <out-dir>
set -euo pipefail
rid=$1 version=$2 publish=$3 out=$4
root=$(cd "$(dirname "$0")/.." && pwd)
icon="$root/src/WolfMark.App/Assets/WolfMark-256.png"
mkdir -p "$out"
out=$(cd "$out" && pwd)
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

# Debug symbols stay out of the download.
find "$publish" \( -name '*.dbg' -o -name '*.pdb' -o -name '*.dSYM' \) -prune -exec rm -rf {} +

case "$rid" in
  linux-*)
    dir="$stage/WolfMark-$version"
    mkdir -p "$dir"
    cp -R "$publish"/. "$dir"/
    mv "$dir/WolfMark.Desktop" "$dir/wolfmark"
    cp "$icon" "$dir/wolfmark.png"
    cat > "$dir/wolfmark.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=WolfMark
Comment=Daily notes in Markdown
Exec=wolfmark
Icon=wolfmark
Categories=Office;Utility;
StartupWMClass=WolfMark.Desktop
DESKTOP
    cat > "$dir/INSTALL.txt" <<'TXT'
WolfMark for Linux (preview)

Run ./wolfmark from this folder. To get a menu entry:
  mkdir -p ~/.local/bin ~/.local/share/applications ~/.local/share/icons/hicolor/256x256/apps
  cp -R . ~/.local/share/wolfmark && ln -sf ~/.local/share/wolfmark/wolfmark ~/.local/bin/wolfmark
  cp wolfmark.png ~/.local/share/icons/hicolor/256x256/apps/
  cp wolfmark.desktop ~/.local/share/applications/
TXT
    tar -C "$stage" -czf "$out/WolfMark-$version-$rid.tar.gz" "WolfMark-$version"
    ;;
  osx-*)
    app="$stage/WolfMark.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish"/. "$app/Contents/MacOS"/
    mv "$app/Contents/MacOS/WolfMark.Desktop" "$app/Contents/MacOS/WolfMark"
    # Icon: iconutil wants an .iconset of sizes; sips scales the 256 px master.
    set=$stage/WolfMark.iconset
    mkdir -p "$set"
    for s in 16 32 64 128 256; do
      sips -z $s $s "$icon" --out "$set/icon_${s}x${s}.png" >/dev/null
      d=$((s / 2)); [ $d -ge 16 ] && cp "$set/icon_${s}x${s}.png" "$set/icon_${d}x${d}@2x.png"
    done
    iconutil -c icns "$set" -o "$app/Contents/Resources/WolfMark.icns"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>WolfMark</string>
  <key>CFBundleDisplayName</key><string>WolfMark</string>
  <key>CFBundleIdentifier</key><string>net.vlko.wolfmark</string>
  <key>CFBundleExecutable</key><string>WolfMark</string>
  <key>CFBundleIconFile</key><string>WolfMark</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${version%%-*}</string>
  <key>CFBundleVersion</key><string>${version%%-*}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    codesign --force --deep --sign - "$app"
    (cd "$stage" && ditto -c -k --keepParent WolfMark.app "$out/WolfMark-$version-$rid.zip")
    ;;
  *)
    echo "unsupported rid: $rid" >&2
    exit 1
    ;;
esac
ls -la "$out"
