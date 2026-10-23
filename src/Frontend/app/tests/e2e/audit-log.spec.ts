import { expect, test, type FrameLocator, type Page } from '@playwright/test'
import { choose, shownDate } from './fields'

// The Tenant Admin audit log in the Administration remote, with mocked APIs: labels and readable
// before/after summaries, filters, paging, empty/error/retry states, Thai, and narrow screens.

test.use({ timezoneId: 'Asia/Bangkok' })

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const cors = {
  'access-control-allow-origin': 'http://127.0.0.1:3104',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}
const json = (status: number, body: unknown) => ({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(body) })

type Entry = { id: string; createdAtUtc: string; action: string; actorEmail: string | null; beforeJson: string | null; afterJson: string | null }
const entry = (index: number, overrides: Partial<Entry> = {}): Entry => ({
  id: `entry-${index}`, createdAtUtc: new Date(Date.UTC(2026, 9, 20, 8, 0) - index * 60_000).toISOString(),
  action: 'member.invited', actorEmail: 'ada@example.test',
  beforeJson: null, afterJson: JSON.stringify({ Email: `new${index}@example.test`, Role: 'Employee' }), ...overrides
})

type State = { queries: URLSearchParams[]; fail: boolean; entries: Entry[] }

async function waitForHydration(page: Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

async function openAuditLog(page: Page, state: State): Promise<FrameLocator> {
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname.replace('/api/v1', '')
    const ok = (result: unknown) => route.fulfill(json(200, apiOk(result)))
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (path === '/auth/sign-in') return ok({ session: {
      accessToken: 'admin-token', refreshToken: 'admin-refresh', mustChangePassword: false, tenantId, role: 'TenantAdmin',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (path === '/timesheets/entry-templates') return ok([])
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [] })
    if (path === '/timesheets/reminders') return ok({ dailyEnabled: false, monthEndEnabled: false, sendAt: '09:00' })
    if (path === `/identity/tenants/${tenantId}/password-policy`)
      return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (path === '/identity/profile/me') return ok({ email: 'ada@example.test', firstName: 'Ada', lastName: 'Admin', photoUrl: null })
    if (path === '/identity/members') return ok([])
    if (path === '/identity/members/invitations' || path === '/identity/members/groups') return ok([])
    if (path === '/identity/organization') return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    if (path === '/identity/members/audit') {
      state.queries.push(url.searchParams)
      if (state.fail) return route.fulfill(json(500, { statusCode: 500, statusMessage: 'Error', errorCode: 'INTERNAL' }))
      const action = url.searchParams.get('action')
      const items = state.entries.filter(item => !action || item.action === action)
      const size = Number(url.searchParams.get('pageSize'))
      const current = Number(url.searchParams.get('page'))
      return ok({ totalCount: items.length, page: current, pageSize: size, items: items.slice((current - 1) * size, current * size) })
    }
    return route.fulfill({ status: 404, headers: cors })
  })
  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('ada@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()
  return page.frameLocator('iframe')
}

async function expectNoSidewaysScroll(page: Page, frame: FrameLocator) {
  await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
}

const sample = (): Entry[] => [
  entry(0, { action: 'member.role_changed', actorEmail: 'ada@example.test',
    beforeJson: JSON.stringify({ Email: 'bo@example.test', Role: 'Employee', Status: 'Active' }),
    afterJson: JSON.stringify({ Email: 'bo@example.test', Role: 'TenantAdmin', Status: 'Active' }) }),
  entry(1, { action: 'member.group_changed', actorEmail: null,
    beforeJson: JSON.stringify({ Email: 'cy@example.test', Group: null }), afterJson: JSON.stringify({ Email: 'cy@example.test', Group: 'Contractors' }) }),
  entry(2, { action: 'member.mfa_reset', beforeJson: JSON.stringify({ Email: 'di@example.test', MfaEnabled: true }),
    afterJson: JSON.stringify({ Email: 'di@example.test', MfaEnabled: false }) }),
  entry(3, { action: 'member.invited', beforeJson: null, afterJson: 'not json' })
]

test('the audit log is read when its tab opens and shows labels, actors, times, and readable changes', async ({ page }) => {
  const state: State = { queries: [], fail: false, entries: sample() }
  const frame = await openAuditLog(page, state)
  expect(state.queries).toEqual([])
  await frame.getByRole('tab', { name: 'Audit log' }).click()
  const list = frame.getByRole('list', { name: 'Audit entries' })
  await expect(list.locator(':scope > li')).toHaveCount(4)
  expect(state.queries).toHaveLength(1)
  expect(Object.fromEntries(state.queries[0]!)).toEqual({ page: '1', pageSize: '25' })
  await expect(frame.getByText('4 entries')).toBeVisible()

  const role = list.locator(':scope > li').nth(0)
  await expect(role).toContainText('Member role changed')
  await expect(role).toContainText('by ada@example.test')
  await expect(role).toContainText('20/10/2026 15:00')
  await expect(role).toContainText('Role: Employee → Organization admin')
  await expect(role).toContainText('Email: bo@example.test')
  await expect(role).toContainText('Status: Active')
  await expect(role).not.toContainText('{')

  const group = list.locator(':scope > li').nth(1)
  await expect(group).toContainText('Member moved to another group')
  await expect(group).toContainText('by Unknown account')
  await expect(group).toContainText('Group: None → Contractors')
  await expect(list.locator(':scope > li').nth(2)).toContainText('MFA: On → Off')
  // Unreadable JSON is dropped rather than dumped.
  await expect(list.locator(':scope > li').nth(3)).not.toContainText('not json')
})

test('filters send the action and the whole local days as a UTC range, and Clear starts over', async ({ page }) => {
  const state: State = { queries: [], fail: false, entries: sample() }
  const frame = await openAuditLog(page, state)
  await frame.getByRole('tab', { name: 'Audit log' }).click()
  await expect(frame.getByRole('list', { name: 'Audit entries' }).locator(':scope > li')).toHaveCount(4)
  const filters = frame.getByRole('form', { name: 'Audit filters' })

  await choose(filters.getByLabel('Action'), 'Member role changed')
  await filters.getByLabel('From date').fill(shownDate('2026-10-01'))
  await filters.getByLabel('To date').fill(shownDate('2026-10-31'))
  await filters.getByRole('button', { name: 'Apply filters' }).click()
  await expect(frame.getByRole('list', { name: 'Audit entries' }).locator(':scope > li')).toHaveCount(1)
  expect(Object.fromEntries(state.queries.at(-1)!)).toEqual({
    page: '1', pageSize: '25', action: 'member.role_changed',
    fromUtc: '2026-09-30T17:00:00.000Z', toUtc: '2026-10-31T16:59:59.999Z'
  })

  await filters.getByRole('button', { name: 'Clear filters' }).click()
  await expect(frame.getByRole('list', { name: 'Audit entries' }).locator(':scope > li')).toHaveCount(4)
  expect(Object.fromEntries(state.queries.at(-1)!)).toEqual({ page: '1', pageSize: '25' })
})

test('a reversed date range is refused in the form before any request', async ({ page }) => {
  const state: State = { queries: [], fail: false, entries: sample() }
  const frame = await openAuditLog(page, state)
  await frame.getByRole('tab', { name: 'Audit log' }).click()
  await expect(frame.getByRole('list', { name: 'Audit entries' }).locator(':scope > li')).toHaveCount(4)
  const filters = frame.getByRole('form', { name: 'Audit filters' })
  await filters.getByLabel('From date').fill(shownDate('2026-10-20'))
  await filters.getByLabel('To date').fill(shownDate('2026-10-10'))
  await filters.getByLabel('To date').blur()
  // The date field itself rejects an end before the start, which blocks the form.
  await filters.getByRole('button', { name: 'Apply filters' }).click()
  await expect.poll(() => filters.getByLabel('To date').evaluate((input: HTMLInputElement) => input.validity.valid)).toBe(false)
  expect(state.queries).toHaveLength(1)
})

test('long logs page 25 at a time, newest first, with Previous and Next', async ({ page }) => {
  const state: State = { queries: [], fail: false, entries: Array.from({ length: 30 }, (_, index) => entry(index)) }
  const frame = await openAuditLog(page, state)
  await frame.getByRole('tab', { name: 'Audit log' }).click()
  const list = frame.getByRole('list', { name: 'Audit entries' })
  await expect(list.locator(':scope > li')).toHaveCount(25)
  await expect(list.locator(':scope > li').first()).toContainText('new0@example.test')
  const pager = frame.getByRole('navigation', { name: 'Pagination' })
  await expect(pager).toContainText('1 / 2')
  await expect(pager.getByRole('button', { name: 'Previous' })).toBeDisabled()
  await pager.getByRole('button', { name: 'Next' }).click()
  await expect(list.locator(':scope > li')).toHaveCount(5)
  await expect(pager).toContainText('2 / 2')
  await expect(pager.getByRole('button', { name: 'Next' })).toBeDisabled()
  expect(state.queries.at(-1)!.get('page')).toBe('2')
})

test('an empty log and a failed load each say so, and Try again recovers', async ({ page }) => {
  const state: State = { queries: [], fail: true, entries: [] }
  const frame = await openAuditLog(page, state)
  await frame.getByRole('tab', { name: 'Audit log' }).click()
  const panel = frame.getByRole('region', { name: 'Member activity' })
  await expect(panel.getByRole('alert')).toBeVisible()
  state.fail = false
  await panel.getByRole('button', { name: 'Try again' }).click()
  await expect(panel.getByText('No entries match these filters.')).toBeVisible()
  await expect(panel.getByRole('alert')).toHaveCount(0)
})

test('the audit log speaks Thai and fits 375 and 320 px without sideways scroll', async ({ page }) => {
  const state: State = { queries: [], fail: false, entries: sample() }
  const frame = await openAuditLog(page, state)
  await page.getByRole('radio', { name: 'ไทย' }).click()
  await frame.getByRole('tab', { name: /ประวัติ.*การเปลี่ยนแปลง/ }).click()
  const list = frame.getByRole('list', { name: 'รายการประวัติ' })
  await expect(list.locator(':scope > li')).toHaveCount(4)
  await expect(list.locator(':scope > li').nth(0)).toContainText('เปลี่ยนบทบาทสมาชิก')
  await expect(list.locator(':scope > li').nth(0)).toContainText('บทบาท: พนักงาน → ผู้ดูแลองค์กร')
  await expect(frame.getByRole('form', { name: 'ตัวกรองประวัติ' })).toBeVisible()
  for (const width of [1280, 375, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await expectNoSidewaysScroll(page, frame)
    await expect(frame.getByRole('button', { name: 'ใช้ตัวกรอง' })).toBeVisible()
  }
})
