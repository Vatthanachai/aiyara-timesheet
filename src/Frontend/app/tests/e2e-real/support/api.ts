import { activationSubject, waitForMailCode } from './mail'
import { password, sleep, stack, unique } from './stack'
import { Authenticator } from './totp'

/**
 * Gateway calls for setting a spec up (an organization, an activated member, an authenticator) where
 * that setup is not what the spec tests. The flow under test always goes through the browser. Every
 * call creates new data with a unique name, so specs never depend on each other or on earlier runs.
 */
export async function call<T = unknown>(method: string, path: string, body?: unknown, token?: string): Promise<T> {
  let response: Response
  // Gateway's per-minute limits answer 429; the window is a minute, so wait it out and try again.
  for (let attempt = 0; ; attempt++) {
    response = await fetch(`${stack.gatewayUrl}/api/v1${path}`, {
      method,
      headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body)
    })
    if (response.status !== 429 || attempt >= 4) break
    await sleep(15_000)
  }
  const text = await response.text()
  if (!response.ok) throw new Error(`${method} ${path} answered ${response.status}: ${text}`)
  return (text ? (JSON.parse(text) as { result: T }).result : undefined) as T
}

type Session = { accessToken: string }
export type Organization = {
  tenantId: string; organizationCode: string; name: string; adminEmail: string; adminToken: string
}

/** A new organization with an activated Tenant Admin, created by public sign-up, with the admin signed in. */
export async function createOrganization(label = 'Org'): Promise<Organization> {
  const id = unique()
  const adminEmail = `${label.toLowerCase()}-admin-${id}@example.test`
  const name = `Real E2E ${label} ${id}`
  const created = await call<{ tenantId: string; organizationCode: string }>('POST', '/tenants',
    { name, slug: `real-e2e-${label.toLowerCase()}-${id}`, adminEmail })
  await call('POST', '/auth/activate', { code: await waitForMailCode(adminEmail, activationSubject), password })
  const session = await call<Session>('POST', '/auth/login', { organizationCode: created.organizationCode, email: adminEmail, password })
  return { ...created, name, adminEmail, adminToken: session.accessToken }
}

/** An employee invited by the Tenant Admin, who accepted and activated the account. */
export async function addEmployee(organization: Organization, label = 'employee') {
  const email = `${label}-${unique()}@example.test`
  const invitation = await call<{ code: string }>('POST', `/auth/tenants/${organization.tenantId}/invitations`,
    { email, role: 'Employee' }, organization.adminToken)
  await call('POST', '/invitations/accept', { code: invitation.code })
  await call('POST', '/auth/activate', { code: await waitForMailCode(email, activationSubject), password })
  return { email, password }
}

/** Gives a signed-in Tenant Admin an authenticator app, the way the profile page does. */
export async function enrollAuthenticator(token: string) {
  const enrollment = await call<{ secret: string }>('POST', '/identity/mfa/enrollment', undefined, token)
  const authenticator = new Authenticator(enrollment.secret)
  await call('POST', '/identity/mfa/enrollment/confirm', { totpCode: await authenticator.code() }, token)
  return authenticator
}
