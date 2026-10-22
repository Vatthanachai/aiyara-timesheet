import { expect, test, type FrameLocator, type Page } from '@playwright/test'
import { choose } from './fields'

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })

async function waitForHydration(page: Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

type Project = { id: string; name: string; client: string | null; isBillable: boolean; hourlyRate: number | null; currencyCode: string | null; budgetHours: number | null }
const apollo = (): Project => ({ id: 'p-apollo', name: 'Apollo', client: null, isBillable: false, hourlyRate: null, currencyCode: null, budgetHours: null })

/** Today in the organization's zone (Asia/Bangkok), as the usage view reads it. */
const bangkokToday = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
const lastDay = (year: number, month: number) => new Date(Date.UTC(year, month, 0)).getUTCDate()
const monthRange = (year: number, month: number) => ({ from: `${year}-${String(month).padStart(2, '0')}-01`,
  to: `${year}-${String(month).padStart(2, '0')}-${String(lastDay(year, month)).padStart(2, '0')}` })
const thisMonth = () => { const [year, month] = bangkokToday().split('-').map(Number); return monthRange(year!, month!) }
const lastMonth = () => { const [year, month] = bangkokToday().split('-').map(Number); return month === 1 ? monthRange(year! - 1, 12) : monthRange(year!, month! - 1) }

type Refusal = { status: number; error?: Record<string, unknown> }
type Mock = {
  /** The role the account signs in with. */
  role?: 'TenantAdmin' | 'Employee'
  /** Answers PUT projects/{id}/billing with a refusal instead of saving. */
  refuseBilling?: () => Refusal | undefined
  /** Makes GET projects/usage fail. */
  failProjectUsage?: boolean
}

/** Signs in through the Shell with every endpoint the Administration and Reporting pages read mocked. */
async function openApp(page: Page, area: 'Administration' | 'Reports', mock: Mock = {}) {
  const role = mock.role ?? 'TenantAdmin'
  const origin = area === 'Administration' ? 'http://127.0.0.1:3104' : 'http://127.0.0.1:3103'
  const cors = { 'access-control-allow-origin': origin, 'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS' }
  const state = { projects: [apollo()], billingPuts: [] as { id: string; body: Record<string, unknown> }[], usageQueries: [] as string[],
    projectUsageFor: [] as string[] }
  const row = (id: string, name: string, extra: Record<string, unknown>) => ({ projectId: id, name, client: null, isBillable: false, hourlyRate: null,
    currencyCode: null, budgetHours: null, isDeleted: false, periodMinutes: 0, billableMinutes: 0, billableAmount: null, usedMinutes: 0, usedHours: 0,
    remainingHours: null, overBudget: false, ...extra })
  const usageRows = [
    row('p-apollo', 'Apollo', { client: 'Contoso', isBillable: true, hourlyRate: 1200, currencyCode: 'THB', budgetHours: 100, periodMinutes: 450,
      billableMinutes: 450, billableAmount: 9000, usedMinutes: 6600, usedHours: 110, remainingHours: -10, overBudget: true }),
    row('p-gemini', 'Gemini', { client: 'Contoso', isBillable: true, hourlyRate: 50, currencyCode: 'USD', budgetHours: 40, periodMinutes: 120,
      billableMinutes: 120, billableAmount: 100, usedMinutes: 600, usedHours: 10, remainingHours: 30 }),
    row('p-internal', 'Internal', { periodMinutes: 60, usedMinutes: 60, usedHours: 1 }),
    row('p-old', 'Mercury', { isDeleted: true, periodMinutes: 30, usedMinutes: 30, usedHours: .5 })
  ]
  const totals = { periodMinutes: 660, billableMinutes: 570, amounts: [{ currencyCode: 'THB', amount: 9000 }, { currencyCode: 'USD', amount: 100 }] }
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    const ok = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'OK', result }) })
    const refuse = (status: number, error: Record<string, unknown> = {}) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'Refused', ...error }) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (url.pathname.endsWith('/auth/sign-in')) return ok({ session: {
      accessToken: 'billing-token', refreshToken: 'billing-refresh', mustChangePassword: false, tenantId, role,
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    const billing = url.pathname.match(/\/timesheets\/projects\/([^/]+)\/billing$/)
    if (billing && request.method() === 'PUT') {
      const refusal = mock.refuseBilling?.()
      if (refusal) return refuse(refusal.status, refusal.error)
      const body = request.postDataJSON()
      state.billingPuts.push({ id: billing[1]!, body })
      const project = state.projects.find(item => item.id === billing[1])!
      Object.assign(project, body)
      return ok({ ...project, tenantId, isDeleted: false })
    }
    if (url.pathname.endsWith('/timesheets/catalog')) return ok({ projects: state.projects, categories: [], holidays: [], workingDays: [1, 2, 3, 4, 5] })
    if (url.pathname.endsWith('/timesheets/entry-templates')) return ok([])
    if (url.pathname.endsWith('/timesheets/reminders')) return ok({ dailyEnabled: false, monthEndEnabled: false, sendAt: '09:00' })
    if (url.pathname.endsWith(`/identity/tenants/${tenantId}/password-policy`)) return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (url.pathname.endsWith('/identity/members')) return ok([
      { id: 'member-anong', firstName: 'Anong', lastName: 'Admin', email: 'admin@acme.test', role: 'TenantAdmin', status: 'Active', groupId: null, groupName: null },
      { id: 'member-somchai', firstName: 'Somchai', lastName: 'Staff', email: 'somchai@acme.test', role: 'Employee', status: 'Active', groupId: null, groupName: null }])
    if (url.pathname.endsWith('/identity/members/invitations') || url.pathname.endsWith('/identity/members/groups')) return ok([])
    if (url.pathname.endsWith('/identity/organization')) return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    if (url.pathname.endsWith('/identity/profile/me')) return ok({ email: 'admin@acme.test', firstName: 'Anong', lastName: 'Admin', photoUrl: null })
    if (url.pathname.endsWith('/reports/context')) return ok({ isAdmin: role === 'TenantAdmin', timeZoneId: 'Asia/Bangkok' })
    if (url.pathname.endsWith('/reports/definitions') || url.pathname.endsWith('/reports/schedules')) return ok([])
    if (url.pathname.endsWith('/reports/dashboard')) return ok({ total: 0, ready: 0, queued: 0, blocked: 0, failed: 0 })
    if (url.pathname.endsWith('/reports/retention')) return ok({ years: 7 })
    if (url.pathname.endsWith('/reports/runs')) return ok({ totalCount: 0, page: 1, pageSize: 10, items: [] })
    if (url.pathname.endsWith('/timesheets/members/summary')) return ok({ year: 2026, month: 10, locked: false, members: [] })
    if (url.pathname.endsWith('/timesheets/projects/usage')) {
      if (role !== 'TenantAdmin') return refuse(403, { errorCode: 'FORBIDDEN' })
      state.usageQueries.push(`projects ${url.searchParams.toString()}`)
      if (mock.failProjectUsage) return refuse(500)
      return ok({ from: url.searchParams.get('from'), to: url.searchParams.get('to'), unassignedMinutes: 90, projects: usageRows, totals })
    }
    if (url.pathname.endsWith('/timesheets/clients/usage')) {
      state.usageQueries.push(`clients ${url.searchParams.toString()}`)
      return ok({ from: url.searchParams.get('from'), to: url.searchParams.get('to'), unassignedMinutes: 90, totals, clients: [
        { client: 'Contoso', projectCount: 2, periodMinutes: 570, billableMinutes: 570, nonBillableMinutes: 0,
          amounts: [{ currencyCode: 'THB', amount: 9000 }, { currencyCode: 'USD', amount: 100 }] },
        { client: null, projectCount: 2, periodMinutes: 90, billableMinutes: 0, nonBillableMinutes: 90, amounts: [] }] })
    }
    const members = url.pathname.match(/\/timesheets\/projects\/([^/]+)\/usage$/)
    if (members) {
      state.projectUsageFor.push(`${members[1]} ${url.searchParams.toString()}`)
      return ok({ from: url.searchParams.get('from'), to: url.searchParams.get('to'), project: usageRows[0],
        members: [{ ownerId: 'member-anong', minutes: 90 }, { ownerId: 'member-somchai', minutes: 300 }, { ownerId: 'member-gone', minutes: 60 }] })
    }
    return route.fulfill({ status: 404, headers: cors })
  })
  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@acme.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  if (area === 'Administration') await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()
  else await page.getByRole('link', { name: /Reports.*View reports/ }).click()
  return { frame: page.frameLocator('iframe'), state }
}

/** No page or frame scrolls sideways. */
async function expectNoSidewaysScroll(page: Page, frame: FrameLocator) {
  await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
}

test('administration edits a project\'s billing details with inline validation and saves exactly what was typed', async ({ page }) => {
  const { frame, state } = await openApp(page, 'Administration')
  await frame.getByRole('tab', { name: 'Projects and categories' }).click()
  const toggle = frame.getByRole('button', { name: 'Billing and budget: Apollo' })
  await expect(frame.getByText('Not billable')).toBeVisible()
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  // The form opens from the keyboard.
  await toggle.focus()
  await page.keyboard.press('Enter')
  await expect(frame.getByRole('button', { name: 'Hide billing: Apollo' })).toHaveAttribute('aria-expanded', 'true')
  const form = frame.getByRole('form', { name: 'Billing and budget: Apollo' })
  await expect(form).toBeVisible()
  await expect(form).toContainText('current rate')

  // Each rule is judged as it is typed and blocks the save.
  await form.getByLabel('Hourly rate').fill('12.345')
  await expect(form.getByText('The rate must be a number from 0 to 1,000,000 with at most 2 decimals.')).toBeVisible()
  await expect(form.getByLabel('Hourly rate')).toHaveAttribute('aria-invalid', 'true')
  await form.getByLabel('Hourly rate').fill('1200.50')
  await expect(form.getByText('Choose a currency when you set a rate.')).toBeVisible()
  await form.getByLabel('Currency').fill('TH')
  await expect(form.getByText('The currency must be exactly 3 letters, such as THB.')).toBeVisible()
  await form.getByLabel('Budget (hours)').fill('0')
  await expect(form.getByText('The budget must be above 0 and at most 1,000,000 hours, with at most 2 decimals.')).toBeVisible()
  await form.getByLabel('Client').fill('x'.repeat(200))
  await form.getByRole('button', { name: 'Save billing' }).click()
  await expect(form.getByRole('alert')).toHaveText('Could not save. Fix the marked fields and try again.')
  expect(state.billingPuts).toEqual([])

  await form.getByLabel('Client').fill('  Contoso  ')
  await form.getByLabel('Currency').fill('thb')
  await form.getByLabel('Budget (hours)').fill('100')
  await form.getByRole('checkbox', { name: 'This project is billable' }).check()
  await form.getByRole('button', { name: 'Save billing' }).click()
  await expect(frame.getByRole('status').filter({ hasText: 'Saved the billing details of Apollo.' })).toBeVisible()
  expect(state.billingPuts).toEqual([{ id: 'p-apollo', body: { client: 'Contoso', isBillable: true, hourlyRate: 1200.5, currencyCode: 'THB', budgetHours: 100 } }])
  await expect(frame.getByText(/Contoso · Billable · THB.*1,200\.50 per hour · 100 h budget/)).toBeVisible()

  // Clearing the optional fields sends nulls: the route replaces all five.
  await form.getByLabel('Client').fill('')
  await form.getByLabel('Hourly rate').fill('')
  await form.getByLabel('Currency').fill('')
  await form.getByLabel('Budget (hours)').fill('')
  await form.getByRole('checkbox', { name: 'This project is billable' }).uncheck()
  await form.getByRole('button', { name: 'Save billing' }).click()
  await expect.poll(() => state.billingPuts.length).toBe(2)
  expect(state.billingPuts[1]!.body).toEqual({ client: null, isBillable: false, hourlyRate: null, currencyCode: null, budgetHours: null })
})

test('administration words every refusal of a billing save next to the form and keeps what was typed', async ({ page }) => {
  let refusal: Refusal | undefined
  const { frame } = await openApp(page, 'Administration', { refuseBilling: () => refusal })
  await frame.getByRole('tab', { name: 'Projects and categories' }).click()
  await frame.getByRole('button', { name: 'Billing and budget: Apollo' }).click()
  const form = frame.getByRole('form', { name: 'Billing and budget: Apollo' })
  await form.getByLabel('Client').fill('Contoso')
  const save = form.getByRole('button', { name: 'Save billing' })

  refusal = { status: 403, error: { errorCode: 'FORBIDDEN' } }
  await save.click()
  await expect(form.getByRole('alert')).toHaveText('Only organization admins can change billing details.')

  refusal = { status: 400, error: { errorCode: 'VALIDATION_FAILED', errors: { HourlyRate: ['INVALID'], CurrencyCode: ['REQUIRED'] } } }
  await save.click()
  await expect(form.getByRole('alert')).toHaveText('Could not save. Fix the marked fields and try again.')
  await expect(form.getByText('The rate must be a number from 0 to 1,000,000 with at most 2 decimals.')).toBeVisible()
  await expect(form.getByText('Choose a currency when you set a rate.')).toBeVisible()
  await expect(form.getByLabel('Hourly rate')).toHaveAttribute('aria-invalid', 'true')

  refusal = { status: 404, error: { errorCode: 'NOT_FOUND' } }
  await save.click()
  await expect(form.getByRole('alert')).toHaveText('This project no longer exists. The list was reloaded.')

  refusal = { status: 500 }
  await save.click()
  await expect(form.getByRole('alert')).toContainText('Something went wrong')
  await expect(form.getByLabel('Client')).toHaveValue('Contoso')
})

test('administration billing form speaks Thai and never scrolls sideways at 375 and 320 px', async ({ page }) => {
  const { frame } = await openApp(page, 'Administration')
  await frame.getByRole('tab', { name: 'Projects and categories' }).click()
  await frame.getByRole('button', { name: 'Billing and budget: Apollo' }).click()
  await page.getByRole('radio', { name: 'ไทย' }).click()
  const form = frame.getByRole('form', { name: 'การเรียกเก็บเงินและงบประมาณ: Apollo' })
  await expect(form.getByLabel('อัตราต่อชั่วโมง')).toBeVisible()
  await form.getByLabel('อัตราต่อชั่วโมง').fill('-1')
  await expect(form.getByText('อัตราต้องเป็นตัวเลข 0–1,000,000 ทศนิยมไม่เกิน 2 ตำแหน่ง')).toBeVisible()
  await form.getByLabel('อัตราต่อชั่วโมง').fill('100')
  await expect(form.getByText('ต้องระบุสกุลเงินเมื่อกำหนดอัตรา')).toBeVisible()
  await expect(form.getByRole('button', { name: 'บันทึกข้อมูลการเรียกเก็บเงิน' })).toBeVisible()
  for (const width of [1280, 375, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await expectNoSidewaysScroll(page, frame)
    await expect(form.getByRole('button', { name: 'บันทึกข้อมูลการเรียกเก็บเงิน' })).toBeVisible()
  }
})

test('reporting shows usage by project and client for a chosen period with budgets, currencies, and members', async ({ page }) => {
  const { frame, state } = await openApp(page, 'Reports')
  const usage = frame.getByRole('region', { name: 'Projects and clients' })
  await expect(usage).toBeVisible()
  const current = thisMonth()
  await expect.poll(() => state.usageQueries).toEqual(expect.arrayContaining([`projects from=${current.from}&to=${current.to}`, `clients from=${current.from}&to=${current.to}`]))

  const projects = usage.getByRole('region', { name: 'By project' })
  const apolloRow = projects.getByRole('row', { name: /Apollo/ })
  await expect(apolloRow).toContainText('Contoso · Billable')
  await expect(apolloRow).toContainText('7.5')
  await expect(apolloRow).toContainText('THB')
  await expect(apolloRow).toContainText('9,000.00')
  // Over budget reads as text and not only as colour.
  await expect(apolloRow).toContainText('110 of 100 h used')
  await expect(apolloRow).toContainText('Over budget by 10 h')
  await expect(apolloRow.getByRole('img')).toHaveAttribute('aria-label', /over budget, 110 of 100 budget hours used/)
  const geminiRow = projects.getByRole('row', { name: /Gemini/ })
  await expect(geminiRow).toContainText('30 h left')
  await expect(geminiRow.getByRole('img')).toHaveAttribute('aria-label', 'Gemini: 10 of 40 budget hours used (25%)')
  await expect(projects.getByRole('row', { name: /Internal/ })).toContainText('No budget')
  await expect(projects.getByRole('row', { name: /Mercury/ })).toContainText('Deleted')
  // Totals keep each currency apart and never add them.
  const totalRow = projects.getByRole('row', { name: /^Total/ })
  await expect(totalRow).toContainText('9,000.00')
  await expect(totalRow).toContainText('USD')
  await expect(totalRow).toContainText('Hours with no project: 1.5')

  const clients = usage.getByRole('region', { name: 'By client' })
  await expect(clients.getByRole('row', { name: /Contoso/ })).toContainText('2')
  await expect(clients.getByRole('row', { name: /No client/ })).toContainText('1.5')
  await expect(usage.getByText(/current hourly rate.*never added together/)).toBeVisible()

  // The member breakdown opens from the keyboard and names members from the member list.
  const showMembers = projects.getByRole('button', { name: 'Show members: Apollo' })
  await showMembers.focus()
  await page.keyboard.press('Enter')
  await expect(projects.getByRole('button', { name: 'Hide members: Apollo' })).toHaveAttribute('aria-expanded', 'true')
  const memberList = usage.getByRole('list', { name: 'Hours by member on Apollo' })
  await expect(memberList.getByRole('listitem')).toHaveText(['Somchai Staff5', 'Anong Admin1.5', 'Former member1'])
  expect(state.projectUsageFor).toEqual([`p-apollo from=${current.from}&to=${current.to}`])

  // A month preset, a chosen month, a custom range, and all time each ask for exactly that period.
  const period = usage.getByLabel('Period', { exact: true })
  const previous = lastMonth()
  await choose(period, 'Last month')
  await expect.poll(() => state.usageQueries).toContain(`projects from=${previous.from}&to=${previous.to}`)
  await expect(usage.getByRole('button', { name: 'Show members: Apollo' })).toBeVisible()
  await choose(period, 'Custom range')
  await usage.getByRole('textbox', { name: 'From', exact: true }).fill('01/09/2026')
  await usage.getByRole('textbox', { name: 'From', exact: true }).press('Tab')
  await usage.getByRole('textbox', { name: 'To', exact: true }).fill('15/09/2026')
  await usage.getByRole('textbox', { name: 'To', exact: true }).press('Tab')
  await expect.poll(() => state.usageQueries).toContain('clients from=2026-09-01&to=2026-09-15')
  await choose(period, 'All time')
  await expect.poll(() => state.usageQueries.at(-1)).toBe('clients ')
  await expect(usage.getByText('Showing all time')).toBeVisible()
})

test('a failed usage load shows its own message and leaves the rest of reporting working', async ({ page }) => {
  const { frame, state } = await openApp(page, 'Reports', { failProjectUsage: true })
  const usage = frame.getByRole('region', { name: 'Projects and clients' })
  await expect(usage.getByRole('region', { name: 'By project' }).getByRole('alert')).toContainText('Could not load usage by project.')
  // The by-client table and the rest of the page still work.
  await expect(usage.getByRole('region', { name: 'By client' }).getByRole('row', { name: /Contoso/ })).toBeVisible()
  await expect(frame.getByRole('region', { name: 'Documents' })).toBeVisible()
  await expect(frame.getByRole('heading', { name: 'Reports and documents', level: 1 })).toBeVisible()
  expect(state.usageQueries.length).toBeGreaterThan(0)
})

test('usage speaks Thai, fits 375 and 320 px without sideways scroll, and stays out of a member\'s reporting', async ({ page }) => {
  const { frame } = await openApp(page, 'Reports')
  const usage = frame.getByRole('region', { name: 'Projects and clients' })
  await expect(usage.getByRole('region', { name: 'By project' }).getByRole('row', { name: /Apollo/ })).toBeVisible()
  for (const width of [1280, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await expectNoSidewaysScroll(page, frame)
  }
  await page.getByRole('radio', { name: 'ไทย' }).click()
  const thai = frame.getByRole('region', { name: 'โครงการและลูกค้า' })
  await expect(thai.getByRole('region', { name: 'ตามโครงการ' }).getByRole('row', { name: /Apollo/ })).toContainText('เกินงบ 10 ชั่วโมง')
  await expect(thai.getByRole('region', { name: 'ตามลูกค้า' }).getByRole('row', { name: /ไม่มีลูกค้า/ })).toBeVisible()
  await thai.getByRole('button', { name: 'ดูสมาชิก: Apollo' }).click()
  await expect(thai.getByRole('list', { name: 'ชั่วโมงของสมาชิกในโครงการ Apollo' })).toBeVisible()
  await expectNoSidewaysScroll(page, frame)
})

test('a member\'s reporting has no usage view and never asks for usage', async ({ page }) => {
  const { frame, state } = await openApp(page, 'Reports', { role: 'Employee' })
  await expect(frame.getByRole('heading', { name: 'Reports and documents', level: 1 })).toBeVisible()
  await expect(frame.getByRole('region', { name: 'Projects and clients' })).toHaveCount(0)
  expect(state.usageQueries).toEqual([])
})
