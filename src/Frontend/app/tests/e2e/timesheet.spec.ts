import { expect, test, type Page } from '@playwright/test'

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const cors = {
  'access-control-allow-origin': 'http://127.0.0.1:3102',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}
const now = new Date()
const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
const day = (value: number) => `${currentMonth}-${String(value).padStart(2, '0')}`
const entry = (id: string, date: string, startTime: string, endTime: string, durationMinutes: number, taskName: string) =>
  ({ id, date, startTime, endTime, durationMinutes, taskName, detail: null, notes: null, projectId: null, categoryId: null, personalTaskId: null, includesBreak: false })

/** Signs in through the Shell and opens Timesheet with a normal working day of 7 hours (09:00–17:00 less a 1-hour break). */
async function openTimesheet(page: Page, requests: string[] = []) {
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    const ok = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'OK', result }) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (url.pathname.endsWith('/auth/sign-in')) return ok({ organizations: [], session: {
      accessToken: 'card-token', refreshToken: 'card-refresh', accessExpiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: false, tenantId, role: 'Employee' } })
    if (url.pathname.includes('/timesheets/entries')) {
      if (request.method() !== 'GET') {
        requests.push(`${request.method()} ${url.pathname.replace(/.*\/timesheets/, '')} ${request.postData()}`)
        return ok(null)
      }
      return ok([
        entry('e1', day(5), '09:00:00', '17:00:00', 420, 'Full day'),
        entry('e2', day(6), '09:00:00', '12:30:00', 210, 'Half day'),
        entry('e3', day(7), '08:00:00', '17:30:00', 510, 'Long day')
      ])
    }
    if (url.pathname.endsWith('/timesheets/tasks') || url.pathname.endsWith('/timesheets/leave')) return ok([])
    if (url.pathname.endsWith('/timesheets/catalog')) return ok({ projects: [], categories: [], holidays: [],
      workingDays: [0, 1, 2, 3, 4, 5, 6], workHours: { startTime: '09:00', endTime: '17:00', breakStart: '12:00', breakEnd: '13:00' } })
    if (url.pathname.endsWith('/timesheets/context')) return ok({ currentMonth, locked: false })
    return route.fulfill({ status: 404, headers: cors })
  })
  await page.goto('/')
  await page.waitForFunction(() => Boolean((document.querySelector('button') as HTMLElement & { __vnode?: unknown } | null)?.__vnode))
  await page.getByLabel('Email').fill('employee@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: 'Record time' }).click()
  const timesheet = page.frameLocator('iframe')
  await expect(timesheet.locator('.month-totals')).toHaveText('19 h logged over 3 days.')
  return timesheet
}

test('the time card fills each day against the normal working day and marks overtime', async ({ page }) => {
  const timesheet = await openTimesheet(page)
  const cell = (value: number) => timesheet.locator(`.day-button[aria-label="Date ${day(value)}"]`)

  await expect(cell(5).locator('.day-hours')).toHaveText('7 h')
  await expect(cell(5).locator('.day-fill')).toHaveAttribute('style', /--fill: 1\b/)
  await expect(cell(6).locator('.day-hours')).toHaveText('3.5 h')
  await expect(cell(6).locator('.day-fill')).toHaveAttribute('style', /--fill: 0\.5\b/)
  await expect(cell(7)).toHaveClass(/overtime/)
  await expect(cell(7).locator('.day-hours')).toHaveText('8.5 h')
  await expect(cell(6)).not.toHaveClass(/overtime/)
  await expect(cell(8).locator('.day-fill')).toHaveCount(0)

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('the selected day lists its entries, and Edit opens one in the form until another day is chosen', async ({ page }) => {
  const requests: string[] = []
  const timesheet = await openTimesheet(page, requests)
  const form = timesheet.locator('.entry-panel')

  await timesheet.getByRole('button', { name: `Date ${day(6)}` }).click()
  const selected = timesheet.locator('.day-panel')
  await expect(selected.locator('.day-total')).toHaveText('3.5 h')
  await expect(selected.locator('.day-entries li')).toHaveCount(1)
  await expect(selected.locator('.ruler-band')).toHaveCount(1)

  await selected.getByRole('button', { name: 'Edit Half day' }).click()
  await expect(form.getByRole('heading', { name: 'Edit entry' })).toBeVisible()
  await expect(form.getByLabel('Task', { exact: true })).toHaveValue('Half day')
  await expect(form.getByLabel('End', { exact: true })).toHaveValue('12:30')
  await expect(selected.locator('.day-entries li.editing')).toHaveCount(1)

  // Choosing another day leaves the edit instead of moving the entry there.
  await timesheet.getByRole('button', { name: `Date ${day(7)}` }).click()
  await expect(form.getByRole('heading', { name: 'Add entry' })).toBeVisible()
  await expect(form.getByLabel('Task', { exact: true })).toHaveValue('')
  await expect(form.getByLabel('Date')).toHaveValue(day(7))

  // Edit from the month list selects the entry's day and saves with PUT.
  await timesheet.locator('tbody tr').filter({ hasText: 'Full day' }).getByRole('button', { name: 'Edit' }).click()
  await expect(timesheet.locator('.day-button[aria-pressed="true"]')).toHaveAttribute('aria-label', `Date ${day(5)}`)
  await form.getByLabel('End', { exact: true }).fill('16:00')
  await form.getByRole('button', { name: 'Save changes' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^PUT \/entries\/e1 .*"endTime":"16:00:00"/)
})

test('Timesheet takes the theme chosen in the Shell', async ({ page }) => {
  const timesheet = await openTimesheet(page)
  await expect(timesheet.locator('html')).toHaveAttribute('data-theme', 'light')
  await page.getByRole('radiogroup', { name: 'Theme' }).getByRole('radio', { name: 'Dark' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await expect(timesheet.locator('html')).toHaveAttribute('data-theme', 'dark')
})
