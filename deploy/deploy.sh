#!/usr/bin/env bash
set -Eeuo pipefail

# CI 通过 SSH 以 stdin 调用：
#   bash -s -- deploy     正常部署
#   bash -s -- cleanup    部署步骤失败/超时后的兜底清理（不停止正在运行的服务）
#
# 目标：每个 docker 调用都有硬逾时；部署失败或中断时自动强制清理，
# 避免卡死的 docker compose / dockerd 长时间占住磁盘 I/O。

root=/opt/mc-erp
app="$root/app"
mode=${1:-deploy}

# 分步预算（秒）：preflight + build + up 约 885s，CI 的 ssh / step 逾时留有余量。
# --wait-timeout 必须小于 UP_TIMEOUT，让 compose 自己先报健康检查失败并输出诊断。
PREFLIGHT_TIMEOUT=45
BUILD_TIMEOUT=300
UP_TIMEOUT=540
COMPOSE_WAIT_TIMEOUT=480
DIAG_TIMEOUT=60
CLEANUP_TIMEOUT=120
STOP_TIMEOUT=20

compose=(docker compose --env-file "$root/.env" -f "$app/docker-compose.prod.yml")

log() {
  printf '[deploy %s] %s\n' "$(date -Is)" "$*"
}

# 先 TERM，20 秒后 KILL；默认连同子进程组一起处理，docker CLI 不会永久阻塞。
run() {
  local seconds=$1
  shift
  # </dev/null：脚本本身由 stdin 送入，防止 docker 命令抢读剩下的脚本内容
  timeout --kill-after=20s "${seconds}s" "$@" </dev/null
}

disk_avail_kb() {
  local path=/var/lib/docker
  [[ -d $path ]] || path=/
  df -Pk "$path" | awk 'NR==2 {print $4}'
}

print_disk() {
  local path=/var/lib/docker
  [[ -d $path ]] || path=/
  df -Ph "$path" | awk 'NR==2 {printf "disk %s: %s free of %s\n", $6, $4, $2}'
}

prune_build_cache() {
  command -v docker >/dev/null 2>&1 || return 0
  run "$CLEANUP_TIMEOUT" docker builder prune --force --all >/dev/null 2>&1 || true
}

prune_dangling() {
  command -v docker >/dev/null 2>&1 || return 0
  run "$CLEANUP_TIMEOUT" docker image prune --force >/dev/null 2>&1 || true
  run "$CLEANUP_TIMEOUT" docker builder prune --force --filter until=24h >/dev/null 2>&1 || true
}

kill_stale_compose() {
  command -v pkill >/dev/null 2>&1 || return 0
  local pattern='docker compose .*docker-compose\.prod\.yml'
  if pgrep -f "$pattern" >/dev/null 2>&1; then
    log "killing stale docker compose processes"
    pkill -TERM -f "$pattern" 2>/dev/null || true
    sleep 3
    pkill -KILL -f "$pattern" 2>/dev/null || true
  fi
}

docker_ready() {
  run 15 docker info >/dev/null 2>&1
}

# 只有 dockerd 真的无响应才重启；正常路径永远走不到这里。
restart_docker_if_stuck() {
  docker_ready && return 0
  log "docker daemon is not responding, restarting it"
  if ! run 120 sudo -n systemctl restart docker 2>/dev/null; then
    log "docker restart failed (re-run deploy/bootstrap.sh to grant the sudoers rule)"
    return 1
  fi
  for _ in $(seq 1 60); do
    if docker_ready; then
      log "docker daemon is back"
      return 0
    fi
    sleep 2
  done
  log "docker daemon still unresponsive after restart"
  return 1
}

cleanup() {
  local exit_code=${1:-1}
  set +e
  log "cleanup start (exit code: $exit_code)"
  kill_stale_compose
  # 已经替换过镜像才停服务；早期失败（解包、compose config）保留旧版本继续运行。
  if (( exit_code != 0 && replaced == 1 )); then
    log "deployment interrupted, stopping half-started stack"
    run 90 "${compose[@]}" down --remove-orphans --timeout "$STOP_TIMEOUT" >/dev/null 2>&1 || true
    prune_build_cache
  fi
  prune_dangling
  rm -f "$root"/release.tar.gz "$root"/mc-erp-*.tar "$root"/mc-erp-*.tar.gz
  restart_docker_if_stuck || true
  print_disk
  log "cleanup done"
}

collect_diagnostics() {
  set +e
  log "===== compose ps ====="
  run 30 "${compose[@]}" ps -a || true
  local service container
  for service in postgres backend web; do
    container=$(run 30 "${compose[@]}" ps -a -q "$service" 2>/dev/null | head -n 1) || true
    log "===== $service: state ====="
    if [[ -n ${container:-} ]]; then
      run 30 docker inspect --format 'status={{.State.Status}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} exit={{.State.ExitCode}}' "$container" || true
      run 30 docker inspect --format '{{range .State.Health.Log}}[{{.Start}}] exit={{.ExitCode}} {{.Output}}{{"\n"}}{{end}}' "$container" 2>/dev/null | tail -n 10 || true
    fi
    log "===== $service: logs ====="
    run "$DIAG_TIMEOUT" "${compose[@]}" logs --tail 120 --no-color "$service" || true
  done
}

replaced=0

if [[ $mode == cleanup ]]; then
  cleanup 1
  exit 0
fi

on_exit() {
  local exit_code=$1
  trap - EXIT INT TERM HUP
  set +e
  cleanup "$exit_code"
  exit "$exit_code"
}
trap 'on_exit $?' EXIT
trap 'on_exit 130' INT
trap 'on_exit 143' TERM
trap 'on_exit 129' HUP

log "deploy start"
print_disk

[[ -f $root/.env ]] || { log "missing $root/.env on server" >&2; exit 1; }
docker_ready || { log "docker daemon is not ready" >&2; exit 1; }

avail_kb=$(disk_avail_kb)
log "free disk: $((avail_kb / 1024)) MiB"
if (( avail_kb < 2 * 1024 * 1024 )); then
  log "free disk below 2 GiB, pruning before deploy"
  prune_build_cache
  prune_dangling
  avail_kb=$(disk_avail_kb)
fi
if (( avail_kb < 1024 * 1024 )); then
  log "free disk still below 1 GiB, aborting before touching images" >&2
  exit 1
fi

mkdir -p "$app"
rm -rf "$app/backend" "$app/frontend"
log "extracting release"
run 120 tar -xzf "$root/release.tar.gz" -C "$app"
cd "$app"
run "$PREFLIGHT_TIMEOUT" "${compose[@]}" config --quiet

log "building images (cap ${BUILD_TIMEOUT}s)"
run "$BUILD_TIMEOUT" "${compose[@]}" build
replaced=1

log "starting services (compose wait ${COMPOSE_WAIT_TIMEOUT}s, hard cap ${UP_TIMEOUT}s)"
if ! run "$UP_TIMEOUT" "${compose[@]}" up --detach --no-build --wait --wait-timeout "$COMPOSE_WAIT_TIMEOUT"; then
  log "deploy failed, collecting diagnostics" >&2
  collect_diagnostics
  exit 1
fi

rm -f "$root/release.tar.gz"
log "deployed"
run 60 "${compose[@]}" ps
