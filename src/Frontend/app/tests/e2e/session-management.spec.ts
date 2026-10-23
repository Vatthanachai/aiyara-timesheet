import { expect, test, type Page, type Route } from '@playwright/test'

// Session management with mocked APIs: the signed-in devices list on the profile page, signing out
// one device, the others, and everywhere.

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const session = (name: string) => ({
  accessToken: `${name}-token`, refreshToken: `${name}-refresh`, mustChangePassword: false, tenantId, role: 'Employee',
  accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
  refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
})
const cors = {
  'access-control-allow-origin': 'http://127.0.0.1:3105',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}
const json = (status: number, body: unknown) =>
  ({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(body) })
const failure = (status: number, errorCode: string) => json(status, { statusCode: status, statusMessage: 'Error', errorCode })

type Item = { id: string; device: string; signedInAtUtc: string; lastUsedAtUtc: string; expiresAtUtc: string; current: boolean }
const item = (id: string, device: string, current = false, lastUsed = '2026-10-23T09:45:00Z'): Item => ({
  id, device, current, signedInAtUtc: '2026-10-20T01:30:00Z', lastUsedAtUtc: lastUsed, expiresAtUtc: '2026-11-20T01:30:00Z'
})
const initial = () => [
  item('s-current', 'Chrome on Windows', true), item('s-phone', 'Safari on iOS', false, '2026-10-22T08:00:00Z'),
  item('s-mac', 'Firefox on macOS', false, '2026-10-21T08:00:00Z')
]

type State = {
  sessions: Item[]; calls: string[]
  failList?: boolean
  deleteResult?: (id: string) => { status: number; code?: string } | undefined
  othersResult?: { status: number; code?: string }
}

async function waitForHydration(page: Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

async function openProfile(page: Page, state: State) {
  await page.route('**/api/v1/**', async (route: Route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1', '')
    const ok = (result: unknown) => route.fulfill(json(200, apiOk(result)))
    const noContent = () => route.fulfill({ status: 204, headers: cors })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (path === '/auth/sign-in') return route.fulfill({ ...json(200, apiOk({ session: session('member'), organizations: [] })), headers: {} })
    if (path === '/auth/logout') { state.calls.push('POST /auth/logout'); return route.fulfill({ status: 204 }) }
    if (path === '/identity/profile/me') return ok({ email: 'ada@example.test', firstName: 'Ada', lastName: 'Lovelace', photoUrl: null, jobTitle: null })
    if (path.startsWith('/identity/mfa')) return ok({ enabled: false, enabledAtUtc: null, recoveryCodesRemaining: 0, requiredByOrganization: false, canDisable: true })
    if (path.startsWith('/identity/sessions')) {
      const sub = path.replace('/identity/sessions', '')
      if (request.method() === 'GET') {
        if (state.failList) return route.fulfill(failure(503, 'UNAVAILABLE'))
        return ok(state.sessions)
      }
      state.calls.push(`${request.method()} ${sub}`)
      if (request.method() === 'DELETE') {
        const id = sub.slice(1)
        const forced = state.deleteResult?.(id)
        if (forced) return route.fulfill(failure(forced.status, forced.code ?? 'ERROR'))
        state.sessions = state.sessions.filter(value => value.id !== id)
        return noContent()
      }
      if (sub === '/revoke-others') {
        if (state.othersResult) return route.fulfill(failure(state.othersResult.status, state.othersResult.code ?? 'ERROR'))
        state.sessions = state.sessions.filter(value => value.current)
        return noContent()
      }
      if (sub === '/revoke-all') { state.sessions = []; return noContent() }
    }
    return route.fulfill({ status: 404, headers: cors })
  })
  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('ada@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.locator('.profile-trigger').click()
  await page.getByRole('link', { name: 'Profile settings' }).click()
  return page.frameLocator('iframe')
}
const sectionOf = (frame: ReturnType<Page['frameLocator']>) => frame.getByRole('region', { name: 'Signed-in devices' })

test('the profile lists signed-in devices with the current one marked and no sign out for it', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [] }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  const rows = section.getByRole('listitem')
  await expect(rows).toHaveCount(3)
  await expect(rows.nth(0)).toContainText('Chrome on Windows')
  await expect(rows.nth(0).getByText('This device')).toBeVisible()
  await expect(rows.nth(0).getByRole('button')).toHaveCount(0)
  // Times use the organization's formats (default dd/MM/yyyy and 24-hour) in the browser's time zone.
  await expect(rows.nth(1)).toContainText('Safari on iOS')
  await expect(rows.nth(1)).toContainText(/Signed in \d{2}\/10\/2026 \d{2}:\d{2}/)
  await expect(rows.nth(1)).toContainText(/Last used about \d{2}\/10\/2026 \d{2}:\d{2}/)
  await expect(rows.nth(1).getByText('This device')).toHaveCount(0)
  await expect(rows.nth(1).getByRole('button', { name: 'Sign out Safari on iOS' })).toBeVisible()
  await expect(section.getByText(/keep working for up to 15 minutes/)).toBeVisible()
  await expect(section.getByRole('button', { name: 'Sign out other devices' })).toBeVisible()
  await expect(section.getByRole('button', { name: 'Sign out everywhere' })).toBeVisible()
})

test('signing out one device asks first, can be cancelled, and refreshes the list', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [] }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  await section.getByRole('button', { name: 'Sign out Safari on iOS' }).click()
  await expect(section.getByRole('button', { name: 'Yes, sign out' })).toBeFocused()
  await section.getByRole('button', { name: 'Cancel' }).click()
  await expect(section.getByRole('button', { name: 'Sign out Safari on iOS' })).toBeFocused()
  expect(state.calls).toEqual([])

  await section.getByRole('button', { name: 'Sign out Safari on iOS' }).click()
  await section.getByRole('button', { name: 'Yes, sign out' }).click()
  await expect(section.getByRole('status').filter({ hasText: 'That device was signed out.' })).toBeVisible()
  await expect(section.getByRole('listitem')).toHaveCount(2)
  await expect(section.getByText('Safari on iOS')).toHaveCount(0)
  expect(state.calls).toEqual(['DELETE /s-phone'])
})

test('a device that is already gone is treated as signed out and the list is refreshed', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [], deleteResult: id => id === 's-phone' ? { status: 404, code: 'NOT_FOUND' } : undefined }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  await expect(section.getByRole('listitem')).toHaveCount(3)
  state.sessions = state.sessions.filter(value => value.id !== 's-phone')
  await section.getByRole('button', { name: 'Sign out Safari on iOS' }).click()
  await section.getByRole('button', { name: 'Yes, sign out' }).click()
  await expect(section.getByRole('status').filter({ hasText: 'That device was already signed out.' })).toBeVisible()
  await expect(section.getByRole('alert')).toHaveCount(0)
  await expect(section.getByRole('listitem')).toHaveCount(2)
})

test('sign out other devices asks first and keeps this device; an old session is told to sign in again', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [], othersResult: { status: 409, code: 'SESSION_UNKNOWN' } }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  await section.getByRole('button', { name: 'Sign out other devices' }).click()
  await expect(section.getByText('This device stays signed in.')).toBeVisible()
  await section.getByRole('button', { name: 'Yes, sign out other devices' }).click()
  await expect(section.getByRole('alert')).toContainText('Sign out and sign in again')
  await expect(section.getByRole('listitem')).toHaveCount(3)

  state.othersResult = undefined
  await section.getByRole('button', { name: 'Sign out other devices' }).click()
  await section.getByRole('button', { name: 'Yes, sign out other devices' }).click()
  await expect(section.getByRole('status').filter({ hasText: 'Other devices were signed out.' })).toBeVisible()
  await expect(section.getByRole('listitem')).toHaveCount(1)
  await expect(section.getByRole('button', { name: 'Sign out other devices' })).toHaveCount(0)
  expect(state.calls).toEqual(['POST /revoke-others', 'POST /revoke-others'])
})

test('sign out everywhere confirms, then clears this session and returns to the signed-out page', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [] }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  await section.getByRole('button', { name: 'Sign out everywhere' }).click()
  await expect(section.getByText(/including this one/)).toBeVisible()
  await section.getByRole('button', { name: 'Cancel' }).click()
  expect(state.calls).toEqual([])
  await section.getByRole('button', { name: 'Sign out everywhere' }).click()
  await section.getByRole('button', { name: 'Yes, sign out everywhere' }).click()
  await expect(page).toHaveURL(/\/signed-out$/)
  expect(state.calls).toContain('POST /revoke-all')
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.access'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.refresh'))).toBeNull()
})

test('a failed load shows an error with a retry, and an empty list says so', async ({ page }) => {
  const state: State = { sessions: [], calls: [], failList: true }
  const frame = await openProfile(page, state)
  const section = sectionOf(frame)
  await expect(section.getByRole('alert')).toContainText('temporarily unavailable')
  await expect(section.getByRole('listitem')).toHaveCount(0)
  state.sessions = initial()
  state.failList = false
  await section.getByRole('button', { name: 'Try again' }).click()
  await expect(section.getByRole('listitem')).toHaveCount(3)
  await expect(section.getByRole('alert')).toHaveCount(0)
})

test('with no sessions listed the section says so and still offers sign out everywhere', async ({ page }) => {
  const frame = await openProfile(page, { sessions: [], calls: [] })
  const section = sectionOf(frame)
  await expect(section.getByText('No signed-in devices found.')).toBeVisible()
  await expect(section.getByRole('button', { name: 'Sign out other devices' })).toHaveCount(0)
  await expect(section.getByRole('button', { name: 'Sign out everywhere' })).toBeVisible()
})

test('the section is in Thai and fits narrow screens without sideways scrolling', async ({ page }) => {
  const state: State = { sessions: initial(), calls: [] }
  const frame = await openProfile(page, state)
  await expect(sectionOf(frame)).toBeVisible()
  await page.getByRole('radio', { name: 'ไทย' }).click()
  const section = frame.getByRole('region', { name: 'อุปกรณ์ที่เข้าสู่ระบบ' })
  await expect(section.getByText('อุปกรณ์นี้', { exact: true })).toBeVisible()
  await expect(section.getByRole('listitem').nth(1)).toContainText('เข้าสู่ระบบเมื่อ')
  await section.getByRole('button', { name: 'ออกจากระบบอุปกรณ์ Safari on iOS' }).click()
  await expect(section.getByText('ออกจากระบบอุปกรณ์นี้ใช่หรือไม่').first()).toBeVisible()
  await section.getByRole('button', { name: 'ยกเลิก' }).click()
  await section.getByRole('button', { name: 'ออกจากระบบอุปกรณ์อื่นทั้งหมด' }).click()
  await expect(section.getByText(/อุปกรณ์นี้จะยังเข้าสู่ระบบอยู่/)).toBeVisible()
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
  await section.getByRole('button', { name: 'ยกเลิก' }).click()
  await section.getByRole('button', { name: 'ออกจากระบบอุปกรณ์ Safari on iOS' }).click()
  await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
})
