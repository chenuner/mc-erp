# Ubuntu 24.04 部署

在 GitHub Actions 手动触发、默认分支构建成功后，GitHub Actions 通过 SSH 上传产物，在服务器使用 Docker Compose 启动 PostgreSQL 16、.NET 10 后端及 Nginx 前端。服务器不需要安装 .NET、Node.js 或 pnpm，也不需要仓库访问令牌。

只有两个环境：根目录 `docker-compose.yml` 启动本地开发数据库，`docker-compose.prod.yml` 启动生产前后端及数据库。生产 Dockerfile 位于 `backend/` 和 `frontend/`，只打包 GitHub 已构建的产物，不在服务器编译源码。

本服务器已有官網佔用 80/443，因此宿主機 nginx 是唯一入口，ERP 的 web 容器只綁 `127.0.0.1:8080`。根目錄 `nginx.conf` 是伺服器 `/etc/nginx/nginx.conf` 的參考版本，不隨 CI 上傳。

## 1. 首次初始化服务器

先将本次部署代码提交、推送到 GitHub。尚未配置部署参数时，首次 Deploy Production 失败是预期行为；配置完成后重新运行整个工作流。

通过云厂商控制台或已有 SSH 登录 Ubuntu，执行：

```bash
sudo apt-get update
sudo apt-get install -y git
git clone https://github.com/chenuner/mc-erp.git
cd mc-erp
sudo bash deploy/bootstrap.sh
```

脚本只安装 Docker 及 Compose，使用 Ubuntu 已有的 SSH 和 OpenSSL；创建 `deploy` 用户、部署专用 SSH 密钥和 `/opt/mc-erp/.env`。重复运行不会重置密码。Docker 用户组具有等同 root 的权限，应严格保护部署密钥和 GitHub 默认分支。

已执行旧版 bootstrap、但尚未部署的服务器，在源码目录执行 `git pull` 后重新运行 `sudo bash deploy/bootstrap.sh`。脚本会将旧的 `shared/.env` 移到 `/opt/mc-erp/.env`，保留所有密码和 SSH 密钥，并移除旧版空目录。已配置的 GitHub Secrets 和 Variables 不需要修改。新增的 docker 重启 sudoers 规则和每日清理 timer 也由这次重跑安装，建议所有服务器都重跑一次。

服务器固定目录为：

```text
/opt/mc-erp/
├── .env                       # 服务器运行密钥
└── app/                       # CI 上传的生产产物
	├── docker-compose.prod.yml
	├── backend/
	│   ├── Dockerfile
	│   └── publish/
	└── frontend/
		├── Dockerfile
		├── nginx.conf
		└── dist/
```

云服务器安全组放行 TCP 22、80、443。数据库和 web 容器没有对公网映射，不要放行 5432 或 8080。若已启用 UFW，先确保实际 SSH 端口已放行，再执行 `sudo ufw allow 80,443/tcp`。这里假定 SSH 端口为 22。

### 1.1 與現有官網共存（先用 IP 驗收）

官網 `msucce.com` / `www.msucce.com` 繼續由宿主機 nginx 從 `/var/www/dist` 提供。ERP 先用 `http://伺服器公網IP` 打開，nginx 把「沒有匹配到官網域名」的請求轉發到 `127.0.0.1:8080`，不需要域名，也不需要新證書。

1. 把根目錄 `nginx.conf` 套到 `/etc/nginx/nginx.conf`，先檢查再 reload：

```bash
sudo nginx -t && sudo systemctl reload nginx
```

這個配置只新增一個 `listen 80 default_server` 的 ERP 站點，官網的 80/443 兩塊保持原樣。先套用再做部署也可以；容器未啟動時訪問 IP 只會得到 502，官網不受影響。

若 `nginx -t` 報 `duplicate default server`，表示 `/etc/nginx/sites-enabled/` 或 `conf.d/` 裡已有別的默認站點（Ubuntu 自帶的 `default` 就是），移走那個文件再檢查。

2. 雲安全組放行 80/443（8080 不對外，不要放行）。確認宿主機 8080 沒被佔用：

```bash
sudo ss -ltnp | grep ':8080' || echo '8080 free'
```

3. 部署成功後打開 `http://伺服器公網IP`，就是 ERP 登錄頁。

> 臨時用 IP + HTTP 只適合連通驗收。正式錄入員工、薪酬、票據前仍應切到域名 + HTTPS：加 `erp.msucce.com` 的 A 記錄、用 acme.sh 簽證書，再在 `nginx.conf` 給 ERP 加一個 443 server 塊。現有證書只涵蓋 `msucce.com` + `www.msucce.com`，不含子域，屆時要單獨簽：

```bash
sudo /root/.acme.sh/acme.sh --issue -d erp.msucce.com --webroot /var/www/acme-challenge
sudo install -d /etc/nginx/ssl/erp.msucce.com
sudo /root/.acme.sh/acme.sh --install-cert -d erp.msucce.com \
  --key-file       /etc/nginx/ssl/erp.msucce.com/erp.msucce.com.key \
  --fullchain-file /etc/nginx/ssl/erp.msucce.com/fullchain.cer \
  --reloadcmd      "systemctl reload nginx"
```

`acme.sh` 路徑按實際安裝位置調整（`sudo find / -name acme.sh -type f`）。續期由 acme.sh 每日 cron 處理。

## 2. GitHub Repository Secrets

进入仓库 Settings → Secrets and variables → Actions → Secrets，新增：

| 名称 | 内容 |
|---|---|
| `DEPLOY_SSH_KEY` | 下面第一条命令输出的完整私钥，包含 BEGIN/END 行 |
| `DEPLOY_KNOWN_HOSTS` | 下面第二条命令输出的一整行服务器主机密钥 |

在服务器执行，输出只填入 GitHub，不要发到聊天或提交到仓库：

```bash
sudo cat /home/deploy/.ssh/github_actions
printf '%s ' '你的服务器公网IP'; sudo cut -d ' ' -f 1,2 /etc/ssh/ssh_host_ed25519_key.pub
```

第二条命令中的 IP 必须和 `DEPLOY_HOST` 完全一致。非 22 端口时，第一列改为 `[公网IP]:端口`。主机密钥来自可信服务器会话，不在 CI 中盲目信任 `ssh-keyscan` 的结果。

## 3. GitHub Repository Variables

在同一页面的 Variables 中新增：

| 名称 | 值 |
|---|---|
| `DEPLOY_HOST` | 服务器公网 IP，必填，不带 http:// |
| `DEPLOY_USER` | `deploy`，不填时也默认使用此值 |
| `DEPLOY_PORT` | `22`，非标准 SSH 端口按实际填写 |

数据库密码、JWT 密钥和首次管理员密码已在服务器随机生成，不需要添加到 GitHub。可选 OCR 配置在服务器 `/opt/mc-erp/.env` 中添加 `OCR_ENDPOINT`、`OCR_KEY` 后重新部署；不配置时可以手动录入票据。

## 4. 部署和登录

> **CICD触发方式**：CI 只监听 `pull_request` 和手动 `workflow_dispatch`。**推送到 `master` 不会自动执行**，不点 Run workflow 就不会部署。
>
> - 为了节省资源想要部署必须新建一次 run；在旧 run 上点 Re-run 不会改变分支。
> - 分支下拉选 `master`。选 `dev` 等其他分支时 `Deploy Production` 显示灰色 `Skipped`，只建置不部署。
> - 想改成推送即部署：在 `.github/workflows/ci.yml` 的 `on:` 下加 `push:` / `branches: [master]`，代价是每次推 master 都直接改生产。

GitHub → Actions → CI → Run workflow，选择 `master`。需要重跑时重新运行整个工作流，以便重新生成构建产物。PR 不会部署。

等待 Backend Build and Package、Frontend Build and Package、Deploy Production 全部通过，再打开 `http://服务器公网IP`（前提是已套用 1.1 的 `nginx.conf`）。

用户名为 `admin`，首次密码在服务器查看：

```bash
sudo grep '^INITIAL_ADMIN_PASSWORD=' /opt/mc-erp/.env
```

8080 不对公网开放。需要绕过 nginx 直接排查容器时，用 SSH 转发：

```bash
ssh -L 8080:127.0.0.1:8080 你的管理用户@服务器公网IP
```

保持连接后打开 `http://localhost:8080`。使用已有管理账户，不要用禁止端口转发的 Actions 专用密钥。

## 5. 日常运维

```bash
cd /opt/mc-erp/app
docker compose --env-file ../.env -f docker-compose.prod.yml ps
docker compose --env-file ../.env -f docker-compose.prod.yml logs --tail 100
```

以上命令使用 `deploy` 用户或 root 执行。健康检查失败时 Actions 会报告失败，并按 `postgres` / `backend` / `web` 分别输出容器状态、健康检查记录和日志；不会自动回滚数据库迁移。发布可能有短暂停机。

### 5.1 超时与清理（防止 docker 卡死拖垮磁盘）

部署链路有三层硬逾时，任何一层生效都不会让 docker 操作无限占住磁盘 I/O：

| 层 | 位置 | 上限 |
|---|---|---|
| GitHub job | `ci.yml` 的 `deploy` | 30 分钟（外层安全网） |
| GitHub step | `Upload and start services` | 18 分钟 |
| 远端脚本 | `deploy/deploy.sh` 的 `timeout` | 每步 45–540 秒，约 15 分钟内收尾 |

远端脚本每一条 `docker` 命令都套 `timeout --kill-after`：先 TERM，20 秒后 KILL。失败、超时或 SSH 中断都会触发 `trap` 清理：

- 杀掉卡住的 `docker compose` 进程；
- 已经替换过镜像时才 `compose down --remove-orphans` 停掉半启动的栈，早期失败保留旧版本继续运行；
- `docker builder prune --all` + `docker image prune` 释放构建缓存和悬空镜像；
- 删除 `/opt/mc-erp` 下的临时发布包；
- 如果 `docker info` 无响应，用 sudoers 放行的 `systemctl restart docker` 重启守护进程并等待恢复。

`bootstrap.sh` 还安装了每日 `mc-erp-docker-prune.timer`（`docker system prune --filter until=168h`），作为兜底防止磁盘长期增长。CI 侧的 `Cleanup remote after failure` 步骤只做进程和缓存清理，不会停止正在运行的服务。

部署前脚本会检查 `/var/lib/docker` 剩余空间：低于 2 GiB 先自动清理，仍低于 1 GiB 直接失败，不碰镜像。需要人工介入时：

```bash
# 看谁卡住了
ps -eo pid,stat,etime,cmd | grep -E 'docker (compose|build|load)' | grep -v grep
sudo systemctl restart docker
# 手动清一次
docker builder prune --all --force
docker image prune --force
docker system df
```

`deploy/deploy.sh` 也支持手动跑 `bash deploy/deploy.sh cleanup`（在服务器上，用 `deploy` 用户或 root），只做清理、不停服务。

容器日志已限制为 `10m × 3` 份，崩溃循环不会写满磁盘。

生产数据库、上传原图和日志分别保存在 `mc-erp-production_pgdata`、`mc-erp-production_storage`、`mc-erp-production_logs` 卷中，与本地开发数据隔离。更新或重启容器不会清空数据；禁止执行 `docker compose down -v`。备份必须覆盖数据库、原图及服务器 `.env`，迁移前先做备份。已有数据库的密码不能只通过修改 `.env` 更换，需要同步修改 PostgreSQL 角色密码。

`INITIAL_ADMIN_PASSWORD` 仅在空数据库初始化时读取，更改该变量不会重置已有用户密码。每次发布替换固定应用目录，不维护版本目录或软链接。旧的无标签镜像和构建缓存由部署脚本及每日 timer 自动清理，不要删除持久卷。