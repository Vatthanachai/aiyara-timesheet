import { expect, test, type Page } from '@playwright/test'
import { choose, monthState, options, shownDate } from './fields'

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
/** Leave the mocked API returns: the month's leave, the year's balances, and the organization's leave types. */
type LeaveMock = { leaves: unknown[]; balances: unknown[]; leaveTypes: unknown[] }
/** The rest of the mocked organization and member: the work week, holidays, saved entry templates, and a
 * hook that answers a write (a Timesheet path such as `/entries/copy`) instead of the default success. */
type MockOptions = { workingDays?: number[]; holidays?: unknown[]; templates?: unknown[]
  respond?: (method: string, path: string, body: string | null) => { status?: number; result?: unknown; error?: Record<string, unknown> } | undefined }

async function openTimesheet(page: Page, requests: string[] = [], state: Record<string, unknown> = {},
  leave: LeaveMock = { leaves: [], balances: [], leaveTypes: [] }, options: MockOptions = {}) {
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname.replace(/.*\/timesheets/, '')
    const ok = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'OK', result }) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    const answer = request.method() !== 'GET' && url.pathname.includes('/timesheets/') ? options.respond?.(request.method(), path, request.postData()) : undefined
    if (answer) {
      requests.push(`${request.method()} ${path} ${request.postData()}`)
      const status = answer.status ?? 200
      return answer.error
        ? route.fulfill({ status, headers: cors, contentType: 'application/json', body: JSON.stringify({ statusCode: status, statusMessage: 'Refused', ...answer.error }) })
        : ok(answer.result, status)
    }
    if (url.pathname.endsWith('/auth/sign-in')) return ok({ organizations: [], session: {
      accessToken: 'card-token', refreshToken: 'card-refresh', accessExpiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: false, tenantId, role: 'Employee' } })
    if (url.pathname.includes('/timesheets/entries')) {
      if (request.method() !== 'GET') {
        requests.push(`${request.method()} ${path} ${request.postData()}`)
        return ok(path === '/entries/copy' ? { created: [], skipped: [] } : path === '/entries/bulk' ? { created: [] } : null)
      }
      return ok([
        entry('e1', day(5), '09:00:00', '17:00:00', 420, 'Full day'),
        entry('e2', day(6), '09:00:00', '12:30:00', 210, 'Half day'),
        entry('e3', day(7), '08:00:00', '17:30:00', 510, 'Long day')
      ])
    }
    if (url.pathname.endsWith('/timesheets/tasks')) return ok([])
    if (url.pathname.includes('/timesheets/entry-templates')) {
      if (request.method() === 'GET') return ok(options.templates ?? [])
      requests.push(`${request.method()} ${path} ${request.postData()}`)
      return request.method() === 'DELETE' ? route.fulfill({ status: 204, headers: cors }) : ok({ id: 't-new' }, request.method() === 'POST' ? 201 : 200)
    }
    if (url.pathname.endsWith('/timesheets/catalog')) return ok({ projects: [{ id: 'p1', name: 'Website redesign' }, { id: 'p2', name: 'Mobile app' }, { id: 'p3', name: 'Internal tools' }], categories: [], holidays: options.holidays ?? [], leaveTypes: leave.leaveTypes,
      workingDays: options.workingDays ?? [0, 1, 2, 3, 4, 5, 6], workHours: { startTime: '09:00', endTime: '17:00', breakStart: '12:00', breakEnd: '13:00' } })
    if (url.pathname.endsWith('/submit')) {
      requests.push(`${request.method()} ${url.pathname.replace(/.*\/timesheets/, '')}`)
      Object.assign(state, { status: 'submitted', returnReason: null, editable: false })
      return ok({ status: 'submitted' })
    }
    if (url.pathname.endsWith('/timesheets/me/leave-balance')) return ok({ year: now.getFullYear(), balances: leave.balances })
    if (url.pathname.endsWith('/timesheets/leave')) {
      if (request.method() === 'POST') {
        requests.push(`POST /leave ${request.postData()}`)
        return ok({ id: 'l-new' }, 201)
      }
      return ok(leave.leaves)
    }
    if (/\/timesheets\/me\/months\/\d+\/\d+$/.test(url.pathname)) return ok(monthState(url.pathname, currentMonth, state))
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
  await expect(timesheet.locator('.month-totals')).toHaveText(/^19 h logged over 3 days/)
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
  await expect(form.getByLabel('Date')).toHaveValue(shownDate(day(7)))

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

test('pickers filter as you type, times are 24-hour, and dates are dd/MM/yyyy', async ({ page }) => {
  const timesheet = await openTimesheet(page)
  const form = timesheet.locator('.entry-panel')

  // Typing narrows the list and marks the match; Enter picks the highlighted choice.
  const project = form.getByLabel('Project')
  await expect(project).toHaveAttribute('role', 'combobox')
  await project.click()
  await expect(options(project)).toHaveText(['—', 'Website redesign', 'Mobile app', 'Internal tools'])
  await project.fill('app')
  await expect(options(project)).toHaveText(['Mobile app'])
  await expect(form.locator('[role="option"] mark')).toHaveText('app')
  await project.press('Enter')
  await expect(project).toHaveValue('Mobile app')
  await expect(project).toHaveAttribute('aria-expanded', 'false')

  // Escape puts back the last choice; text that names nothing is not kept.
  await project.fill('nothing like this')
  await expect(form.getByText('No matches')).toBeVisible()
  await project.press('Escape')
  await expect(project).toHaveValue('Mobile app')
  await project.fill('zzz')
  await project.blur()
  await expect(project).toHaveValue('Mobile app')

  // Arrow keys move through the list.
  await project.focus()
  await project.press('ArrowDown')
  await project.press('ArrowDown')
  await project.press('Enter')
  await expect(project).toHaveValue('Internal tools')

  // Times are always 24-hour, suggested every half hour, and typed loosely.
  const start = form.getByLabel('Start', { exact: true })
  await start.click()
  await expect(options(start)).toHaveCount(48)
  await expect(options(start).nth(27)).toHaveText('13:30')
  await start.fill('13')
  await expect(options(start)).toHaveText(['13:00', '13:30'])
  await start.fill('930')
  await start.blur()
  await expect(start).toHaveValue('09:30')
  const end = form.getByLabel('End', { exact: true })
  await end.fill('17.15')
  await end.blur()
  await expect(end).toHaveValue('17:15')
  await expect(form.locator('.duration-pill')).toContainText('6.75')

  // Dates are always dd/MM/yyyy, and a date outside the month is refused.
  const date = form.getByLabel('Date')
  await expect(date).toHaveAttribute('placeholder', 'dd/MM/yyyy')
  await expect(date).toHaveValue(/^\d{2}\/\d{2}\/\d{4}$/)
  await date.fill('1-1-1999')
  await expect.poll(() => date.evaluate(element => (element as HTMLInputElement).validationMessage)).toMatch(/^Choose a date from /)
})

test('a returned month shows the reason, stays editable, and is submitted again', async ({ page }) => {
  const requests: string[] = []
  const timesheet = await openTimesheet(page, requests, { status: 'returned', returnReason: 'Add the client meeting on the 6th.' })
  const bar = timesheet.locator('.approval-bar')

  await expect(bar).toHaveAttribute('data-status', 'returned')
  await expect(bar.locator('.approval-pill')).toHaveText('Returned')
  await expect(bar.locator('.return-reason')).toHaveText('Reason: Add the client meeting on the 6th.')
  await expect(timesheet.locator('.entry-panel')).toBeVisible()

  let question = ''
  page.on('dialog', dialog => { question = dialog.message(); void dialog.accept() })
  await bar.getByRole('button', { name: 'Submit again' }).click()
  await expect.poll(() => requests.at(-1)).toBe(`POST /me/months/${now.getFullYear()}/${now.getMonth() + 1}/submit`)
  expect(question).toMatch(/^Submit .+ for approval\?/)
  await expect(bar).toHaveAttribute('data-status', 'submitted')
  await expect(bar.locator('.approval-pill')).toHaveText('Submitted')
  await expect(bar.getByRole('button')).toHaveCount(0)
  await expect(timesheet.getByRole('status').filter({ hasText: 'Submitted for approval' })).toBeVisible()
  // Waiting for approval, the month is read only.
  await expect(timesheet.locator('.entry-panel')).toHaveCount(0)

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('an approved month cannot be changed or submitted', async ({ page }) => {
  const timesheet = await openTimesheet(page, [], { status: 'approved', editable: false })
  const bar = timesheet.locator('.approval-bar')
  await expect(bar.locator('.approval-pill')).toHaveText('Approved')
  await expect(bar.getByRole('button')).toHaveCount(0)
  await expect(timesheet.locator('.entry-panel')).toHaveCount(0)
  // Nothing in a closed month offers to copy, enter several days, or use templates.
  await expect(timesheet.getByRole('button', { name: 'Copy previous working day' })).toHaveCount(0)
  await expect(timesheet.getByRole('button', { name: 'Copy previous week' })).toHaveCount(0)
  await expect(timesheet.getByRole('region', { name: 'Entry templates' })).toHaveCount(0)
})

test('leave names an organization leave type, can be half a day, and shows the year\'s balance', async ({ page }) => {
  const requests: string[] = []
  const timesheet = await openTimesheet(page, requests, {}, {
    leaveTypes: [{ id: 't-annual', name: 'Annual', annualDays: 10 }, { id: 't-sick', name: 'Sick', annualDays: null }],
    leaves: [{ id: 'l1', date: day(9), kind: 'Annual', notes: null, leaveTypeId: 't-annual', minutes: 210 }],
    balances: [{ leaveTypeId: 't-annual', name: 'Annual', annualDays: 10, usedDays: 10.5, remainingDays: -0.5 },
      { leaveTypeId: 't-sick', name: 'Sick', annualDays: null, usedDays: 2, remainingDays: null }]
  })
  const panel = timesheet.locator('.support-panel')
  await expect(timesheet.locator('.month-totals')).toHaveText('19 h logged over 3 days, 0.5 days of leave.')
  await expect(panel.locator('.leave-amount')).toHaveText('3.5 h')
  const balances = panel.locator('.leave-balances p')
  await expect(balances).toHaveText([/^Annual\s*10.5 of 10 days taken, -0.5 left$/, /^Sick\s*2 days taken$/])
  await expect(balances.first()).toHaveClass(/overdrawn/)

  const form = panel.locator('.leave-form')
  await expect(form.getByLabel('Kind')).toHaveValue('Annual')
  await choose(form.getByLabel('Kind'), 'Sick')
  await choose(form.getByLabel('How long'), 'Half day')
  await form.getByRole('button', { name: 'Add entry' }).click()
  // Half of the 7-hour day (09:00–17:00 less the hour's break).
  await expect.poll(() => requests.at(-1)).toMatch(/^POST \/leave .*"leaveTypeId":"t-sick","minutes":210/)

  await choose(form.getByLabel('How long'), 'Some hours')
  await form.getByLabel('Hours').fill('1.5')
  await form.getByRole('button', { name: 'Add entry' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/"minutes":90/)
})

/** The body of a recorded write, `METHOD /path {json}`. */
const bodyOf = (line: string | undefined) => JSON.parse(line!.slice(line!.indexOf(' {') + 1))
/** The Monday of the week of the 15th, which is in the month along with the whole of its week. */
const mondayOf15 = () => 15 - (new Date(`${day(15)}T00:00:00Z`).getUTCDay() + 6) % 7

test('copying the previous working day or week reports the days it skipped, and a closed day is named', async ({ page }) => {
  const requests: string[] = []
  const monday = mondayOf15()
  let closed = false
  const timesheet = await openTimesheet(page, requests, {}, undefined, { workingDays: [1, 2, 3, 4, 5], respond: (method, path, body) => {
    if (path !== '/entries/copy') return undefined
    if (closed) return { status: 409, error: { errorCode: 'MONTH_CLOSED', errors: { dates: [day(15)] } } }
    return JSON.parse(body!).days === 1
      ? { result: { created: [entry('c1', day(monday), '08:00:00', '17:30:00', 510, 'Long day')], skipped: [] } }
      : { result: { created: [], skipped: [{ date: day(monday), reason: 'holiday' }, { date: day(monday + 1), reason: 'has-entries' }] } }
  } })
  const notice = timesheet.getByRole('status').filter({ hasText: /copied/i })

  // On a Monday the previous working day is the Friday before; the weekend is passed over.
  await timesheet.getByRole('button', { name: `Date ${day(monday)}` }).click()
  const previousDay = timesheet.getByRole('button', { name: 'Copy previous working day' })
  await expect(previousDay).toHaveAttribute('title', `Copy the entries of ${shownDate(day(monday - 3))} onto ${shownDate(day(monday))}`)
  await previousDay.click()
  await expect.poll(() => requests.at(-1)).toBe(`POST /entries/copy {"from":"${day(monday - 3)}","to":"${day(monday)}","days":1}`)
  await expect(notice).toHaveText(/^Copied 1 entry\.$/)

  // The week is the selected day's Monday to Sunday, copied from the week before.
  await timesheet.getByRole('button', { name: `Date ${day(15)}` }).click()
  await timesheet.getByRole('button', { name: 'Copy previous week' }).click()
  await expect.poll(() => requests.at(-1)).toBe(`POST /entries/copy {"from":"${day(monday - 7)}","to":"${day(monday)}","days":7}`)
  await expect(notice.getByText('Nothing was copied.')).toBeVisible()
  await expect(notice.locator('li')).toHaveText([`${shownDate(day(monday))}: Holiday`, `${shownDate(day(monday + 1))}: Already has entries`])

  closed = true
  await timesheet.getByRole('button', { name: 'Copy previous working day' }).click()
  await expect(timesheet.getByRole('alert')).toHaveText(`These days cannot be changed: ${shownDate(day(15))}.`)
})

test('one entry on several days starts from the week\'s working days and sends exactly the days chosen', async ({ page }) => {
  const requests: string[] = []
  const monday = mondayOf15()
  let closed = false
  const timesheet = await openTimesheet(page, requests, {}, undefined, {
    workingDays: [1, 2, 3, 4, 5], holidays: [{ id: 'h1', date: day(monday + 2), name: 'Founders day' }],
    respond: (method, path) => closed && path === '/entries/bulk'
      ? { status: 409, error: { errorCode: 'MONTH_CLOSED', errors: { dates: [day(monday), day(monday + 5)] } } } : undefined
  })
  const form = timesheet.locator('.entry-panel')
  const box = (value: number, note = '') => form.getByRole('checkbox', { name: note ? `${shownDate(day(value))}, ${note}` : shownDate(day(value)), exact: true })

  await timesheet.getByRole('button', { name: `Date ${day(15)}` }).click()
  await form.getByLabel('Task', { exact: true }).fill('Sprint review')
  await form.getByLabel('Several days').check()
  // Monday to Friday of the week, less the holiday; the weekend is offered but not chosen.
  for (const value of [monday, monday + 1, monday + 3, monday + 4]) await expect(box(value)).toBeChecked()
  await expect(box(monday + 2, 'Founders day')).not.toBeChecked()
  await expect(box(monday + 5, 'Day off')).not.toBeChecked()
  await expect(form.getByLabel('Date')).toHaveCount(0)
  await box(monday + 5, 'Day off').check()

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
  await page.setViewportSize({ width: 1280, height: 900 })

  await form.getByRole('button', { name: 'Add to 5 days' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^POST \/entries\/bulk /)
  const body = bodyOf(requests.at(-1))
  expect(body.dates).toEqual([monday, monday + 1, monday + 3, monday + 4, monday + 5].map(day))
  expect(body).toMatchObject({ taskName: 'Sprint review', startTime: '09:00:00', endTime: '17:00:00', projectId: null })
  await expect(timesheet.getByRole('status').filter({ hasText: 'Added to 5 days.' })).toBeVisible()

  // A day the member may not change refuses the whole request and is named.
  closed = true
  await form.getByLabel('Task', { exact: true }).fill('Sprint review')
  await form.getByLabel('Several days').check()
  await box(monday + 5, 'Day off').check()
  await form.getByRole('button', { name: 'Add to 5 days' }).click()
  await expect(timesheet.getByRole('alert')).toHaveText(`These days cannot be changed: ${shownDate(day(monday))}, ${shownDate(day(monday + 5))}.`)
})

test('a template fills the form, the form is saved as a template, and templates are renamed, replaced, and deleted', async ({ page }) => {
  const requests: string[] = []
  const timesheet = await openTimesheet(page, requests, {}, undefined, {
    templates: [{ id: 't1', name: 'Stand-up', taskName: 'Daily stand-up', startTime: '09:30:00', endTime: '09:45:00', projectId: 'p2',
      categoryId: null, detail: 'Team sync', notes: null, includesBreak: false }],
    respond: (method, path, body) => method === 'POST' && path === '/entry-templates' && JSON.parse(body!).name.toLowerCase() === 'stand-up'
      ? { status: 409, error: { errorCode: 'TEMPLATE_EXISTS' } } : undefined
  })
  const form = timesheet.locator('.entry-panel')
  const panel = timesheet.getByRole('region', { name: 'Entry templates' })
  await expect(panel.getByLabel('Template name')).toHaveValue('Stand-up')
  await expect(panel.locator('small')).toHaveText('09:30–09:45 · Daily stand-up')

  await choose(form.getByLabel('Fill from a template'), 'Stand-up')
  await expect(form.getByLabel('Task', { exact: true })).toHaveValue('Daily stand-up')
  await expect(form.getByLabel('Start', { exact: true })).toHaveValue('09:30')
  await expect(form.getByLabel('End', { exact: true })).toHaveValue('09:45')
  await expect(form.getByLabel('Project')).toHaveValue('Mobile app')
  await expect(form.getByLabel('Detail')).toHaveValue('Team sync')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^POST \/entries /)
  expect(bodyOf(requests.at(-1))).toMatchObject({ taskName: 'Daily stand-up', startTime: '09:30:00', endTime: '09:45:00', projectId: 'p2', detail: 'Team sync' })

  // Save as template names the form; a name the member already has is refused.
  await form.getByLabel('Task', { exact: true }).fill('Code review')
  await form.getByLabel('Start', { exact: true }).fill('13:00')
  await form.getByLabel('End', { exact: true }).fill('15:00')
  await form.getByRole('button', { name: 'Save as template' }).click()
  const name = form.locator('.template-save').getByLabel('Template name')
  await name.fill('stand-up')
  await form.getByRole('button', { name: 'Save template' }).click()
  await expect(timesheet.getByRole('alert')).toHaveText('You already have a template called "stand-up".')
  await name.fill('Review')

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
  await page.setViewportSize({ width: 1280, height: 900 })

  await form.getByRole('button', { name: 'Save template' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^POST \/entry-templates /)
  expect(bodyOf(requests.at(-1))).toMatchObject({ name: 'Review', taskName: 'Code review', startTime: '13:00:00', endTime: '15:00:00', projectId: null })
  await expect(timesheet.getByRole('status').filter({ hasText: 'Template saved.' })).toBeVisible()
  await expect(form.locator('.template-save')).toHaveCount(0)

  // Rename in place, then save the name from the row's menu.
  await panel.getByLabel('Template name').fill('Morning stand-up')
  await panel.getByLabel('More for Morning stand-up', { exact: true }).click()
  await panel.getByRole('button', { name: 'Save name' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^PUT \/entry-templates\/t1 /)
  expect(bodyOf(requests.at(-1))).toMatchObject({ name: 'Morning stand-up', taskName: 'Daily stand-up', startTime: '09:30:00', projectId: 'p2' })

  // Replace with the form keeps the name and takes what the form holds.
  await form.getByLabel('Task', { exact: true }).fill('Planning')
  await panel.getByLabel('More for Stand-up', { exact: true }).click()
  await panel.getByRole('button', { name: 'Replace with the form' }).click()
  await expect.poll(() => requests.at(-1)).toMatch(/^PUT \/entry-templates\/t1 /)
  expect(bodyOf(requests.at(-1))).toMatchObject({ name: 'Stand-up', taskName: 'Planning' })

  // Use fills the form from the list too.
  await panel.getByRole('button', { name: 'Use Stand-up' }).click()
  await expect(form.getByLabel('Task', { exact: true })).toHaveValue('Daily stand-up')

  let question = ''
  page.on('dialog', dialog => { question = dialog.message(); void dialog.accept() })
  await panel.getByLabel('More for Stand-up', { exact: true }).click()
  await panel.getByRole('button', { name: 'Delete' }).click()
  await expect.poll(() => requests.at(-1)).toBe('DELETE /entry-templates/t1 null')
  expect(question).toBe('Delete the template "Stand-up"?')
})
