import { expect, test, type Page } from '@playwright/test'

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const json = (body: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(body) })
const workHours = { startTime: '09:00', endTime: '17:00', breakStart: '12:00', breakEnd: '13:00' }
const entry = (id: string, date: string, startTime: string, endTime: string, durationMinutes: number, taskName: string, projectName: string | null = null) =>
  ({ id, date, startTime, endTime, durationMinutes, taskName, projectName, categoryName: null, includesBreak: false })
const month = (year: number, value: number, extra: Record<string, unknown> = {}) => apiOk({
  ownerId: 'me', year, month: value, locked: false, entries: [], leaves: [], holidays: [],
  workingDays: [1, 2, 3, 4, 5], workHours, ...extra
})

/** Opens the home page already signed in, at a fixed local time. */
async function openSignedIn(page: Page, now: string, role = 'Employee') {
  await page.clock.setFixedTime(new Date(now))
  await page.addInitScript(([tenant, signedInRole]) => {
    sessionStorage.setItem('aiyara.access', 'home-token')
    sessionStorage.setItem('aiyara.refresh', 'home-refresh')
    sessionStorage.setItem('aiyara.expires', '2999-01-01T00:00:00Z')
    sessionStorage.setItem('aiyara.tenant', tenant!)
    sessionStorage.setItem('aiyara.role', signedInRole!)
  }, [tenantId, role])
  await page.route('**/api/v1/identity/profile/me', route => route.fulfill(json(apiOk({
    email: 'orathai@example.test', firstName: 'Orathai', lastName: 'Jaidee', photoUrl: null }))))
  await page.route('**/api/v1/identity/organization', route => route.fulfill(json(apiOk({
    name: 'Acme', logoUrl: null, dateFormat: 'dd/MM/yyyy', timeFormat: 'HH:mm' }))))
  await page.goto('/')
}

test('today shows the logged hours, the day ruler, the week, and the month', async ({ page }) => {
  const requested: string[] = []
  await page.route('**/api/v1/timesheets/me/timesheet**', route => {
    requested.push(new URL(route.request().url()).search)
    expect(route.request().headers().authorization).toBe('Bearer home-token')
    return route.fulfill(json(month(2026, 10, {
      entries: [
        entry('e1', '2026-10-19', '09:00:00', '17:00:00', 420, 'Backlog grooming'),
        entry('e2', '2026-10-21', '09:00:00', '11:30:00', 150, 'Design review', 'Atlas'),
        entry('e3', '2026-10-21', '13:00:00', '16:00:00', 180, 'Sprint planning'),
        entry('e4', '2026-10-07', '09:00:00', '10:00:00', 60, 'Earlier work')
      ],
      leaves: [{ id: 'l1', date: '2026-10-20', kind: 'annual', notes: null }],
      holidays: [{ date: '2026-10-23', name: 'Chulalongkorn Day' }]
    })))
  })
  await openSignedIn(page, '2026-10-21T14:05:00')

  await expect(page.getByRole('heading', { level: 1, name: 'Good afternoon, Orathai' })).toBeVisible()
  await expect(page.getByText('Wednesday 21/10/2026')).toBeVisible()
  await expect(page.getByText('5 h 30 m logged today of 7 h')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Record time' })).toHaveAttribute('href', '/remote/timesheet')
  expect(requested).toEqual(['?year=2026&month=10'])

  const today = page.getByRole('region', { name: 'Logged today' })
  await expect(today.getByRole('listitem')).toHaveText(['09:00–11:30Design reviewAtlas', '13:00–16:00Sprint planning'])
  await expect(page.locator('.ruler-band')).toHaveCount(2)
  await expect(page.locator('.ruler-now')).toContainText('Now')

  const week = page.getByRole('region', { name: 'This week' }).getByRole('listitem')
  await expect(week).toHaveCount(7)
  await expect(week.nth(0)).toContainText('7 h')
  await expect(week.nth(1)).toContainText('annual leave')
  await expect(week.nth(2)).toHaveAttribute('aria-current', 'date')
  await expect(week.nth(3)).toHaveText('Thu 22')
  await expect(week.nth(4)).toContainText('Chulalongkorn Day')
  await expect(week.nth(5)).toContainText('Day off')

  await expect(page.getByRole('region', { name: 'October 2026' })).toContainText('13 h 30 m over 3 days')
  const links = page.getByRole('navigation', { name: 'Also here' })
  await expect(links.getByRole('link', { name: /Reports/ })).toBeVisible()
  await expect(links.getByRole('link', { name: /Timesheet|Profile/ })).toHaveCount(0)
})

test('a week that starts in the previous month reads both months', async ({ page }) => {
  const requested: string[] = []
  await page.route('**/api/v1/timesheets/me/timesheet**', route => {
    const url = new URL(route.request().url())
    requested.push(url.search)
    return url.searchParams.get('month') === '9'
      ? route.fulfill(json(month(2026, 9, { entries: [entry('s1', '2026-09-29', '09:00:00', '12:00:00', 180, 'September work')] })))
      : route.fulfill(json(month(2026, 10)))
  })
  await openSignedIn(page, '2026-10-01T09:30:00')

  await expect(page.getByText('Nothing logged yet today. Your day runs 09:00–17:00.')).toBeVisible()
  expect(requested.sort()).toEqual(['?year=2026&month=10', '?year=2026&month=9'])
  const week = page.getByRole('region', { name: 'This week' }).getByRole('listitem')
  await expect(week.nth(1)).toContainText('3 h')
  await expect(page.getByRole('region', { name: 'October 2026' })).toContainText('Nothing logged this month yet')
})

test('a day that cannot load says so and loads again on Try again', async ({ page }) => {
  // $fetch retries a failed GET once, so the day keeps failing until the test lets it load.
  let failing = true
  await page.route('**/api/v1/timesheets/me/timesheet**', route => failing
    ? route.fulfill(json({ statusCode: 500, statusMessage: 'Internal Server Error' }, 500))
    : route.fulfill(json(month(2026, 10, { holidays: [{ date: '2026-10-23', name: 'Chulalongkorn Day' }] }))))
  await openSignedIn(page, '2026-10-23T10:00:00')

  await expect(page.getByRole('alert')).toContainText('Your day could not be loaded')
  await expect(page.getByRole('link', { name: 'Record time' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Also here' }).getByRole('link', { name: /Reports/ })).toBeVisible()
  failing = false
  await page.getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByText('Today is a holiday: Chulalongkorn Day')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('platform admins get the system admin home without reading a timesheet', async ({ page }) => {
  let timesheetRead = false
  await page.route('**/api/v1/timesheets/**', route => { timesheetRead = true; return route.fulfill(json(month(2026, 10))) })
  await openSignedIn(page, '2026-10-21T14:05:00', 'PlatformAdmin')

  await expect(page.getByRole('heading', { level: 1, name: 'System admin' })).toBeVisible()
  await expect(page.getByRole('main').getByRole('link', { name: 'Open System admin' })).toHaveAttribute('href', '/sys-admin')
  await expect(page.getByRole('link', { name: 'Record time' })).toHaveCount(0)
  expect(timesheetRead).toBe(false)
})

test('signed out, the page explains the product and groups the ways in', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'Your working day, hour by hour.' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Forgot password?' })).toHaveAttribute('href', '/forgot-password')
  const newHere = page.getByRole('navigation', { name: 'New here?' })
  await expect(newHere.getByRole('link')).toHaveText(['Sign up your organization', 'Join with an invitation code', 'Activate an account from your email'])
  await expect(page.getByRole('link', { name: 'System administrator sign in' })).toHaveAttribute('href', '/sys-admin/login')

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 800 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }
})

test('the signed-in home has no sideways scroll on a phone', async ({ page }) => {
  await page.route('**/api/v1/timesheets/me/timesheet**', route => route.fulfill(json(month(2026, 10, {
    entries: [entry('e2', '2026-10-21', '08:00:00', '11:30:00', 210, 'A task with a long name that should be cut short')]
  }))))
  await page.setViewportSize({ width: 320, height: 800 })
  await openSignedIn(page, '2026-10-21T14:05:00')
  await expect(page.getByText('3 h 30 m logged today of 7 h')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320)
})
