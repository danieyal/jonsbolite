#!/usr/bin/env sh
#
# Installs JonsboLite from the release tarball.
#
#   ./install.sh              install for the current user (~/.local/bin)
#   ./install.sh --uninstall  remove it again
#
# The binary goes in your home directory, but the udev rule has to live in /etc, so this
# asks for sudo once for that step alone. Everything else runs unprivileged — JonsboLite
# itself never needs root on Linux.

set -eu

PREFIX="${PREFIX:-$HOME/.local}"
BIN_DIR="$PREFIX/bin"
UNIT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
UDEV_RULE=/etc/udev/rules.d/99-jonsbolite.rules
# CDPATH='' rather than CDPATH= : both clear it for this one command, but the bare form
# reads as a stray assignment to shellcheck (SC1007).
SRC="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

# Prefer sudo, fall back to doas, and cope with already being root.
as_root() {
    if [ "$(id -u)" = 0 ]; then "$@"
    elif command -v sudo >/dev/null 2>&1; then sudo "$@"
    elif command -v doas >/dev/null 2>&1; then doas "$@"
    else die "need root for $1 — install sudo, or re-run this script as root"
    fi
}

uninstall() {
    say "==> stopping service"
    systemctl --user disable --now jonsbolite.service 2>/dev/null || true

    say "==> removing files"
    rm -f "$BIN_DIR/jonsbolite" "$UNIT_DIR/jonsbolite.service"
    systemctl --user daemon-reload 2>/dev/null || true

    if [ -f "$UDEV_RULE" ]; then
        say "==> removing udev rule (needs root)"
        as_root rm -f "$UDEV_RULE"
        as_root udevadm control --reload-rules || true
    fi

    say ""
    say "Done. Config left in ${XDG_CONFIG_HOME:-$HOME/.config}/jonsbolite/ — delete it if you want it gone."
}

install_all() {
    [ -f "$SRC/jonsbolite" ] || die "jonsbolite binary not found next to this script"

    say "==> installing binary to $BIN_DIR"
    mkdir -p "$BIN_DIR"
    install -m 0755 "$SRC/jonsbolite" "$BIN_DIR/jonsbolite"

    say "==> installing udev rule (needs root)"
    as_root install -m 0644 "$SRC/99-jonsbolite.rules" "$UDEV_RULE"
    as_root udevadm control --reload-rules
    as_root udevadm trigger --subsystem-match=hidraw || true

    if command -v systemctl >/dev/null 2>&1; then
        say "==> installing systemd user unit to $UNIT_DIR"
        mkdir -p "$UNIT_DIR"
        sed "s|^ExecStart=.*|ExecStart=$BIN_DIR/jonsbolite --quiet|" \
            "$SRC/jonsbolite.service" > "$UNIT_DIR/jonsbolite.service"
        systemctl --user daemon-reload
    else
        say "==> no systemd found, skipping the service unit"
    fi

    say ""
    say "Installed. Replug the panel so the new udev rule applies, then:"
    say ""
    say "    $BIN_DIR/jonsbolite --show-binding    # check which sensors were picked"
    say "    systemctl --user enable --now jonsbolite"
    say ""
    say "If fan, pump or Vcore read 0, the SuperIO driver is probably not loaded:"
    say "    sudo modprobe nct6683 force=1        # or nct6775 / it87, board dependent"
    say ""
    case ":$PATH:" in
        *":$BIN_DIR:"*) ;;
        *) say "Note: $BIN_DIR is not on your PATH." ;;
    esac
}

case "${1:-install}" in
    --uninstall|uninstall) uninstall ;;
    install)               install_all ;;
    *)                     die "unknown argument: $1 (expected --uninstall)" ;;
esac
