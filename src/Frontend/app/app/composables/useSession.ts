import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'

type AuthResponse = {
  accessToken: string
  accessExpiresAtUtc: string
  refreshToken: string
  mustChangePassword: boolean
  tenantId: string
  role?: string
}

export type SignInOrganization = { organizationCode: string; name: string }
type SignInResponse = { session: AuthResponse | null; organizations: SignInOrganization[] }

export function useSession() {
  const token = useState<string>('access-token', () => '')
  const refreshToken = useState<string>('refresh-token', () => '')
  const expiresAt = useState<string>('expires-at', () => '')
  const tenantId = useState<string>('tenant-id', () => '')
  const role = useState<string>('account-role', () => '')
  const config = useRuntimeConfig()

  function restore() {
    if (!import.meta.client) return
    token.value = sessionStorage.getItem('aiyara.access') || ''
    refreshToken.value = sessionStorage.getItem('aiyara.refresh') || ''
    expiresAt.value = sessionStorage.getItem('aiyara.expires') || ''
    tenantId.value = sessionStorage.getItem('aiyara.tenant') || ''
    role.value = sessionStorage.getItem('aiyara.role') || ''
  }

  function save(value: AuthResponse) {
    token.value = value.accessToken
    refreshToken.value = value.refreshToken
    expiresAt.value = value.accessExpiresAtUtc
    tenantId.value = value.tenantId
    role.value = value.role || ''
    if (import.meta.client) {
      sessionStorage.setItem('aiyara.access', token.value)
      sessionStorage.setItem('aiyara.refresh', refreshToken.value)
      sessionStorage.setItem('aiyara.expires', expiresAt.value)
      sessionStorage.setItem('aiyara.tenant', tenantId.value)
      sessionStorage.setItem('aiyara.role', role.value)
    }
  }

  async function login(organizationCode: string, email: string, password: string) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<AuthResponse>>(`${config.public.gatewayUrl}/api/v1/auth/login`, {
      method: 'POST', body: { organizationCode, email, password }
    }))
    save(response)
    return response
  }

  /** Signs in by email alone; returns the organizations to choose from when there are several. */
  async function signIn(email: string, password: string) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<SignInResponse>>(`${config.public.gatewayUrl}/api/v1/auth/sign-in`, {
      method: 'POST', body: { email, password }
    }))
    if (response.session) save(response.session)
    return response
  }

  async function loginPlatformAdmin(email: string, password: string) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<AuthResponse>>(`${config.public.gatewayUrl}/api/v1/auth/platform-admin/login`, {
      method: 'POST', body: { email, password }
    }))
    save(response)
    return response
  }

  async function renew(force = false) {
    if (!force && token.value && role.value && Date.parse(expiresAt.value) > Date.now() + 60_000) return true
    if (!refreshToken.value) return false
    try {
      const response = unwrapResponse(await $fetch<ApiEnvelope<AuthResponse>>(`${config.public.gatewayUrl}/api/v1/auth/refresh`, {
        method: 'POST', body: { refreshToken: refreshToken.value }
      }))
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
    role.value = ''
    if (import.meta.client) {
      sessionStorage.removeItem('aiyara.access')
      sessionStorage.removeItem('aiyara.refresh')
      sessionStorage.removeItem('aiyara.expires')
      sessionStorage.removeItem('aiyara.tenant')
      sessionStorage.removeItem('aiyara.role')
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

  return { token: readonly(token), tenantId: readonly(tenantId), role: readonly(role), login, signIn, loginPlatformAdmin, logout, renew, restore }
}
