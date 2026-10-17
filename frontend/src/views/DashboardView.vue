<script setup lang="ts">
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

const modules = [
  {
    path: '/attendance',
    title: '考勤薪酬',
    desc: '外部打卡數據拉取、工數統計、MPF/OT 薪酬計算、月結報表',
    icon: 'calendar-clock',
  },
  {
    path: '/accounting',
    title: '會計收據',
    desc: '收據拍照上傳、OCR 識別、支出按地盤分類、年度提取',
    icon: 'receipt',
  },
  {
    path: '/storage',
    title: '地盤文件',
    desc: '雲盤文件管理、地盤照片按時間/事件分類、賬戶權限',
    icon: 'hard-drive',
  },
]

function go(path: string) {
  router.push(path)
}
</script>

<template>
  <div class="dashboard">
    <!-- 歡迎橫幅 -->
    <section class="hero mc-card">
      <div class="hero-text">
        <h1 class="hero-title">{{ auth.displayName ? `你好，${auth.displayName}` : '你好' }}</h1>
        <p class="hero-sub">一體化平台，四大模組待業務需求接入。</p>
      </div>
      <div class="hero-deco" />
    </section>

    <!-- 模組入口 -->
    <section class="modules">
      <article
        v-for="m in modules"
        :key="m.path"
        class="module-card mc-card mc-card--hover"
        tabindex="0"
        role="link"
        :aria-label="m.title"
        @click="go(m.path)"
        @keyup.enter="go(m.path)"
      >
        <div class="module-icon" :class="`module-icon--${m.icon}`">
          <AppIcon :name="m.icon" :size="24" />
        </div>
        <h2 class="module-title">{{ m.title }}</h2>
        <p class="module-desc">{{ m.desc }}</p>
        <div class="module-status">
          <span class="status-tag status-tag--pending">待接入</span>
        </div>
      </article>
    </section>
  </div>
</template>

<style scoped>
.dashboard {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.hero {
  position: relative;
  display: flex;
  align-items: center;
  min-height: 148px;
  padding: 32px 36px;
  overflow: hidden;
  color: #fff;
  background: linear-gradient(120deg, #1D5BBF 0%, #2E73E8 55%, #5C8AD9 100%);
  border: none;
}

.hero-title {
  margin: 0 0 6px;
  font-size: 24px;
  font-weight: 600;
}

.hero-sub {
  margin: 0;
  color: rgb(255 255 255 / 85%);
}

.hero-deco {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background-image:
    linear-gradient(rgb(255 255 255 / 8%) 1px, transparent 1px),
    linear-gradient(90deg, rgb(255 255 255 / 8%) 1px, transparent 1px);
  background-size: 32px 32px;
  mask-image: radial-gradient(ellipse at 90% 20%, #000 0%, transparent 70%);
}

.modules {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 24px;
}

.module-card {
  padding: 24px;
  cursor: pointer;
}

.module-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  margin-bottom: 16px;
  border-radius: 10px;
  color: #fff;
}

.module-icon--calendar-clock {
  background: linear-gradient(135deg, #1D5BBF, #2E73E8);
}

.module-icon--receipt {
  background: linear-gradient(135deg, #18A058, #3DBE7A);
}

.module-icon--hard-drive {
  background: linear-gradient(135deg, #6B7280, #9CA3AF);
}

.module-title {
  margin: 0 0 8px;
  font-size: 18px;
  font-weight: 500;
}

.module-desc {
  margin: 0 0 16px;
  min-height: 40px;
  color: var(--mc-text-secondary);
  line-height: 20px;
}

.status-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 10px;
  border-radius: 999px;
  font-size: 12px;
}

.status-tag--pending {
  background: rgb(230 162 60 / 12%);
  color: #b2741f;
}
</style>
