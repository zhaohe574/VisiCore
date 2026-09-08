import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError, authApi, refreshSession, type User, type LoginResult } from '../api'
import { errorMessage } from '../lib/format'

export const useAuth = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const initialized = ref(false)
  const error = ref('')
  let initialization: Promise<void> | null = null
  let refreshTimer: ReturnType<typeof setTimeout> | undefined
  const authenticated = computed(() => !!user.value)
  const can = (code?: string) => !code || !!user.value?.permissions?.some(value => value === '*' || value === code)
  const canRoute = (meta: { permission?: unknown; permissions?: unknown }) => Array.isArray(meta.permissions) ? meta.permissions.some(code => can(String(code))) : can(meta.permission as string | undefined)
  function accept(result: LoginResult) { user.value = result.user; initialized.value = true; schedule(result.expiresAt) }
  function schedule(expiresAt?: string) {
    clearTimeout(refreshTimer)
    const remaining = expiresAt ? Date.parse(expiresAt) - Date.now() - 60000 : 15 * 60000
    refreshTimer = setTimeout(async () => {
      if (!user.value) return
      try { accept(await refreshSession()) } catch (e) { if (!(e instanceof ApiError && e.status === 401)) schedule(new Date(Date.now() + 120000).toISOString()) }
    }, Math.min(2147483647, Math.max(30000, remaining)))
  }
  async function loadUser() { user.value = await authApi.me(); initialized.value = true; schedule() }
  async function initialize() {
    if (initialized.value) return
    if (!initialization) initialization = loadUser().catch(e => { if (!(e instanceof ApiError && e.status === 401)) error.value = errorMessage(e); initialized.value = true }).finally(() => { initialization = null })
    await initialization
  }
  async function login(username: string, password: string) { error.value = ''; accept(await authApi.login(username, password)) }
  function clear() { clearTimeout(refreshTimer); user.value = null; initialized.value = true }
  async function logout() { window.dispatchEvent(new Event('platform-media-stop')); await authApi.logout(); clear() }
  window.addEventListener('platform-auth-expired', clear)
  window.addEventListener('platform-auth-refreshed', event => accept((event as CustomEvent<LoginResult>).detail))
  return { user, initialized, error, authenticated, can, canRoute, accept, initialize, loadUser, login, logout, clear }
})
