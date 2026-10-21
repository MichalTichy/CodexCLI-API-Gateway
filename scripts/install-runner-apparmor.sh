#!/bin/sh
# Run as root on the Docker HOST, not inside the Gateway or a model runner.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
profile_dir=/etc/apparmor.d
install -m 0644 "$script_dir/../deploy/apparmor/codex-gateway-runner" "$profile_dir/codex-gateway-runner"
apparmor_parser --replace "$profile_dir/codex-gateway-runner"

# Snap Docker explicitly allowlists profiles it may signal/trace. Give it
# management access only to our runner profile, without changing its other rules.
snap_profile=/var/lib/snapd/apparmor/profiles/snap.docker.dockerd
if [ -f "$snap_profile" ]; then
    install -d -m 0755 "$profile_dir/local"
    install -m 0644 "$script_dir/../deploy/apparmor/codex-gateway-runner-docker-snap" "$profile_dir/local/codex-gateway-runner-docker-snap"
    include='  #include if exists <local/codex-gateway-runner-docker-snap>'
    if ! grep -qF "$include" "$snap_profile"; then
        test "$(grep -c '^}$' "$snap_profile")" = 1 || {
            echo 'Unexpected Snap Docker profile layout; refusing to edit it.' >&2
            exit 1
        }
        if [ ! -f "$snap_profile.before-codex-gateway" ]; then
            cp "$snap_profile" "$snap_profile.before-codex-gateway"
        fi
        sed -i '/^}$/i\  #include if exists <local/codex-gateway-runner-docker-snap>' "$snap_profile"
    fi
    apparmor_parser --replace "$snap_profile"
    echo 'Snap Docker management rules installed. Rerun after a Docker Snap refresh.'
fi

echo 'Set CODEX_RUNNER_APPARMOR_PROFILE=codex-gateway-runner and redeploy the Gateway.'
