<script setup lang="ts">
import { computed, ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const collapsed = ref(false)
const compact = useMediaQuery('(max-width: 600px)')
const menuCollapsed = computed(() => collapsed.value || compact.value)

interface MenuItem {
  path: string
  name: string
  title: string
  icon: string
}

const menus: MenuItem[] = [
  { path: '/', name: 'dashboard', title: '工作台', icon: 'layout-dashboard' },
  { path: '/attendance', name: 'attendance', title: '考勤薪酬', icon: 'calendar-clock' },
  { path: '/accounting', name: 'accounting', title: '會計收據', icon: 'receipt' },
  { path: '/storage', name: 'storage', title: '地盤文件', icon: 'hard-drive' },
]

const systemMenus: MenuItem[] = [
  { path: '/system/categories', name: 'system-categories', title: '系統字典', icon: 'bookmark' },
  { path: '/system/users', name: 'system-users', title: '用戶管理', icon: 'shield' },
]

const activeMenu = computed(() => {
  if (route.path === '/') return 'dashboard'
  if (route.path.startsWith('/system/')) return 'system'
  return route.name as string
})
const breadcrumb = computed(() => ['MC ERP', (route.meta.title as string) ?? ''])

async function handleLogout() {
  await ElMessageBox.confirm('確定要退出登錄嗎？', '提示', {
    confirmButtonText: '退出',
    cancelButtonText: '取消',
    type: 'warning',
  })
  auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <el-container class="layout">
    <!-- 側邊欄 -->
    <el-aside :width="menuCollapsed ? '64px' : '220px'" class="sidebar">
      <div class="brand" @click="router.push('/')">
        <AppIcon name="hard-hat" :size="26" class="brand-icon" />
        <span v-show="!menuCollapsed" class="brand-name">MC ERP</span>
      </div>
      <el-menu class="mc-menu" :collapse="menuCollapsed" :default-active="activeMenu" :collapse-transition="false">
        <el-menu-item v-for="m in menus" :key="m.name" :index="m.name" @click="router.push(m.path)">
          <AppIcon :name="m.icon" :size="18" />
          <template #title>{{ m.title }}</template>
        </el-menu-item>
        <el-sub-menu index="system">
          <template #title>
            <AppIcon name="settings" :size="18" />
            <span>系統設置</span>
          </template>
          <el-menu-item v-for="m in systemMenus" :key="m.name" :index="m.name" @click="router.push(m.path)">
            <AppIcon :name="m.icon" :size="18" />
            <template #title>{{ m.title }}</template>
          </el-menu-item>
        </el-sub-menu>
      </el-menu>
    </el-aside>

    <el-container>
      <!-- 頂部工具欄 -->
      <el-header class="toolbar" height="56px">
        <button v-if="!compact" class="collapse-btn" type="button" aria-label="切換側欄" @click="collapsed = !collapsed">
          <AppIcon :name="collapsed ? 'chevrons-right' : 'chevrons-left'" :size="18" />
        </button>
        <el-breadcrumb separator="/">
          <el-breadcrumb-item v-for="item in breadcrumb" :key="item">{{ item }}</el-breadcrumb-item>
        </el-breadcrumb>
        <div class="spacer" />
        <el-tooltip content="通知" placement="bottom">
          <button class="tool-btn" type="button">
            <AppIcon name="bell" :size="18" />
          </button>
        </el-tooltip>
        <el-dropdown>
          <span class="user-chip">
            <el-avatar :size="28" class="user-avatar">{{ auth.username.slice(0, 1).toUpperCase() }}</el-avatar>
            <span class="user-name">{{ auth.username || '未知用戶' }}</span>
            <AppIcon name="chevron-down" :size="14" />
          </span>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item @click="handleLogout">
                <el-icon style="margin-right: 6px"><AppIcon name="log-out" :size="14" /></el-icon>
                退出登錄
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>

      <!-- 內容區 -->
      <el-main class="content">
        <RouterView v-slot="{ Component }">
          <Transition name="fade" mode="out-in">
            <component :is="Component" />
          </Transition>
        </RouterView>
      </el-main>
    </el-container>
  </el-container>
</template>

<style scoped>
.layout {
  height: 100%;
}

.sidebar {
  display: flex;
  flex-direction: column;
  background: var(--mc-sidebar);
  transition: width 0.2s ease;
  overflow: hidden;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 56px;
  padding: 0 18px;
  cursor: pointer;
  color: #fff;
}

.brand-icon {
  color: var(--mc-primary-light);
}

.brand-name {
  font-size: 18px;
  font-weight: 600;
  letter-spacing: 1px;
  white-space: nowrap;
}

.mc-menu {
  flex: 1;
  padding-top: 8px;
}

/* 折疊態徽標居中 */
.sidebar:has(.el-menu--collapse) .brand {
  justify-content: center;
  padding: 0;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 0 16px;
  background: var(--mc-card);
  border-bottom: 1px solid var(--mc-border);
}

.collapse-btn,
.tool-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--mc-text-secondary);
  cursor: pointer;
  transition: background-color 0.2s ease, color 0.2s ease;
}

.collapse-btn:hover,
.tool-btn:hover {
  background: var(--mc-bg);
  color: var(--mc-primary);
}

.spacer {
  flex: 1;
}

.user-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px;
  border-radius: 6px;
  cursor: pointer;
  outline: none;
}

.user-chip:hover {
  background: var(--mc-bg);
}

.user-avatar {
  background: var(--mc-primary);
  font-weight: 600;
}

.user-name {
  color: var(--mc-text);
}

.content {
  padding: 24px;
  overflow-y: auto;
}

@media (max-width: 600px) {
  .toolbar { gap: 8px; padding: 0 8px; }
  .user-name { display: none; }
  .content { padding: 16px; }
}
</style>
