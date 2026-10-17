import { defineStore } from 'pinia'
import { login as loginApi } from '@/api/identity'
import { clearToken, getToken, setToken } from '@/api/request'

/** 從 JWT payload 解出基本聲明（不做驗籤，僅展示用途） */
function parseJwt(token: string): Record<string, unknown> {
  try {
    const payload = token.split('.')[1] ?? ''
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
  } catch {
    return {}
  }
}

export const useAuthStore = defineStore('auth', {
  state: () => {
    const token = getToken() ?? ''
    const claims = token ? parseJwt(token) : {}
    const role = (claims.role ?? claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']) as string[] | string | undefined
    return {
      token,
      /** 優先取名字聲明，否則用 subject */
      username: (claims.name as string) ?? (claims.unique_name as string) ?? (claims.sub as string) ?? '',
      roles: Array.isArray(role) ? role : role ? [role] : [],
    }
  },
  getters: {
    isLoggedIn: (state) => state.token !== '',
  },
  actions: {
    async login(username: string, password: string) {
      const token = await loginApi(username, password)
      setToken(token)
      this.token = token
      const claims = parseJwt(token)
      this.username = (claims.name as string) ?? (claims.unique_name as string) ?? (claims.sub as string) ?? username
      const role = claims.role ?? claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
      this.roles = Array.isArray(role) ? role : role ? [role] : []
    },
    logout() {
      clearToken()
      this.token = ''
      this.username = ''
      this.roles = []
    },
  },
})
