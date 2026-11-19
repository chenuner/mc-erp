#!/usr/bin/env bash
set -euo pipefail

if [[ $(id -u) -ne 0 ]]; then
  echo "Run with sudo bash deploy/bootstrap.sh" >&2
  exit 1
fi

apt-get update
apt-get install -y docker.io docker-compose-v2
systemctl enable --now docker

if ! id deploy >/dev/null 2>&1; then
  adduser --disabled-password --gecos '' deploy
fi
usermod -aG docker deploy
install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
if [[ ! -f /home/deploy/.ssh/github_actions ]]; then
  runuser -u deploy -- ssh-keygen -t ed25519 -N '' -C mc-erp-actions -f /home/deploy/.ssh/github_actions
fi
touch /home/deploy/.ssh/authorized_keys
public_key=$(cat /home/deploy/.ssh/github_actions.pub)
if ! grep -qF "$public_key" /home/deploy/.ssh/authorized_keys; then
  printf 'restrict %s\n' "$public_key" >> /home/deploy/.ssh/authorized_keys
fi
chmod 600 /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys

install -d -m 750 -o deploy -g deploy /opt/mc-erp /opt/mc-erp/app
if [[ ! -f /opt/mc-erp/.env && -f /opt/mc-erp/shared/.env ]]; then
  mv /opt/mc-erp/shared/.env /opt/mc-erp/.env
fi
if [[ ! -f /opt/mc-erp/.env ]]; then
  umask 077
  {
    printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 32)"
    printf 'JWT_SECRET=%s\n' "$(openssl rand -hex 64)"
    printf 'INITIAL_ADMIN_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  } > /opt/mc-erp/.env
  chown deploy:deploy /opt/mc-erp/.env
fi
rmdir /opt/mc-erp/shared /opt/mc-erp/incoming /opt/mc-erp/releases 2>/dev/null || true

# dockerd 卡死时，部署脚本需要重启守护进程才能恢复；只放行这一条命令，不给完整 sudo。
cat >/etc/sudoers.d/mc-erp-deploy-docker <<'EOF'
deploy ALL=(root) NOPASSWD: /usr/bin/systemctl restart docker
EOF
chmod 440 /etc/sudoers.d/mc-erp-deploy-docker
visudo -cf /etc/sudoers.d/mc-erp-deploy-docker >/dev/null

# 每日清理过期 docker 数据：即使某次部署的兜底清理没跑成，磁盘也不会无限增长。
cat >/etc/systemd/system/mc-erp-docker-prune.service <<'EOF'
[Unit]
Description=Prune stale Docker data for mc-erp

[Service]
Type=oneshot
ExecStart=/usr/bin/docker system prune --force --filter until=168h
EOF
cat >/etc/systemd/system/mc-erp-docker-prune.timer <<'EOF'
[Unit]
Description=Daily Docker prune for mc-erp

[Timer]
OnCalendar=daily
RandomizedDelaySec=1h
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now mc-erp-docker-prune.timer

echo 'Ready. Deploy user: deploy; application directory: /opt/mc-erp'
echo 'Docker restart sudoers rule: /etc/sudoers.d/mc-erp-deploy-docker'
echo 'Daily Docker prune timer: mc-erp-docker-prune.timer'
echo 'SSH private key: /home/deploy/.ssh/github_actions (GitHub Secret only)'
echo 'Runtime secrets: /opt/mc-erp/.env (keep on server)'
echo 'Allow inbound SSH, HTTP and HTTPS in your cloud firewall before deployment.'