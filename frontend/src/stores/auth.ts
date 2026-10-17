import { defineStore } from 'pinia'
import { login as loginApi } from '@/api/identity'
import { clearToken, getToken, setToken } from '@/api/request'
import { readAuthClaims } from '@/utils/authClaims'

export const useAuthStore = defineStore('auth', {
  state: () => {
    const token = getToken() ?? ''
    return {
      token,
      ...readAuthClaims(token),
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
      const claims = readAuthClaims(token)
      this.username = claims.username
      this.displayName = claims.displayName
      this.roles = claims.roles
    },
    logout() {
      clearToken()
      this.token = ''
      this.username = ''
      this.displayName = ''
      this.roles = []
    },
  },
})
