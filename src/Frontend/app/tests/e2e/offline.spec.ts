import { expect, test, type FrameLocator, type Page, type Route } from '@playwright/test'
import { monthState, shownDate } from './fields'

/**
 * Offline Timesheet (Mobile And Offline Plan): entries and the timer recorded without a connection are kept on the
 * device and synced once when it is back, a refusal is shown with Fix and Discard, and sign-out and an account
 * switch delete what the device holds. The API is mocked; the mock follows the server's rule for a create that names
 * its id (a repeat answers 200 with the entry already written).
 */
const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const cors = {
  'access-control-allow-origin': 'http://127.0.0.1:3102',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}
const now = new Date()
const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
const day = (value: number) => `${currentMonth}-${String(value).padStart(2, '0')}`
type Row = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null
  projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak: boolean }
const row = (id: string, date: string, taskName: string): Row =>
  ({ id, date, startTime: '09:00:00', endTime: '11:00:00', durationMinutes: 120, taskName, detail: null, notes: null, projectId: null, categoryId: null, personalTaskId: null, includesBreak: false })

/** The mocked Timesheet API: its entries, what it was sent, whether the network is down, and hooks to refuse a request. */
class Server {
  entries: Row[] = [row('e1', day(5), 'Existing work')]
  requests: string[] = []
  userId = 'user-a'
  down = false
  /** Answers a write with a refusal instead of the default; undefined lets it through. */
  refuse?: (method: string, path: string, body: Record<string, unknown> | null) => { status: number; error?: Record<string, unknown> } | undefined
  /** Fails the first POST /entries after writing it, as if the answer were lost. */
  loseFirstAnswer = false
  sent(method: string, path: string) { return this.requests.filter(request => request.startsWith(`${method} ${path} `)) }
}

async function openTimesheet(page: Page, server: Server): Promise<FrameLocator> {
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname.replace(/.*\/timesheets/, '')
    const reply = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'OK', result }) })
    if (server.down) return route.abort('internetdisconnected')
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (url.pathname.endsWith('/auth/sign-in')) return reply({ organizations: [], session: {
      accessToken: 'card-token', refreshToken: 'card-refresh', accessExpiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: false, tenantId, role: 'Employee' } })
    if (url.pathname.endsWith('/auth/logout')) return route.fulfill({ status: 204, headers: cors })
    if (!url.pathname.includes('/timesheets/')) return route.fulfill({ status: 404, headers: cors })
    return timesheet(route, server, path, reply)
  })
  await page.goto('/')
  await page.waitForFunction(() => Boolean((document.querySelector('button') as HTMLElement & { __vnode?: unknown } | null)?.__vnode))
  await page.getByLabel('Email').fill('employee@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: 'Record time' }).click()
  const frame = page.frameLocator('iframe')
  await expect(frame.locator('.month-totals')).toContainText('logged')
  // Everything the page reads is on screen, and so saved on the device, before the connection is cut.
  await expect(frame.locator('tbody tr').filter({ hasText: 'Existing work' })).toHaveCount(1)
  return frame
}

async function timesheet(route: Route, server: Server, path: string, reply: (result: unknown, status?: number) => Promise<void>) {
  const request = route.request()
  const method = request.method()
  const raw = request.postData()
  const body = raw ? JSON.parse(raw) as Record<string, unknown> : null
  if (method !== 'GET') server.requests.push(`${method} ${path} ${raw}`)
  if (method !== 'GET') {
    const refused = server.refuse?.(method, path, body)
    if (refused) return route.fulfill({ status: refused.status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: refused.status, statusMessage: 'Refused', ...refused.error }) })
  }
  if (path === '/entries' && method === 'POST') {
    const id = String(body!.id ?? crypto.randomUUID())
    const existing = server.entries.find(entry => entry.id === id)
    const entry = existing ?? { ...row(id, String(body!.date), String(body!.taskName)), startTime: String(body!.startTime), endTime: String(body!.endTime) }
    if (!existing) server.entries.push(entry)
    if (server.loseFirstAnswer) { server.loseFirstAnswer = false; return route.abort('connectionreset') }
    return reply(entry, existing ? 200 : 201)
  }
  const one = path.match(/^\/entries\/([^/?]+)$/)
  if (one && method === 'PUT') {
    const entry = server.entries.find(item => item.id === one[1])
    if (!entry) return route.fulfill({ status: 404, headers: cors, contentType: 'application/json', body: JSON.stringify({ statusCode: 404, statusMessage: 'Not found', errorCode: 'NOT_FOUND' }) })
    Object.assign(entry, body)
    return reply(entry)
  }
  if (one && method === 'DELETE') {
    const index = server.entries.findIndex(item => item.id === one[1])
    if (index < 0) return route.fulfill({ status: 404, headers: cors, contentType: 'application/json', body: JSON.stringify({ statusCode: 404, statusMessage: 'Not found', errorCode: 'NOT_FOUND' }) })
    server.entries.splice(index, 1)
    return route.fulfill({ status: 204, headers: cors })
  }
  if (path.startsWith('/entries')) return reply(server.entries)
  if (path === '/tasks' || path === '/entry-templates' || path.startsWith('/leave')) return reply([])
  if (path === '/timer' && method === 'GET') return reply({ timer: null, nowUtc: new Date().toISOString() })
  if (path === '/timer') return route.fulfill({ status: 204, headers: cors })
  if (path === '/catalog') return reply({ projects: [], categories: [], holidays: [], leaveTypes: [], workingDays: [0, 1, 2, 3, 4, 5, 6],
    workHours: { startTime: '09:00', endTime: '17:00', breakStart: '12:00', breakEnd: '13:00' } })
  if (path === '/context') return reply({ currentMonth, locked: false, timeZoneId: 'Asia/Bangkok', isAdmin: false, userId: server.userId })
  if (path === '/me/leave-balance') return reply({ year: now.getFullYear(), balances: [] })
  if (/^\/me\/months\/\d+\/\d+$/.test(path)) return reply(monthState(path, currentMonth))
  return route.fulfill({ status: 404, headers: cors })
}

/** The device loses the connection: the browser reports it and requests fail. */
async function goOffline(page: Page, server: Server) {
  server.down = true
  await page.context().setOffline(true)
}
async function goOnline(page: Page, server: Server) {
  server.down = false
  await page.context().setOffline(false)
}

/** Adds an entry through the form on the given day, the way a member does. */
async function addEntry(frame: FrameLocator, task: string, dayNumber = 8, start?: string, end?: string) {
  await frame.getByRole('button', { name: `Date ${day(dayNumber)}` }).click()
  const form = frame.locator('.entry-panel')
  await form.getByLabel('Task', { exact: true }).fill(task)
  if (start) await form.getByLabel('Start', { exact: true }).fill(start)
  if (end) await form.getByLabel('End', { exact: true }).fill(end)
  await form.getByRole('button', { name: 'Save', exact: true }).click()
}
const status = (frame: FrameLocator) => frame.getByTestId('sync-status')
const rowOf = (frame: FrameLocator, task: string) => frame.locator('tbody tr').filter({ hasText: task })

/** The rows the remote's IndexedDB holds, read from a page on the remote's own origin. */
async function stored(page: Page) {
  const probe = await page.context().newPage()
  await probe.goto('http://127.0.0.1:3102/')
  const counts = await probe.evaluate(() => new Promise<Record<string, number>>(resolve => {
    const request = indexedDB.open('aiyara-timesheet-offline')
    request.onerror = () => resolve({ outbox: 0, cache: 0, kv: 0 })
    request.onsuccess = () => {
      const db = request.result
      if (!db.objectStoreNames.length) { resolve({ outbox: 0, cache: 0, kv: 0 }); return }
      const result: Record<string, number> = {}
      const transaction = db.transaction(['outbox', 'cache', 'kv'])
      for (const name of ['outbox', 'cache', 'kv']) transaction.objectStore(name).count().onsuccess = event => { result[name] = (event.target as IDBRequest<number>).result }
      transaction.oncomplete = () => { db.close(); resolve(result) }
    }
  }))
  await probe.close()
  return counts
}

test('an entry recorded offline is kept on the device, shown as not synced, and sent once with its id when the connection returns', async ({ page }) => {
  const server = new Server()
  const frame = await openTimesheet(page, server)
  await expect(rowOf(frame, 'Existing work')).toHaveCount(1)

  await goOffline(page, server)
  await expect(frame.getByText('You are offline. Changes are saved on this device and sync when you reconnect.')).toBeVisible()
  await addEntry(frame, 'Site visit')
  await expect(frame.locator('.feedback.success')).toContainText('Saved on this device')
  await expect(status(frame)).toContainText('1 change waiting to sync')
  await expect(rowOf(frame, 'Site visit')).toContainText('Not synced')
  // The estimate uses the organization's break: 09:00-17:00 less 12:00-13:00.
  await expect(rowOf(frame, 'Site visit')).toContainText('7.00')
  expect(server.sent('POST', '/entries')).toHaveLength(0)

  await goOnline(page, server)
  await expect(status(frame)).toHaveCount(0)
  await expect(rowOf(frame, 'Site visit')).not.toContainText('Not synced')
  const posts = server.sent('POST', '/entries')
  expect(posts).toHaveLength(1)
  const sent = JSON.parse(posts[0]!.replace(/^POST \/entries /, ''))
  expect(sent.id).toMatch(/^[0-9a-f-]{36}$/)
  expect(sent).toMatchObject({ taskName: 'Site visit', date: day(8), startTime: '09:00:00', endTime: '17:00:00' })
  expect(server.entries.map(entry => entry.id)).toContain(sent.id)
})

test('a create whose answer was lost is replayed with the same id and written once', async ({ page }) => {
  const server = new Server()
  const frame = await openTimesheet(page, server)
  server.loseFirstAnswer = true
  await addEntry(frame, 'Lost answer')
  // The request got no answer, so the change waits and is sent again; the second call finds the entry already there.
  await expect.poll(() => server.sent('POST', '/entries').length).toBe(2)
  const ids = server.sent('POST', '/entries').map(request => JSON.parse(request.replace(/^POST \/entries /, '')).id)
  expect(ids[0]).toBe(ids[1])
  expect(server.entries.filter(entry => entry.taskName === 'Lost answer')).toHaveLength(1)
  await expect(status(frame)).toHaveCount(0)
  await expect(rowOf(frame, 'Lost answer')).toHaveCount(1)
})

test('editing and deleting changes that never reached the server fold into one item, or none', async ({ page }) => {
  const server = new Server()
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)

  await addEntry(frame, 'Draft one', 9)
  await addEntry(frame, 'Draft two', 10)
  await expect(status(frame)).toContainText('2 changes waiting to sync')
  // Editing a not-yet-synced entry rewrites its queued create.
  await rowOf(frame, 'Draft one').getByRole('button', { name: 'Edit' }).click()
  await frame.locator('.entry-panel').getByLabel('Task', { exact: true }).fill('Draft one, renamed')
  await frame.locator('.entry-panel').getByRole('button', { name: 'Save changes' }).click()
  await expect(status(frame)).toContainText('2 changes waiting to sync')
  await expect(rowOf(frame, 'Draft one, renamed')).toHaveCount(1)
  // Deleting one removes it: the server never hears of it.
  page.once('dialog', dialog => void dialog.accept())
  await rowOf(frame, 'Draft two').getByRole('button', { name: 'Delete' }).click()
  await expect(status(frame)).toContainText('1 change waiting to sync')
  await expect(rowOf(frame, 'Draft two')).toHaveCount(0)
  // Deleting an entry the server has is a queued delete, and the entry disappears at once.
  page.once('dialog', dialog => void dialog.accept())
  await rowOf(frame, 'Existing work').getByRole('button', { name: 'Delete' }).click()
  await expect(status(frame)).toContainText('2 changes waiting to sync')
  await expect(rowOf(frame, 'Existing work')).toHaveCount(0)

  await goOnline(page, server)
  await expect(status(frame)).toHaveCount(0)
  expect(server.sent('POST', '/entries')).toHaveLength(1)
  expect(server.sent('POST', '/entries')[0]).toContain('Draft one, renamed')
  expect(server.sent('DELETE', '/entries/e1')).toHaveLength(1)
  expect(server.entries.map(entry => entry.taskName)).toEqual(['Draft one, renamed'])
})

test('a change the server refuses is shown with its reason and can be fixed or discarded', async ({ page }) => {
  const server = new Server()
  server.refuse = (method, path, body) => method === 'POST' && path === '/entries' && body?.taskName !== 'Fixed work' && body?.taskName !== 'Fine work'
    ? { status: 409, error: { errorCode: 'MONTH_CLOSED', errors: { dates: [day(8)] } } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'Too late', 8)
  await addEntry(frame, 'Fine work', 9)
  await addEntry(frame, 'Discard me', 10)
  await goOnline(page, server)

  // The accepted item is gone; the two refused ones stay, marked, with the reason in words.
  await expect(status(frame)).toContainText('2 changes need your attention')
  await expect(status(frame).getByRole('alert')).toBeVisible()
  await expect(status(frame).locator('li').filter({ hasText: 'Too late' })).toContainText('These days cannot be changed')
  await expect(rowOf(frame, 'Too late')).toContainText('Not accepted')
  await expect(rowOf(frame, 'Fine work')).not.toContainText('Not')
  expect(server.entries.map(entry => entry.taskName)).toEqual(['Existing work', 'Fine work'])

  // Discard drops the item and the entry with it.
  page.once('dialog', dialog => void dialog.accept())
  await status(frame).locator('li').filter({ hasText: 'Discard me' }).getByRole('button', { name: 'Discard' }).click()
  await expect(rowOf(frame, 'Discard me')).toHaveCount(0)
  await expect(status(frame)).toContainText('1 change needs your attention')

  // Fix opens the entry in the form; saving it sends it again, now accepted.
  await status(frame).locator('li').filter({ hasText: 'Too late' }).getByRole('button', { name: 'Fix' }).click()
  const form = frame.locator('.entry-panel')
  await expect(form.getByLabel('Task', { exact: true })).toHaveValue('Too late')
  await form.getByLabel('Task', { exact: true }).fill('Fixed work')
  await form.getByRole('button', { name: 'Save changes' }).click()
  await expect(status(frame)).toHaveCount(0)
  expect(server.entries.map(entry => entry.taskName)).toEqual(['Existing work', 'Fine work', 'Fixed work'])
  // The fix was one create: the refused one was rewritten, not duplicated.
  expect(server.entries.filter(entry => entry.taskName === 'Too late')).toHaveLength(0)
})

test('trouble at the server leaves the change waiting, and Sync now tries again', async ({ page }) => {
  const server = new Server()
  let busy = true
  server.refuse = (method, path) => method === 'POST' && path === '/entries' && busy ? { status: 503, error: { errorCode: 'UNAVAILABLE' } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'Waits for the server')
  await goOnline(page, server)
  await expect(status(frame)).toContainText('1 change waiting to sync')
  await expect(status(frame)).toContainText('Trying again soon')
  await expect(status(frame)).not.toContainText('need your attention')
  busy = false
  await status(frame).getByRole('button', { name: 'Sync now' }).click()
  await expect(status(frame)).toHaveCount(0)
  expect(server.entries.map(entry => entry.taskName)).toContain('Waits for the server')
})

test('the timer starts and stops offline, and stopping queues an entry that syncs with its id', async ({ page }) => {
  const server = new Server()
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  const panel = frame.getByRole('region', { name: 'Timer' })
  await panel.getByLabel('Task').fill('Offline call')
  await panel.getByRole('button', { name: 'Start timer' }).click()
  await expect(panel.getByRole('status')).toHaveText('Timer started')
  await expect(panel.locator('.timer-elapsed')).toHaveText(/^0:00:\d\d$/)
  // A second device would see nothing, and nothing reached the server.
  expect(server.sent('POST', '/timer/start')).toHaveLength(0)

  // Stopping needs a minute to have passed or times chosen by hand.
  await panel.getByRole('button', { name: 'Choose date and times' }).click()
  await panel.getByLabel('Date').fill(shownDate(day(11)))
  await panel.getByLabel('Start', { exact: true }).fill('09:00')
  await panel.getByLabel('End', { exact: true }).fill('10:30')
  await panel.getByRole('button', { name: 'Stop with these times' }).click()
  await expect(panel.getByRole('status')).toHaveText('Timer stopped and the time entry saved')
  await expect(status(frame)).toContainText('1 change waiting to sync')
  await expect(rowOf(frame, 'Offline call')).toContainText('Not synced')

  await goOnline(page, server)
  await expect(status(frame)).toHaveCount(0)
  const posts = server.sent('POST', '/entries')
  expect(posts).toHaveLength(1)
  expect(JSON.parse(posts[0]!.replace(/^POST \/entries /, ''))).toMatchObject({ taskName: 'Offline call', date: day(11), startTime: '09:00:00', endTime: '10:30:00' })
  expect(server.sent('POST', '/timer/stop')).toHaveLength(0)
})

test('online-only actions say they need a connection, and other pages explain it', async ({ page }) => {
  const server = new Server()
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await frame.getByRole('button', { name: 'Copy previous working day' }).click()
  await expect(frame.getByRole('alert')).toHaveText('You are offline. This needs a connection.')
  await expect(page.getByText('You are offline. Record time still works; the rest needs a connection.')).toBeVisible()
  await page.locator('a[href="/remote/reporting"]').click()
  await expect(page.getByTestId('needs-connection')).toContainText('This needs a connection')
  await goOnline(page, server)
})

test('sign-out deletes what the device holds, after asking when changes are waiting', async ({ page }) => {
  const server = new Server()
  server.refuse = (method, path) => method === 'POST' && path === '/entries' ? { status: 503, error: { errorCode: 'UNAVAILABLE' } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'Unsynced at sign-out')
  await goOnline(page, server)
  await expect(status(frame)).toContainText('1 change waiting to sync')
  expect(await stored(page)).toMatchObject({ outbox: 1 })

  const dialogs: string[] = []
  page.on('dialog', dialog => { dialogs.push(dialog.message()); void dialog.accept() })
  await page.locator('.profile-menu summary').click()
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/signed-out/)
  expect(dialogs).toEqual(['Changes waiting to sync (1) will be lost if you sign out. Sign out anyway?'])
  await expect.poll(() => stored(page)).toEqual({ outbox: 0, cache: 0, kv: 0 })
})

test('another account on the same device never sees or sends the first one\'s changes', async ({ page }) => {
  const server = new Server()
  server.refuse = (method, path) => method === 'POST' && path === '/entries' ? { status: 503, error: { errorCode: 'UNAVAILABLE' } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'First account only')
  await goOnline(page, server)
  // Wait until the first sync was refused, so it cannot reach the server once the refusal is lifted.
  await expect(status(frame)).toContainText('Trying again soon')

  // The device now belongs to a different account: the API names another user for the same session.
  server.userId = 'user-b'
  server.refuse = undefined
  await page.reload()
  const next = page.frameLocator('iframe')
  await expect(next.locator('.month-totals')).toContainText('logged')
  await expect(next.getByTestId('sync-status')).toHaveCount(0)
  await expect(next.getByText('First account only')).toHaveCount(0)
  expect(server.entries.map(entry => entry.taskName)).not.toContain('First account only')
  expect(await stored(page)).toMatchObject({ outbox: 0 })
})

test('the entry screens fit at 375 and 320 px while offline with waiting and refused changes', async ({ page }) => {
  const server = new Server()
  server.refuse = (method, path, body) => method === 'POST' && path === '/entries' && body?.taskName === 'Refused entry with a rather long task name that must wrap'
    ? { status: 409, error: { errorCode: 'MONTH_CLOSED', errors: { dates: [day(8)] } } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'Waiting entry', 8)
  await addEntry(frame, 'Refused entry with a rather long task name that must wrap', 9)
  await goOnline(page, server)
  await expect(status(frame)).toContainText('needs your attention')
  await goOffline(page, server)
  await expect(frame.getByText('You are offline.')).toBeVisible()
  await addEntry(frame, 'Waiting again', 10)
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  }
  await goOnline(page, server)
})

test('the purge address alone deletes nothing: opened top-level or framed by a foreign page it is ignored', async ({ page }) => {
  const server = new Server()
  server.refuse = (method, path) => method === 'POST' && path === '/entries' ? { status: 503, error: { errorCode: 'UNAVAILABLE' } } : undefined
  const frame = await openTimesheet(page, server)
  await goOffline(page, server)
  await addEntry(frame, 'Must survive')
  await goOnline(page, server)
  await expect(status(frame)).toContainText('Trying again soon')
  expect(await stored(page)).toMatchObject({ outbox: 1 })

  // Opened by itself (a link anyone can send).
  const top = await page.context().newPage()
  await top.goto('http://127.0.0.1:3102/?aiyara-purge=1')
  await top.waitForTimeout(1000)
  await top.close()
  expect(await stored(page)).toMatchObject({ outbox: 1 })

  // Framed by another site, which also answers the page's question with its own message.
  const foreign = await page.context().newPage()
  await foreign.route('http://foreign.test/**', route => route.fulfill({ contentType: 'text/html',
    body: `<iframe id="f" src="http://127.0.0.1:3102/?aiyara-purge=1"></iframe><script>
      addEventListener('message', event => { if (event.data && event.data.type === 'aiyara.purge-ready') document.getElementById('f').contentWindow.postMessage({ type: 'aiyara.purge' }, '*') })</script>` }))
  await foreign.goto('http://foreign.test/')
  await foreign.waitForTimeout(1500)
  await foreign.close()
  expect(await stored(page)).toMatchObject({ outbox: 1 })
})

test('the Timesheet remote may be framed only by the Shell', async ({ request }) => {
  const response = await request.get('http://127.0.0.1:3102/')
  expect(response.headers()['content-security-policy']).toBe("frame-ancestors 'self' http://127.0.0.1:3100")
})
