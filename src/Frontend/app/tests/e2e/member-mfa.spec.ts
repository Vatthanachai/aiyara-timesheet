import { expect, test, type Page, type Route } from '@playwright/test'

// Member MFA with mocked APIs: the sign-in steps, forced enrollment, the profile section, and the
// Tenant Admin requirement switch and per-member reset.

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const session = (name: string, role = 'Employee', extra: object = {}) => ({
  accessToken: `${name}-token`, refreshToken: `${name}-refresh`, mustChangePassword: false, tenantId, role,
  accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
  refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(), ...extra
})
const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  ({ status, headers, contentType: 'application/json', body: JSON.stringify(body) })
const failure = (status: number, errorCode: string, headers: Record<string, string> = {}) =>
  json(status, { statusCode: status, statusMessage: 'Error', errorCode }, headers)
const recoveryCodes = Array.from({ length: 10 }, (_, index) => `ABCD${index}-EFGH${index}`)
const secret = 'JBSWY3DPEHPK3PXP'
const provisioningUri = `otpauth://totp/Aiyara:ada@example.test?secret=${secret}&issuer=Aiyara`

async function waitForHydration(page: Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}
async function startSignIn(page: Page) {
  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('ada@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
}
const storedToken = (page: Page) => page.evaluate(() => sessionStorage.getItem('aiyara.access'))

test('a member with an authenticator answers the code step, can use a recovery code, and sees clear errors', async ({ page }) => {
  const requests: Record<string, unknown>[] = []
  await page.route('**/api/v1/auth/sign-in', async route => {
    const body = route.request().postDataJSON()
    requests.push(body)
    if (body.recoveryCode === 'ABCDE-FGHJK') return route.fulfill(json(200, apiOk({ session: session('member'), organizations: [] })))
    if (!body.totpCode && !body.recoveryCode) return route.fulfill(failure(401, 'MFAREQUIRED'))
    if (body.totpCode === '123456') return route.fulfill(failure(401, 'MFAINVALID'))
    return route.fulfill(failure(401, 'MFAINVALID'))
  })
  await startSignIn(page)

  // The code step names itself, takes focus, and never reaches the URL.
  const code = page.getByLabel('Authentication code')
  await expect(page.getByRole('form', { name: 'Verify it is you' })).toBeVisible()
  await expect(code).toBeFocused()
  await expect(page.getByLabel('Password')).toHaveCount(0)
  expect(page.url()).not.toContain('code')

  // Spaces typed in the code are dropped; the same request is repeated with it.
  await code.fill('123 456')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('alert')).toContainText('That code is not valid, has expired, or was already used')
  expect(requests.at(-1)).toEqual({ email: 'ada@example.test', password: 'test-password', totpCode: '123456' })
  await expect(code).toHaveValue('')
  await expect(code).toBeFocused()
  expect(await storedToken(page)).toBeNull()

  // A recovery code is sent as such, and the sign-in completes.
  await page.getByRole('button', { name: 'Use a recovery code instead' }).click()
  const recovery = page.getByLabel('Recovery code')
  await expect(recovery).toBeFocused()
  await recovery.fill('abcde-fghjk')
  await page.getByRole('button', { name: 'Continue' }).click()
  expect(requests.at(-1)).toEqual({ email: 'ada@example.test', password: 'test-password', recoveryCode: 'ABCDE-FGHJK' })
  await expect.poll(() => storedToken(page)).toBe('member-token')
  await expect(page.getByRole('link', { name: 'Record time' })).toBeVisible()
})

test('the code step explains a lockout, and Start over returns to the password with focus on the email', async ({ page }) => {
  await page.route('**/api/v1/auth/sign-in', route => {
    const body = route.request().postDataJSON()
    return route.fulfill(body.totpCode ? failure(401, 'INVALIDCREDENTIALS') : failure(401, 'MFAREQUIRED'))
  })
  await startSignIn(page)
  await page.getByLabel('Authentication code').fill('000000')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('alert')).toContainText('Wait 15 minutes, then start over')
  await page.getByRole('button', { name: 'Start over' }).click()
  await expect(page.getByLabel('Email')).toBeFocused()
  await expect(page.getByLabel('Password')).toHaveValue('')
})

test('the code step is in Thai, and a rate limit has its own message', async ({ page }) => {
  await page.route('**/api/v1/auth/sign-in', route => {
    const body = route.request().postDataJSON()
    return route.fulfill(body.totpCode ? failure(429, 'RATELIMITED') : failure(401, 'MFAREQUIRED'))
  })
  await page.goto('/')
  await waitForHydration(page)
  await page.getByRole('radio', { name: 'ไทย' }).click()
  await page.getByLabel('อีเมล').fill('ada@example.test')
  await page.getByLabel('รหัสผ่าน').fill('test-password')
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()
  await expect(page.getByRole('form', { name: 'ยืนยันตัวตน' })).toBeVisible()
  await page.getByLabel('รหัสยืนยัน').fill('123456')
  await page.getByRole('button', { name: 'ดำเนินการต่อ' }).click()
  await expect(page.getByRole('alert')).toContainText('ลองถี่เกินไปหรือบัญชีถูกล็อกชั่วคราว')
  await expect(page.getByRole('button', { name: 'ใช้รหัสกู้คืนแทน' })).toBeVisible()
})

test('choosing an organization of a multi-organization account asks for the code at that login', async ({ page }) => {
  const logins: Record<string, unknown>[] = []
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill(json(200, apiOk({
    session: null, organizations: [{ organizationCode: 'ACME123', name: 'Acme' }, { organizationCode: 'BETA456', name: 'Beta' }]
  }))))
  await page.route('**/api/v1/auth/login', route => {
    const body = route.request().postDataJSON()
    logins.push(body)
    return route.fulfill(body.totpCode ? json(200, apiOk(session('beta'))) : failure(401, 'MFAREQUIRED'))
  })
  await startSignIn(page)
  await page.getByRole('button', { name: 'Beta (BETA456)' }).click()
  await page.getByLabel('Authentication code').fill('654321')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect.poll(() => storedToken(page)).toBe('beta-token')
  expect(logins).toEqual([
    { organizationCode: 'BETA456', email: 'ada@example.test', password: 'test-password' },
    { organizationCode: 'BETA456', email: 'ada@example.test', password: 'test-password', totpCode: '654321' }
  ])
})

test('forced enrollment shows the secret and QR, then the one-time recovery codes that must be acknowledged', async ({ page }) => {
  const signIns: Record<string, unknown>[] = []
  let enrollmentBody: unknown
  await page.route('**/api/v1/auth/mfa/enrollment', route => {
    enrollmentBody = route.request().postDataJSON()
    return route.fulfill(json(200, apiOk({ secret, provisioningUri })))
  })
  await page.route('**/api/v1/auth/sign-in', route => {
    const body = route.request().postDataJSON()
    signIns.push(body)
    if (!body.totpCode) return route.fulfill(failure(401, 'MFA_ENROLLMENT_REQUIRED'))
    if (body.totpCode !== '246810') return route.fulfill(failure(401, 'MFAINVALID'))
    return route.fulfill(json(200, apiOk({ session: session('enrolled', 'Employee', { recoveryCodes }), organizations: [] })))
  })
  await startSignIn(page)

  // The secret and QR come from the password-checked enrollment call; the heading takes focus.
  const heading = page.getByRole('heading', { name: 'Set up an authenticator to sign in' })
  await expect(heading).toBeFocused()
  expect(enrollmentBody).toEqual({ email: 'ada@example.test', password: 'test-password' })
  await expect(page.getByText('JBSW Y3DP EHPK 3PXP')).toBeVisible()
  await expect(page.getByRole('img', { name: 'QR code to add this account to an authenticator app' })).toHaveAttribute('src', /^data:image\/svg\+xml/)
  await expect(page.getByRole('link', { name: 'Open in authenticator app' })).toHaveAttribute('href', provisioningUri)
  expect(page.url()).not.toContain(secret)

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 800 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }

  await page.getByLabel('Authentication code').fill('111111')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('alert')).toContainText('That code is not valid')
  await page.getByLabel('Authentication code').fill('246810')
  await page.getByRole('button', { name: 'Continue' }).click()
  expect(signIns.at(-1)).toEqual({ email: 'ada@example.test', password: 'test-password', totpCode: '246810' })

  // The codes are shown once; the session is not kept until they are acknowledged.
  await expect(page.getByRole('heading', { name: 'Save your recovery codes' })).toBeFocused()
  await expect(page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem')).toHaveCount(10)
  await expect(page.getByRole('list', { name: 'Recovery codes' })).toContainText('ABCD0-EFGH0')
  expect(await storedToken(page)).toBeNull()
  const next = page.getByRole('button', { name: 'Continue' })
  await expect(next).toBeDisabled()
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 800 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }
  await page.getByRole('checkbox', { name: 'I have saved these codes' }).check()
  await expect(next).toBeEnabled()
  await next.click()
  await expect.poll(() => storedToken(page)).toBe('enrolled-token')
  await expect(page.getByRole('link', { name: 'Record time' })).toBeVisible()
  expect(await page.evaluate(() => JSON.stringify(Object.entries(sessionStorage)))).not.toContain('ABCD0')
})

test('an enrollment offer that fails shows a clear message instead of a code', async ({ page }) => {
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill(failure(401, 'MFA_ENROLLMENT_REQUIRED')))
  await page.route('**/api/v1/auth/mfa/enrollment', route => route.fulfill(failure(503, 'UNAVAILABLE')))
  await startSignIn(page)
  await expect(page.getByRole('alert')).toHaveText('The service is temporarily unavailable. Please try again shortly.')
})

// ---------------------------------------------------------------- Profile section

type Status = { enabled: boolean; enabledAtUtc: string | null; recoveryCodesRemaining: number; requiredByOrganization: boolean; canDisable: boolean }
const profileCors = {
  'access-control-allow-origin': 'http://127.0.0.1:3105',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}

async function openProfile(page: Page, state: { status: Status; calls: string[]; confirmCode?: string }) {
  await page.route('**/api/v1/**', async (route: Route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1', '')
    const ok = (result: unknown) => route.fulfill(json(200, apiOk(result), profileCors))
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: profileCors })
    if (path === '/auth/sign-in') return ok({ session: session('member'), organizations: [] })
    if (path === '/identity/profile/me') return ok({ email: 'ada@example.test', firstName: 'Ada', lastName: 'Lovelace', photoUrl: null, jobTitle: null })
    if (path.startsWith('/identity/mfa')) {
      const sub = path.replace('/identity/mfa', '')
      if (request.method() !== 'GET') state.calls.push(`${request.method()} ${sub} ${request.postData() ?? ''}`.trim())
      if (request.method() === 'GET') return ok(state.status)
      if (sub === '/enrollment') return state.status.enabled ? route.fulfill(failure(409, 'MFA_ALREADY_ENROLLED', profileCors)) : ok({ secret, provisioningUri })
      if (sub === '/enrollment/confirm') {
        if (request.postDataJSON().totpCode !== '135790') return route.fulfill(failure(401, 'MFAINVALID', profileCors))
        state.status = { ...state.status, enabled: true, enabledAtUtc: '2026-10-22T08:30:00Z', recoveryCodesRemaining: 10 }
        return ok({ recoveryCodes })
      }
      if (sub === '/recovery-codes') {
        if (request.postDataJSON().totpCode !== '135790') return route.fulfill(failure(401, 'MFAINVALID', profileCors))
        state.status = { ...state.status, recoveryCodesRemaining: 10 }
        return ok({ recoveryCodes: recoveryCodes.map(value => value.replace('ABCD', 'WXYZ')) })
      }
      if (sub === '/disable') {
        state.status = { ...state.status, enabled: false, enabledAtUtc: null, recoveryCodesRemaining: 0 }
        return route.fulfill({ status: 204, headers: profileCors })
      }
    }
    return route.fulfill({ status: 404, headers: profileCors })
  })
  await startSignIn(page)
  await page.locator('.profile-trigger').click()
  await page.getByRole('link', { name: 'Profile settings' }).click()
  return page.frameLocator('iframe')
}

test('a member turns on two-step verification from the profile, saves the codes, renews them, and turns it off', async ({ page }) => {
  const state = { calls: [] as string[], status: { enabled: false, enabledAtUtc: null, recoveryCodesRemaining: 0, requiredByOrganization: false, canDisable: true } as Status }
  const frame = await openProfile(page, state)
  const section = frame.getByRole('region', { name: 'Two-step verification' })
  await expect(section.getByText('Off', { exact: true })).toBeVisible()
  await expect(section.getByRole('button', { name: 'Turn off' })).toHaveCount(0)

  // Enable: secret and QR, a wrong code, then the right one and the recovery codes.
  await section.getByRole('button', { name: 'Set up an authenticator app' }).click()
  await expect(section.getByRole('heading', { name: 'Set up your authenticator' })).toBeFocused()
  await expect(section.getByText('JBSW Y3DP EHPK 3PXP')).toBeVisible()
  await expect(section.getByRole('img', { name: /QR code/ })).toHaveAttribute('src', /^data:image\/svg\+xml/)
  await section.getByLabel('Authentication code').fill('000000')
  await section.getByRole('button', { name: 'Turn on' }).click()
  await expect(section.getByRole('alert')).toContainText('That code is not valid')
  await section.getByLabel('Authentication code').fill('135790')
  await section.getByRole('button', { name: 'Turn on' }).click()
  await expect(section.getByRole('heading', { name: 'Turned on. Save your recovery codes' })).toBeFocused()
  await expect(section.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem')).toHaveCount(10)
  await expect(section.getByRole('status').filter({ hasText: 'Two-step verification is on.' })).toBeVisible()
  await expect(section.getByRole('button', { name: 'Continue' })).toBeDisabled()
  await section.getByRole('checkbox', { name: 'I have saved these codes' }).check()
  await section.getByRole('button', { name: 'Continue' }).click()
  await expect(section.getByText('On since 22/10/2026')).toBeVisible()
  await expect(section.getByText('10 of 10 recovery codes left')).toBeVisible()
  expect(state.calls).toEqual(['POST /enrollment', 'POST /enrollment/confirm {"totpCode":"000000"}', 'POST /enrollment/confirm {"totpCode":"135790"}'])

  // New recovery codes need an authenticator code; the old set stops working.
  await section.getByRole('button', { name: 'Get new recovery codes' }).click()
  await expect(section.getByLabel('Authentication code')).toBeFocused()
  await expect(section.getByRole('button', { name: 'Use a recovery code instead' })).toHaveCount(0)
  await section.getByLabel('Authentication code').fill('135790')
  await section.getByRole('button', { name: 'Get new recovery codes' }).last().click()
  await expect(section.getByRole('list', { name: 'Recovery codes' })).toContainText('WXYZ0-EFGH0')
  await section.getByRole('checkbox', { name: 'I have saved these codes' }).check()
  await section.getByRole('button', { name: 'Continue' }).click()

  // Turning it off accepts a recovery code.
  await section.getByRole('button', { name: 'Turn off' }).click()
  await section.getByRole('button', { name: 'Use a recovery code instead' }).click()
  await section.getByLabel('Recovery code').fill('abcd1-efgh1')
  await section.getByRole('button', { name: 'Turn off' }).last().click()
  await expect(section.getByRole('status').filter({ hasText: 'Two-step verification is off.' })).toBeVisible()
  await expect(section.getByText('Off', { exact: true })).toBeVisible()
  expect(state.calls.at(-1)).toBe('POST /disable {"recoveryCode":"ABCD1-EFGH1"}')
  // Codes are never kept: nothing is in storage.
  expect(await page.evaluate(() => JSON.stringify(Object.entries(sessionStorage)) + JSON.stringify(Object.entries(localStorage)))).not.toContain('ABCD')

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('when the organization requires MFA the member cannot turn it off and is told why', async ({ page }) => {
  const state = { calls: [] as string[], status: { enabled: true, enabledAtUtc: '2026-10-01T08:00:00Z', recoveryCodesRemaining: 1, requiredByOrganization: true, canDisable: false } as Status }
  const frame = await openProfile(page, state)
  const section = frame.getByRole('region', { name: 'Two-step verification' })
  await expect(section.getByText('Your organization requires two-step verification for everyone, so you cannot turn it off.')).toBeVisible()
  await expect(section.getByRole('button', { name: 'Turn off' })).toHaveCount(0)
  await expect(section.getByText('Only 1 recovery codes left. Generate a new set.')).toBeVisible()
  await expect(section.getByRole('button', { name: 'Get new recovery codes' })).toBeVisible()
})

// ---------------------------------------------------------------- Administration

const adminCors = {
  'access-control-allow-origin': 'http://127.0.0.1:3104',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}

async function openAdministration(page: Page, state: { requireMfa: boolean; adminEnrolled: boolean; calls: string[]; members: Record<string, unknown>[] }) {
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1', '')
    const ok = (result: unknown) => route.fulfill(json(200, apiOk(result), adminCors))
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: adminCors })
    if (path === '/auth/sign-in') return ok({ session: session('admin', 'TenantAdmin'), organizations: [] })
    if (path === '/timesheets/entry-templates') return ok([])
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [] })
    if (path === '/timesheets/reminders') return ok({ dailyEnabled: false, monthEndEnabled: false, sendAt: '09:00' })
    if (path === `/identity/tenants/${tenantId}/password-policy`)
      return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (path === '/identity/profile/me') return ok({ email: 'ada@example.test', firstName: 'Ada', lastName: 'Admin', photoUrl: null })
    if (path === '/identity/members') return ok(state.members)
    if (path === '/identity/members/invitations' || path === '/identity/members/groups') return ok([])
    if (path === '/identity/organization' && request.method() === 'GET') return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null, requireMfa: state.requireMfa })
    if (path === '/identity/organization/mfa') {
      state.calls.push(`PUT ${path} ${request.postData()}`)
      if (!state.adminEnrolled) return route.fulfill(failure(409, 'MFA_NOT_ENROLLED', adminCors))
      state.requireMfa = request.postDataJSON().required
      return ok({ requireMfa: state.requireMfa })
    }
    const reset = path.match(/^\/identity\/members\/([^/]+)\/mfa-reset$/)
    if (reset) {
      state.calls.push(`POST ${path} ${request.postData()}`)
      if (reset[1] === 'gone') return route.fulfill(failure(404, 'NOT_FOUND', adminCors))
      const member = state.members.find(item => item.id === reset[1])
      if (member) member.mfaEnabled = false
      return route.fulfill({ status: 204, headers: adminCors })
    }
    return route.fulfill({ status: 404, headers: adminCors })
  })
  await startSignIn(page)
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()
  return page.frameLocator('iframe')
}

test('a tenant admin turns on the MFA requirement after a clear warning and is guided when not enrolled', async ({ page }) => {
  const state = { requireMfa: false, adminEnrolled: false, calls: [] as string[], members: [] as Record<string, unknown>[] }
  const frame = await openAdministration(page, state)
  await frame.getByRole('tab', { name: 'Security' }).click()
  const section = frame.getByRole('region', { name: 'Multi-factor authentication (MFA)' })
  const toggle = section.getByRole('switch', { name: 'Require MFA for all members' })
  await expect(toggle).toHaveAttribute('aria-checked', 'false')

  // The warning comes first; nothing is sent until it is confirmed, and Escape backs out to the switch.
  await toggle.click()
  const confirm = section.getByRole('form', { name: 'Turn on the MFA requirement' })
  await expect(confirm).toContainText('Members who have no authenticator app are signed out now')
  await expect(section.getByLabel('Reason (optional)')).toBeFocused()
  expect(state.calls).toEqual([])
  await page.keyboard.press('Escape')
  await expect(confirm).toHaveCount(0)
  await expect(toggle).toBeFocused()

  // An admin without an authenticator is told how to get one.
  await toggle.click()
  await section.getByLabel('Reason (optional)').fill('Audit finding 42')
  await section.getByRole('button', { name: 'Turn on and sign out members without MFA' }).click()
  await expect(section.getByRole('alert')).toContainText('Set up your own authenticator first')
  expect(state.calls.at(-1)).toBe('PUT /identity/organization/mfa {"required":true,"reason":"Audit finding 42"}')
  await expect(toggle).toHaveAttribute('aria-checked', 'false')

  state.adminEnrolled = true
  await section.getByRole('button', { name: 'Turn on and sign out members without MFA' }).click()
  await expect(section.getByRole('status')).toHaveText('MFA is now required for all members.')
  await expect(toggle).toHaveAttribute('aria-checked', 'true')
  await expect(toggle).toBeFocused()

  // Turning it off is confirmed too, and needs no reason.
  await toggle.click()
  await expect(section.getByRole('form', { name: 'Stop requiring MFA' })).toBeVisible()
  await section.getByRole('button', { name: 'Stop requiring MFA' }).click()
  await expect(section.getByRole('status')).toHaveText('MFA is no longer required.')
  expect(state.calls.at(-1)).toBe('PUT /identity/organization/mfa {"required":false}')

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('the members list shows each member\'s MFA status and resets it after a confirmation with a reason', async ({ page }) => {
  const state = {
    requireMfa: true, adminEnrolled: true, calls: [] as string[],
    members: [
      { id: 'a1', firstName: 'Ada', lastName: 'Admin', email: 'ada@example.test', role: 'TenantAdmin', status: 'Active', isCurrentUser: true, groupId: null, groupName: null, mfaEnabled: true },
      { id: 'a2', firstName: 'Zoe', lastName: 'Staff', email: 'zoe@example.test', role: 'Employee', status: 'Active', isCurrentUser: false, groupId: null, groupName: null, mfaEnabled: true },
      { id: 'a3', firstName: 'New', lastName: 'Hire', email: 'new.hire@example.test', role: 'Employee', status: 'Active', isCurrentUser: false, groupId: null, groupName: null, mfaEnabled: false }
    ]
  }
  const frame = await openAdministration(page, state)
  await frame.getByRole('tab', { name: /People/ }).click()
  const rows = frame.getByRole('table', { name: 'Members' }).getByRole('row')
  await expect(rows.filter({ hasText: 'zoe@example.test' }).getByText('MFA on')).toBeVisible()
  await expect(rows.filter({ hasText: 'new.hire@example.test' }).getByText('MFA off')).toBeVisible()
  // Resetting yourself is not offered, and a member without an authenticator has nothing to reset.
  await expect(frame.getByRole('button', { name: /^Reset MFA/ })).toHaveCount(1)

  const open = frame.getByRole('button', { name: 'Reset MFA: zoe@example.test' })
  await open.click()
  const form = frame.getByRole('form', { name: 'Reset MFA for zoe@example.test' })
  await expect(form).toContainText('signs them out everywhere')
  await expect(form.getByLabel('Reason (optional)')).toBeFocused()
  expect(state.calls).toEqual([])
  await page.keyboard.press('Escape')
  await expect(form).toHaveCount(0)
  await expect(open).toBeFocused()

  await open.click()
  await frame.getByLabel('Reason (optional)').fill('Lost phone')
  await frame.getByRole('button', { name: 'Reset MFA', exact: true }).click()
  await expect(frame.getByRole('status').filter({ hasText: 'Reset MFA for zoe@example.test.' })).toBeVisible()
  expect(state.calls).toEqual(['POST /identity/members/a2/mfa-reset {"reason":"Lost phone"}'])
  await expect(rows.filter({ hasText: 'zoe@example.test' }).getByText('MFA off')).toBeVisible()
  await expect(frame.getByRole('button', { name: /^Reset MFA/ })).toHaveCount(0)
})
