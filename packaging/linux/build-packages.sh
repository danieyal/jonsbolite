#!/usr/bin/env bash
#
# Builds the Linux release artifacts: a tarball and a .deb, per architecture.
#
#   ./packaging/linux/build-packages.sh 1.0.0
#   ./packaging/linux/build-packages.sh 1.0.0 linux-x64
#
# Output lands in dist/. Needs the .NET 8 SDK; dpkg-deb is optional and only the .deb is
# skipped without it.

set -euo pipefail

VERSION="${1:-1.0.0}"
# An array, not a string: splitting a plain "$ARCHES" would be an unquoted expansion.
if [ "$#" -ge 2 ]; then
    ARCHES=("${@:2}")
else
    ARCHES=(linux-x64 linux-arm64)
fi
REPO="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST="$REPO/dist"

mkdir -p "$DIST"

# Debian's architecture names are not .NET's.
deb_arch() {
    case "$1" in
        linux-x64)   echo amd64 ;;
        linux-arm64) echo arm64 ;;
        linux-arm)   echo armhf ;;
        *)           echo "${1#linux-}" ;;
    esac
}

for RID in "${ARCHES[@]}"; do
    echo "==> publishing $RID"
    OUT="$REPO/JonsboLite/bin/Release/net8.0/$RID/publish"
    rm -rf "$OUT"
    dotnet publish "$REPO/JonsboLite/JonsboLite.csproj" \
        -f net8.0 -r "$RID" -c Release --nologo -v minimal \
        -p:SelfContained=true -p:PublishTrimmed=true

    [ -f "$OUT/JonsboLite" ] || { echo "expected $OUT/JonsboLite" >&2; exit 1; }

    # ---- tarball -----------------------------------------------------------
    STAGE="$(mktemp -d)"
    trap 'rm -rf "$STAGE"' EXIT
    PKG="jonsbolite-$VERSION-$RID"
    mkdir -p "$STAGE/$PKG"

    install -m 0755 "$OUT/JonsboLite"                       "$STAGE/$PKG/jonsbolite"
    install -m 0755 "$REPO/packaging/linux/install.sh"       "$STAGE/$PKG/install.sh"
    install -m 0644 "$REPO/JonsboLite/Linux/99-jonsbolite.rules" "$STAGE/$PKG/"
    install -m 0644 "$REPO/JonsboLite/Linux/jonsbolite.service"  "$STAGE/$PKG/"
    install -m 0644 "$REPO/JonsboLite/README.md"             "$STAGE/$PKG/"
    install -m 0644 "$REPO/THIRD-PARTY-NOTICES.md"           "$STAGE/$PKG/"
    install -m 0644 "$REPO/LICENSE"                          "$STAGE/$PKG/"

    tar -czf "$DIST/$PKG.tar.gz" -C "$STAGE" "$PKG"
    echo "    $DIST/$PKG.tar.gz"

    # ---- deb ---------------------------------------------------------------
    if command -v dpkg-deb >/dev/null 2>&1; then
        ARCH="$(deb_arch "$RID")"
        ROOT="$STAGE/deb"
        mkdir -p "$ROOT/DEBIAN" \
                 "$ROOT/usr/bin" \
                 "$ROOT/lib/udev/rules.d" \
                 "$ROOT/usr/lib/systemd/user" \
                 "$ROOT/usr/share/doc/jonsbolite"

        install -m 0755 "$OUT/JonsboLite" "$ROOT/usr/bin/jonsbolite"
        install -m 0644 "$REPO/JonsboLite/Linux/99-jonsbolite.rules" "$ROOT/lib/udev/rules.d/"
        # Packaged binary lives on PATH, so point the unit at it rather than ~/.local/bin.
        sed 's|^ExecStart=.*|ExecStart=/usr/bin/jonsbolite --quiet|' \
            "$REPO/JonsboLite/Linux/jonsbolite.service" \
            > "$ROOT/usr/lib/systemd/user/jonsbolite.service"
        install -m 0644 "$REPO/JonsboLite/README.md"   "$ROOT/usr/share/doc/jonsbolite/"
        install -m 0644 "$REPO/THIRD-PARTY-NOTICES.md" "$ROOT/usr/share/doc/jonsbolite/"
        install -m 0644 "$REPO/LICENSE"                "$ROOT/usr/share/doc/jonsbolite/copyright"

        cat > "$ROOT/DEBIAN/control" <<EOF
Package: jonsbolite
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: JonsboLite contributors <noreply@example.com>
Depends: libc6
Description: Driver for JONSBO/Ocypus/Ruituo USB monitor panels
 Sends CPU and GPU telemetry to the small USB HID panel shipped with several
 PC cases, replacing a 293 MB vendor application with a single binary.
 .
 Sensors are read from the kernel's own hwmon, procfs and powercap interfaces,
 so no additional library or driver is needed, and the daemon requires no
 elevated privileges: panel access comes from the bundled udev rule.
EOF

        cat > "$ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = configure ]; then
    # Apply the hidraw rule without waiting for a reboot.
    if command -v udevadm >/dev/null 2>&1; then
        udevadm control --reload-rules || true
        udevadm trigger --subsystem-match=hidraw || true
    fi
    cat <<'MSG'

JonsboLite installed. Replug the panel, then:

    jonsbolite --show-binding
    systemctl --user enable --now jonsbolite

If fan, pump or Vcore read 0, load the SuperIO driver for your board:
    sudo modprobe nct6683 force=1

MSG
fi
exit 0
EOF
        chmod 0755 "$ROOT/DEBIAN/postinst"

        DEB="$DIST/jonsbolite_${VERSION}_${ARCH}.deb"
        dpkg-deb --build --root-owner-group "$ROOT" "$DEB" >/dev/null
        echo "    $DEB"
        rm -rf "$ROOT"
    else
        echo "    (dpkg-deb not found, skipping .deb for $RID)"
    fi

    rm -rf "$STAGE"
    trap - EXIT
done

echo
ls -la "$DIST"
