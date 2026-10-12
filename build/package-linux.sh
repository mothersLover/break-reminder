#!/usr/bin/env bash
# Пакует результат dotnet publish в tar.gz с установщиком.
# usage: package-linux.sh <publish-dir> <rid> <out-dir>
set -euo pipefail

PUB="$1"; RID="$2"; OUT="$3"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAGE="$(mktemp -d)/BreakReminder"

mkdir -p "$STAGE"
find "$PUB" -maxdepth 1 -type f ! -name '*.pdb' -exec cp {} "$STAGE/" \;
chmod +x "$STAGE/BreakReminder"
cp "$ROOT/src/BreakReminder.App/Assets/icon.png" "$STAGE/break-reminder.png"

cat > "$STAGE/install.sh" <<'EOF'
#!/usr/bin/env bash
# Ставит Отдыхалку для текущего пользователя: ~/.local/share/break-reminder + ярлык в меню приложений.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/break-reminder"
mkdir -p "$DEST" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/512x512/apps"
cp -f "$HERE"/* "$DEST/"
ln -sf "$DEST/BreakReminder" "$HOME/.local/bin/break-reminder"
cp -f "$HERE/break-reminder.png" "$HOME/.local/share/icons/hicolor/512x512/apps/break-reminder.png"
cat > "$HOME/.local/share/applications/break-reminder.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Отдыхалка
Comment=Напоминания о перерывах
Exec="$DEST/BreakReminder"
Icon=break-reminder
Terminal=false
Categories=Utility;
DESK
echo "Готово. Отдыхалка есть в меню приложений; из терминала: ~/.local/bin/break-reminder"
echo "Горячая клавиша паузы: Настройки → Клавиатура → Свои сочетания → команда:"
echo "  $DEST/BreakReminder --toggle-pause"
EOF
chmod +x "$STAGE/install.sh"

mkdir -p "$OUT"
tar -C "$(dirname "$STAGE")" -czf "$OUT/BreakReminder-$RID.tar.gz" BreakReminder
echo "OK: $OUT/BreakReminder-$RID.tar.gz"
