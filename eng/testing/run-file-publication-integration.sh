#!/usr/bin/env bash
# Disposable Linux runner only. Both test servers bind to loopback.
set -euo pipefail
root="$(mktemp -d)"
user="farpub$(openssl rand -hex 4)"
password="$(openssl rand -hex 18)"
ftp_pid=""
ssh_pid=""
created_user=0
cleanup() {
  if [[ -n "$ftp_pid" ]]; then kill "$ftp_pid" 2>/dev/null || true; fi
  if [[ -n "$ssh_pid" ]]; then sudo kill "$ssh_pid" 2>/dev/null || true; fi
  if [[ "$created_user" == 1 ]]; then sudo userdel -r "$user" >/dev/null 2>&1 || true; fi
  rm -rf "$root"
}
trap cleanup EXIT
if [[ ! -x /usr/sbin/sshd ]]; then
  sudo apt-get update -qq
  sudo apt-get install -y openssh-server
fi
sudo mkdir -p /run/sshd
sudo useradd --create-home --shell /bin/bash "$user"
created_user=1
printf '%s:%s\n' "$user" "$password" | sudo chpasswd
ssh-keygen -q -t ed25519 -N '' -f "$root/hostkey"
cat > "$root/sshd_config" <<EOF
Port 22222
ListenAddress 127.0.0.1
HostKey $root/hostkey
PidFile $root/sshd.pid
PasswordAuthentication yes
KbdInteractiveAuthentication no
UsePAM no
PermitRootLogin no
AllowUsers $user
Subsystem sftp internal-sftp
EOF
sudo /usr/sbin/sshd -f "$root/sshd_config"
ssh_pid="$(sudo cat "$root/sshd.pid")"
mkdir "$root/ftp"
printf original > "$root/ftp/target"
python3 -m venv "$root/venv"
"$root/venv/bin/pip" -q install pyftpdlib==2.0.1
"$root/venv/bin/python" -m pyftpdlib -i 127.0.0.1 -p 21221 -u "$user" -P "$password" -d "$root/ftp" -w > "$root/ftp.log" 2>&1 &
ftp_pid=$!
python3 - <<'PY'
import socket, time
for port in (22222, 21221):
    for attempt in range(100):
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.2):
                break
        except OSError:
            time.sleep(0.1)
    else:
        raise RuntimeError(f"Loopback server on port {port} did not start")
PY
export CSHARPFAR_PUBLICATION_INTEGRATION=1
export CSHARPFAR_SFTP_PORT=22222 CSHARPFAR_FTP_PORT=21221
export CSHARPFAR_SFTP_ROOT="/home/$user"
export CSHARPFAR_TEST_USER="$user" CSHARPFAR_TEST_PASSWORD="$password"
dotnet test tests/CSharpFar.Tests/CSharpFar.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~NetworkPublicationIntegrationTests --logger "console;verbosity=normal"
