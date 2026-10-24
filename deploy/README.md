# Ubuntu 24.04 部署

默认分支构建和检查成功后，GitHub Actions 通过 SSH 上传产物，在服务器使用 Docker Compose 启动 PostgreSQL 16、.NET 10 后端及 Nginx 前端。服务器不需要安装 .NET、Node.js 或 pnpm，也不需要仓库访问令牌。

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

脚本创建 `deploy` 用户、部署专用 SSH 密钥和 `/opt/mc-erp/shared/.env`。重复运行不会重置密码。Docker 用户组具有等同 root 的权限，应严格保护部署密钥和 GitHub 默认分支。

云服务器安全组放行 TCP 22、80。数据库和后端没有宿主机端口映射，不要放行 5432 或 8080。若已启用 UFW，先确保实际 SSH 端口已放行，再执行 `sudo ufw allow 80/tcp`。这里假定 SSH 端口为 22。

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

数据库密码、JWT 密钥和首次管理员密码已在服务器随机生成，不需要添加到 GitHub。可选 OCR 配置在服务器 `/opt/mc-erp/shared/.env` 中添加 `OCR_ENDPOINT`、`OCR_KEY` 后重新部署；不配置时可以手动录入票据。

## 4. 部署和登录

GitHub → Actions → CI → Run workflow，选择仓库默认分支。需要重跑时重跑整个工作流，以便重新生成构建产物；也可以继续向默认分支推送提交。PR 不会部署。

等待 Backend Build and Checks、Frontend Type Check and Build、Deploy Production 全部通过，再打开 `http://服务器公网IP`。

用户名为 `admin`，首次密码在服务器查看：

```bash
sudo grep '^INITIAL_ADMIN_PASSWORD=' /opt/mc-erp/shared/.env
```

此配置的 HTTP 入口只适合首次连通验收。正式录入员工、薪酬、票据等真实数据前，必须配置域名及 HTTPS，避免公网明文传输密码和数据。尚未配置 HTTPS 时，可用 SSH 隧道从本机访问：

```bash
ssh -L 8080:127.0.0.1:80 你的管理用户@服务器公网IP
```

保持连接后打开 `http://localhost:8080`。使用已有管理账户，不要用禁止端口转发的 Actions 专用密钥。

## 5. 日常运维

```bash
cd /opt/mc-erp/current
export RELEASE_ID=$(basename "$(pwd -P)")
docker compose --env-file /opt/mc-erp/shared/.env -f deploy/compose.yml ps
docker compose --env-file /opt/mc-erp/shared/.env -f deploy/compose.yml logs --tail 100
```

以上命令使用 `deploy` 用户或 root 执行。健康检查失败时 Actions 会报告失败并输出容器日志；不会自动回滚数据库迁移。发布可能有短暂停机。

数据库、上传原图和日志分别保存在 `mc-erp_pgdata`、`mc-erp_storage`、`mc-erp_logs` 卷中。更新或重启容器不会清空数据；禁止执行 `docker compose down -v`。备份必须覆盖数据库、原图及服务器 `.env`，迁移前先做备份。已有数据库的密码不能只通过修改 `.env` 更换，需要同步修改 PostgreSQL 角色密码。

`INITIAL_ADMIN_PASSWORD` 仅在空数据库初始化时读取，更改该变量不会重置已有用户密码。历史发布目录和镜像保留在服务器，需要定期按磁盘情况清理，不要删除持久卷。