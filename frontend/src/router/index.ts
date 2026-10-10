import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { title: '登錄' },
    },
    {
      path: '/',
      component: () => import('@/layouts/MainLayout.vue'),
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'dashboard',
          component: () => import('@/views/DashboardView.vue'),
          meta: { title: '工作台' },
        },
        {
          path: 'attendance',
          name: 'attendance',
          component: () => import('@/views/attendance/AttendanceView.vue'),
          meta: { title: '考勤薪酬' },
        },
        {
          path: 'accounting',
          name: 'accounting',
          component: () => import('@/views/accounting/AccountingView.vue'),
          meta: { title: '會計收據' },
        },
        {
          path: 'storage',
          name: 'storage',
          component: () => import('@/views/storage/StorageView.vue'),
          meta: { title: '地盤文件' },
        },
        {
          path: 'system/categories',
          name: 'system-categories',
          component: () => import('@/views/system/CategoriesView.vue'),
          meta: { title: '分類字典' },
        },
        {
          path: 'system/users',
          name: 'system-users',
          component: () => import('@/views/identity/UsersView.vue'),
          meta: { title: '用戶管理' },
        },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.meta.requiresAuth && !auth.isLoggedIn) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && auth.isLoggedIn) {
    return { name: 'dashboard' }
  }
  document.title = to.meta.title ? `${to.meta.title} — MC ERP` : 'MC ERP'
})

export default router
