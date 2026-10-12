import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'

export type PlatformAdminSummary = { email: string; status: 'Active' | 'PendingActivation' }
export type PlatformOrganizationSummary = {
  tenantId: string
  organizationCode: string
  name: string
  slug: string
  status: 'Active' | 'Suspended'
  createdAtUtc: string
  admins: PlatformAdminSummary[]
}
export type PlatformAuditEntry = {
  id: string
  createdAtUtc: string
  action: string
  tenantId: string
  organizationName: string | null
  organizationCode: string | null
  actorEmail: string | null
  reason: string | null
  beforeJson: string | null
  afterJson: string | null
}
export type PlatformAuditPage = { totalCount: number; page: number; pageSize: number; items: PlatformAuditEntry[] }

/**
 * Session plumbing shared by the SysAdmin pages. The server re-checks every platform call;
 * `isPlatformAdmin` only decides what the page renders.
 */
export function usePlatformApi() {
  const { token, role, renew, restore } = useSession()
  const config = useRuntimeConfig()
  const isPlatformAdmin = computed(() => Boolean(token.value) && role.value === 'PlatformAdmin')

  /** Calls a `/api/v1/platform/**` route, renewing the session once on 401. */
  async function platformGet<T>(path: string, query?: Record<string, unknown>): Promise<T> {
    return platformSend<T>(path, { query })
  }

  async function platformSend<T>(path: string, options: Record<string, unknown> = {}): Promise<T> {
    const send = () => $fetch<ApiEnvelope<T>>(`${config.public.gatewayUrl}/api/v1/platform${path}`, {
      ...options,
      headers: { ...(options.headers as Record<string, string> | undefined), Authorization: `Bearer ${token.value}` }
    })
    try { return unwrapResponse(await send()) }
    catch (cause) {
      const status = (cause as { status?: number })?.status
      if (status !== 401 || !(await renew(true))) throw cause
      return unwrapResponse(await send())
    }
  }

  /** Restores the stored session and reports whether the page may load platform data. */
  async function ready() {
    restore()
    await nextTick()
    return (await renew()) && isPlatformAdmin.value
  }

  return { isPlatformAdmin, platformGet, platformSend, ready }
}
