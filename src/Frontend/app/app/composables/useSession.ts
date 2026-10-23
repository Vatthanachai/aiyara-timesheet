import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
import { DEFAULT_FORMATS, readFormats, type Formats } from '../../../shared/date-format'

export type AuthResponse = {
  accessToken: string
  accessExpiresAtUtc: string
  refreshToken: string
  mustChangePassword: boolean
  tenantId: string
  role?: string
  /** Present only on the response that completes a required enrollment; shown once. */
  recoveryCodes?: string[] | null
}
export type SecondFactor = { totpCode?: string; recoveryCode?: string }

export type SignInOrganization = { organizationCode: string; name: string }
type SignInResponse = { session: AuthResponse | null; organizations: SignInOrganization[] }
export type MfaEnrollment = { secret: string; provisioningUri: string }
export type PlatformAdminLoginResult = {
  session: AuthResponse | null
  enrollment: MfaEnrollment | null
  recoveryCodes: string[]
}

/** When this browser last signed out, as the signed-out page prints it. */
export type SignOutRecord = { date: string; time: string; formats: Formats; admin: boolean }
const signOutKey = 'aiyara.signed-out'

export function readSignOut(): SignOutRecord | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(signOutKey) || 'null')
    return value?.date && value?.time ? { ...value, formats: readFormats(value.formats) } : null
  } catch { return null }
}

export function useSession() {
  const token = useState<string>('access-token', () => '')
  const refreshToken = useState<string>('refresh-token', () => '')
  const expiresAt = useState<string>('expires-at', () => '')
  const tenantId = useState<string>('tenant-id', () => '')
  const role = useState<string>('account-role', () => '')
  const config = useRuntimeConfig()
  const offlineData = useOfflineData()

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

  /**
   * Signs a member in. A second factor is repeated on the same call after MFAREQUIRED. A session that
   * comes with first-time recovery codes is not saved here: the caller saves it with `acceptSession`
   * once the codes have been shown.
   */
  async function login(organizationCode: string, email: string, password: string, factor: SecondFactor = {}) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<AuthResponse>>(`${config.public.gatewayUrl}/api/v1/auth/login`, {
      method: 'POST', body: { organizationCode, email, password, ...factor }
    }))
    if (!response.recoveryCodes?.length) save(response)
    return response
  }

  /** Signs in by email alone; returns the organizations to choose from when there are several. */
  async function signIn(email: string, password: string, factor: SecondFactor = {}) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<SignInResponse>>(`${config.public.gatewayUrl}/api/v1/auth/sign-in`, {
      method: 'POST', body: { email, password, ...factor }
    }))
    if (response.session && !response.session.recoveryCodes?.length) save(response.session)
    return response
  }

  /** The authenticator secret for a member whose organization requires MFA (after MFA_ENROLLMENT_REQUIRED). */
  async function beginMemberEnrollment(email: string, password: string) {
    return unwrapResponse(await $fetch<ApiEnvelope<MfaEnrollment>>(`${config.public.gatewayUrl}/api/v1/auth/mfa/enrollment`, {
      method: 'POST', body: { email, password }
    }))
  }

  /**
   * Signs a system administrator in. Without a second factor the server either offers an
   * authenticator enrollment or answers MFAREQUIRED. A session that comes with first-time
   * recovery codes is not saved here: the caller saves it with `acceptSession` once the codes
   * have been shown.
   */
  async function loginPlatformAdmin(email: string, password: string,
    factor: { totpCode?: string; recoveryCode?: string } = {}) {
    const response = unwrapResponse(await $fetch<ApiEnvelope<PlatformAdminLoginResult>>(`${config.public.gatewayUrl}/api/v1/auth/platform-admin/login`, {
      method: 'POST', body: { email, password, ...factor }
    }))
    if (response.session && !response.recoveryCodes.length) save(response.session)
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
    // Kept past the sign-out so the signed-out page can say when it happened, in the organization's formats.
    if (import.meta.client) {
      const now = new Date()
      const pad = (value: number) => String(value).padStart(2, '0')
      const record: SignOutRecord = {
        date: `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`,
        time: `${pad(now.getHours())}:${pad(now.getMinutes())}`,
        formats: useState<Formats>('organization-formats', () => DEFAULT_FORMATS).value,
        admin: role.value === 'PlatformAdmin'
      }
      sessionStorage.setItem(signOutKey, JSON.stringify(record))
    }
    if (refreshToken.value) {
      try {
        await $fetch(`${config.public.gatewayUrl}/api/v1/auth/logout`, {
          method: 'POST', body: { refreshToken: refreshToken.value }
        })
      } catch { /* Clear local state even if the network is unavailable. */ }
    }
    // Time kept on this device for offline use goes with the session (decision 13 of the Mobile And Offline Plan).
    if (role.value !== 'PlatformAdmin') await offlineData.clear()
    clear()
  }

  return { token: readonly(token), tenantId: readonly(tenantId), role: readonly(role), login, signIn, beginMemberEnrollment, loginPlatformAdmin, acceptSession: save, logout, renew, restore }
}
