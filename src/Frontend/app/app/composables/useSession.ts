type AuthResponse = {
  accessToken: string
  accessExpiresAtUtc: string
  refreshToken: string
  mustChangePassword: boolean
}

export function useSession() {
  const token = useState<string>('access-token', () => '')
  const refreshToken = useState<string>('refresh-token', () => '')
  const expiresAt = useState<string>('expires-at', () => '')
  const tenantId = useState<string>('tenant-id', () => '')
  const config = useRuntimeConfig()

  function restore() {
    if (!import.meta.client) return
    token.value = sessionStorage.getItem('aiyara.access') || ''
    refreshToken.value = sessionStorage.getItem('aiyara.refresh') || ''
    expiresAt.value = sessionStorage.getItem('aiyara.expires') || ''
    tenantId.value = sessionStorage.getItem('aiyara.tenant') || ''
  }

  function save(value: AuthResponse) {
    token.value = value.accessToken
    refreshToken.value = value.refreshToken
    expiresAt.value = value.accessExpiresAtUtc
    if (import.meta.client) {
      sessionStorage.setItem('aiyara.access', token.value)
      sessionStorage.setItem('aiyara.refresh', refreshToken.value)
      sessionStorage.setItem('aiyara.expires', expiresAt.value)
    }
  }

  async function login(tenantGuid: string, email: string, password: string) {
    const response = await $fetch<AuthResponse>(`${config.public.gatewayUrl}/api/v1/auth/login`, {
      method: 'POST', body: { tenantId: tenantGuid, email, password }
    })
    save(response)
    tenantId.value = tenantGuid
    if (import.meta.client) sessionStorage.setItem('aiyara.tenant', tenantGuid)
    return response
  }

  async function renew() {
    if (token.value && Date.parse(expiresAt.value) > Date.now() + 60_000) return true
    if (!refreshToken.value) return false
    try {
      const response = await $fetch<AuthResponse>(`${config.public.gatewayUrl}/api/v1/auth/refresh`, {
        method: 'POST', body: { refreshToken: refreshToken.value }
      })
      save(response)
      return true
    } catch {
      clear()
      return false
    }
  }

  function clear() {
    token.value = ''
    refreshToken.value = ''
    expiresAt.value = ''
    tenantId.value = ''
    if (import.meta.client) {
      sessionStorage.removeItem('aiyara.access')
      sessionStorage.removeItem('aiyara.refresh')
      sessionStorage.removeItem('aiyara.expires')
      sessionStorage.removeItem('aiyara.tenant')
    }
  }

  async function logout() {
    if (refreshToken.value) {
      try {
        await $fetch(`${config.public.gatewayUrl}/api/v1/auth/logout`, {
          method: 'POST', body: { refreshToken: refreshToken.value }
        })
      } catch { /* Clear local state even if the network is unavailable. */ }
    }
    clear()
  }

  return { token: readonly(token), tenantId: readonly(tenantId), login, logout, renew, restore }
}
