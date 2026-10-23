import { expect, test, type FrameLocator, type Page } from '@playwright/test'
import { parseInviteRows } from '../../../remotes/administration/app/utils/bulkInvite'

// "Invite several" in the Administration remote, with mocked APIs: paste and CSV parsing, the preview
// and its checks, group names mapped to ids, mixed results, retry of skipped rows, errors, Thai, and
// narrow screens.

const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const cors = {
  'access-control-allow-origin': 'http://127.0.0.1:3104',
  'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
}
const json = (status: number, body: unknown) => ({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(body) })
const groups = [{ id: 'g1', name: 'Sales', memberCount: 2 }, { id: 'g2', name: 'Ops Team', memberCount: 0 }]

type BulkRow = { email: string; role: string; groupId?: string }
type Outcome = { status: 'Created' | 'Skipped'; reason?: string; emailSent?: boolean }
type State = {
  bodies: { rows: BulkRow[] }[]
  /** Answers one bulk request; the default creates every row. */
  respond: (rows: BulkRow[], call: number) => ReturnType<typeof json>
  invitationReads: number
}
const created = (rows: BulkRow[], outcomes: Record<string, Outcome> = {}) => {
  const results = rows.map((row, index) => {
    const outcome = outcomes[row.email] ?? { status: 'Created' }
    return { index, email: row.email.toLowerCase(), status: outcome.status, reason: outcome.reason ?? null,
      invitationId: outcome.status === 'Created' ? `inv-${index}` : null, emailSent: outcome.status === 'Created' && outcome.emailSent !== false }
  })
  return json(200, apiOk({ createdCount: results.filter(r => r.status === 'Created').length,
    skippedCount: results.filter(r => r.status === 'Skipped').length, rows: results }))
}
const newState = (respond?: State['respond']): State => ({ bodies: [], invitationReads: 0, respond: respond ?? (rows => created(rows)) })

async function waitForHydration(page: Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

async function openPanel(page: Page, state: State, language: 'en' | 'th' = 'en'): Promise<FrameLocator> {
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
    if (path === '/identity/members/groups') return ok(groups)
    if (path === '/identity/members/invitations') { state.invitationReads++; return ok([]) }
    if (path === '/identity/organization') return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    if (path === '/identity/members/invitations/bulk') {
      const body = request.postDataJSON()
      state.bodies.push(body)
      return route.fulfill(state.respond(body.rows, state.bodies.length))
    }
    return route.fulfill({ status: 404, headers: cors })
  })
  await page.goto('/')
  await waitForHydration(page)
  if (language === 'th') await page.getByRole('radio', { name: 'ไทย' }).click()
  await page.getByLabel(language === 'th' ? 'อีเมล' : 'Email').fill('ada@example.test')
  await page.getByLabel(language === 'th' ? 'รหัสผ่าน' : 'Password').fill('test-password')
  await page.getByRole('button', { name: language === 'th' ? 'เข้าสู่ระบบ' : 'Sign in' }).click()
  await page.getByRole('navigation').getByRole('link', { name: language === 'th' ? 'ตั้งค่า' : 'Administration' }).first().click()
  const frame = page.frameLocator('iframe')
  await frame.getByRole('tab', { name: language === 'th' ? /^สมาชิก/ : /^People/ }).click()
  await frame.getByRole('button', { name: language === 'th' ? 'เชิญหลายคน' : 'Invite several' }).click()
  return frame
}

async function expectNoSidewaysScroll(page: Page, frame: FrameLocator) {
  await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
}

test('the parser reads delimiters, quotes, a BOM, optional headers, role defaults, and group names', () => {
  const rows = (text: string) => parseInviteRows(text, groups).map(({ email, role, groupId, problem }) => ({ email, role, groupId, problem }))

  // A plain list: every row is an Employee with no group.
  expect(rows('a@x.test\nb@x.test')).toEqual([
    { email: 'a@x.test', role: 'Employee', groupId: null, problem: null },
    { email: 'b@x.test', role: 'Employee', groupId: null, problem: null }])
  // A BOM, a header in any order, semicolons, CRLF, quoted fields with the delimiter, and extra columns.
  expect(rows('\uFEFFgroup;Email;note;role\r\n"Ops Team";c@x.test;hi;admin\r\nsales;d@x.test;;')).toEqual([
    { email: 'c@x.test', role: 'TenantAdmin', groupId: 'g2', problem: null },
    { email: 'd@x.test', role: 'Employee', groupId: 'g1', problem: null }])
  // Tabs, as pasted from a spreadsheet; a doubled quote and a line break inside quotes stay in the cell.
  expect(rows('e@x.test\ttenantadmin\tSALES')).toEqual([{ email: 'e@x.test', role: 'TenantAdmin', groupId: 'g1', problem: null }])
  expect(parseInviteRows('"f@x.test","Employee","Say ""hi""\nthere"', groups)[0]!.groupText).toBe('Say "hi"\nthere')
  // Blank lines are skipped, and the problems are named: invalid email, duplicate (ignoring case), role, group.
  expect(rows('\n\nbad\nG@x.test\ng@X.test\nh@x.test,boss\ni@x.test,,Nowhere\n')).toEqual([
    { email: 'bad', role: 'Employee', groupId: null, problem: 'invalidEmail' },
    { email: 'G@x.test', role: 'Employee', groupId: null, problem: null },
    { email: 'g@X.test', role: 'Employee', groupId: null, problem: 'duplicate' },
    { email: 'h@x.test', role: null, groupId: null, problem: 'invalidRole' },
    { email: 'i@x.test', role: 'Employee', groupId: null, problem: 'unknownGroup' }])
  expect(parseInviteRows('   \n', groups)).toEqual([])
})

test('pasted text is previewed with its checks, group names become ids, and mixed results are explained', async ({ page }) => {
  const state = newState((rows, call) => call === 1 ? created(rows, {
    'new@example.test': { status: 'Created' },
    'noemail@example.test': { status: 'Created', emailSent: false },
    'member@example.test': { status: 'Skipped', reason: 'AlreadyMember' },
    'invited@example.test': { status: 'Skipped', reason: 'AlreadyInvited' }
  }) : created(rows))
  const frame = await openPanel(page, state)
  const input = frame.getByLabel('Paste emails or CSV data')
  const send = frame.getByRole('button', { name: /^Send \d+ invitations$/ })
  await expect(send).toBeDisabled()

  await input.fill([
    'email,role,group', 'new@example.test,Employee,sales', 'noemail@example.test,admin,"Ops Team"',
    'member@example.test', 'invited@example.test,,'
  ].join('\n'))
  const preview = frame.getByRole('table', { name: 'Preview before sending' })
  await expect(preview.locator('tbody tr')).toHaveCount(4)
  await expect(frame.getByText('4 rows')).toBeVisible()
  await expect(preview.locator('tbody tr').nth(0)).toContainText('new@example.test')
  await expect(preview.locator('tbody tr').nth(0)).toContainText('Employee')
  await expect(preview.locator('tbody tr').nth(0)).toContainText('sales')
  await expect(preview.locator('tbody tr').nth(1)).toContainText('Organization admin')
  await expect(preview.locator('tbody tr').nth(1)).toContainText('Ops Team')
  await expect(preview.locator('tbody tr').nth(2)).toContainText('No group')
  await expect(frame.getByText('rows have problems')).toHaveCount(0)
  await expect(send).toHaveText('Send 4 invitations')

  await send.click()
  expect(state.bodies).toEqual([{ rows: [
    { email: 'new@example.test', role: 'Employee', groupId: 'g1' },
    { email: 'noemail@example.test', role: 'TenantAdmin', groupId: 'g2' },
    { email: 'member@example.test', role: 'Employee' },
    { email: 'invited@example.test', role: 'Employee' }] }])

  // Counts, one line per row with plain-word reasons, and a pointer for the invitation whose email did not go.
  const results = frame.getByRole('table', { name: 'Invitation results' })
  await expect(frame.getByRole('heading', { name: 'Invitation results' })).toBeFocused()
  await expect(frame.getByText('2 invited')).toBeVisible()
  await expect(frame.getByText('2 skipped')).toBeVisible()
  await expect(results.locator('tbody tr')).toHaveCount(4)
  await expect(results.locator('tbody tr').nth(0)).toContainText('Invited')
  await expect(results.locator('tbody tr').nth(1)).toContainText('did not go out')
  await expect(results.locator('tbody tr').nth(1)).toContainText('pending invitations list')
  await expect(results.locator('tbody tr').nth(2)).toContainText('Already a member of this organization')
  await expect(results.locator('tbody tr').nth(3)).toContainText('An invitation is already pending')
  await expect(frame.locator('body')).not.toContainText('inv-')
  // The page reloaded the pending invitations after creating some.
  expect(state.invitationReads).toBeGreaterThanOrEqual(2)

  // Skipped rows come back to the input, as CSV without the reason, to fix and retry.
  await frame.getByRole('button', { name: 'Fix skipped rows and retry' }).click()
  await expect(input).toHaveValue('email,role,group\nmember@example.test,,\ninvited@example.test,,\n')
  await expect(preview.locator('tbody tr')).toHaveCount(2)
  await send.click()
  await expect(frame.getByText('2 invited')).toBeVisible()
  await expect(frame.getByRole('button', { name: 'Fix skipped rows and retry' })).toHaveCount(0)
  expect(state.bodies).toHaveLength(2)
})

test('skipped rows can be copied and downloaded as CSV with their reasons', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: 'http://127.0.0.1:3104' })
  const state = newState(rows => created(rows, { 'dup@example.test': { status: 'Skipped', reason: 'AlreadyMember' },
    '=cmd@example.test': { status: 'Skipped', reason: 'InvalidEmail' } }))
  const frame = await openPanel(page, state)
  await frame.getByLabel('Paste emails or CSV data').fill('ok@example.test\ndup@example.test,TenantAdmin,Sales\n=cmd@example.test')
  // A leading = is a valid address character; in the CSV it is neutralized so a spreadsheet never runs it.
  await frame.getByRole('button', { name: 'Send 3 invitations' }).click()
  await expect(frame.getByText('2 skipped')).toBeVisible()

  const download = page.waitForEvent('download')
  await frame.getByRole('button', { name: 'Download skipped rows (CSV)' }).click()
  const file = await download
  expect(file.suggestedFilename()).toBe('skipped-invitations.csv')
  const content = await (await import('node:fs/promises')).readFile(await file.path() as string, 'utf8')
  expect(content).toBe('\uFEFFemail,role,group,reason\r\ndup@example.test,TenantAdmin,Sales,AlreadyMember\r\n\'=cmd@example.test,,,InvalidEmail\r\n')

  await frame.getByRole('button', { name: 'Copy skipped rows (CSV)' }).click()
  await expect(frame.getByText('Copied.')).toBeVisible()
})

test('a CSV file with a BOM, semicolons, and a header is read into the preview', async ({ page }) => {
  const state = newState()
  const frame = await openPanel(page, state)
  const csv = '\uFEFFEmail;Role;Group\r\nuno@example.test;TenantAdmin;Sales\r\ndos@example.test;;"Ops Team"\r\n'
  await frame.locator('.bulk input[type="file"]').setInputFiles({ name: 'people.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf8') })
  await expect(frame.getByText('people.csv')).toBeVisible()
  const preview = frame.getByRole('table', { name: 'Preview before sending' })
  await expect(preview.locator('tbody tr')).toHaveCount(2)
  await expect(preview.locator('tbody tr').nth(0)).toContainText('Organization admin')
  await expect(preview.locator('tbody tr').nth(1)).toContainText('Ops Team')
  await frame.getByRole('button', { name: 'Send 2 invitations' }).click()
  await expect(frame.getByText('2 invited')).toBeVisible()
  expect(state.bodies[0]!.rows).toEqual([{ email: 'uno@example.test', role: 'TenantAdmin', groupId: 'g1' }, { email: 'dos@example.test', role: 'Employee', groupId: 'g2' }])

  // A file over 1 MB is refused without being read.
  await frame.getByRole('button', { name: 'Invite more' }).click()
  await frame.locator('.bulk input[type="file"]').setInputFiles({ name: 'big.csv', mimeType: 'text/csv', buffer: Buffer.alloc(1024 * 1024 + 1, 'a') })
  await expect(frame.getByRole('alert')).toContainText('The file is too large')
})

test('rows with problems and batches over 100 rows are blocked with a clear message', async ({ page }) => {
  const state = newState()
  const frame = await openPanel(page, state)
  const input = frame.getByLabel('Paste emails or CSV data')
  const send = frame.getByRole('button', { name: /^Send \d+ invitations$/ })
  await input.fill('good@example.test\nnot-an-email\nGOOD@example.test\nrole@example.test,boss\ngroup@example.test,,Nowhere')
  const rows = frame.getByRole('table', { name: 'Preview before sending' }).locator('tbody tr')
  await expect(frame.getByText('4 rows have problems')).toBeVisible()
  await expect(rows.nth(1)).toContainText('Not a valid email')
  await expect(rows.nth(2)).toContainText('Repeated in this list')
  await expect(rows.nth(3)).toContainText('Unknown role')
  await expect(rows.nth(4)).toContainText('No group with this name')
  await expect(frame.getByText('Fix or remove the rows with problems before sending.')).toBeVisible()
  await expect(send).toBeDisabled()
  await frame.getByRole('button', { name: 'Remove rows with problems' }).click()
  await expect(rows).toHaveCount(1)
  await expect(send).toBeEnabled()

  await input.fill(Array.from({ length: 100 }, (_, i) => `p${i}@example.test`).join('\n'))
  await expect(send).toBeEnabled()
  await input.fill(Array.from({ length: 101 }, (_, i) => `p${i}@example.test`).join('\n'))
  await expect(frame.getByRole('alert')).toContainText('There are 101 rows, over the limit of 100 per batch')
  await expect(send).toBeDisabled()
  expect(state.bodies).toEqual([])
})

test('a rate limit and a server error keep the rows, and Send again goes through', async ({ page }) => {
  const state = newState((rows, call) => call === 1
    ? json(429, { statusCode: 429, statusMessage: 'Error', errorCode: 'RATELIMITED' })
    : call === 2 ? json(400, { statusCode: 400, statusMessage: 'Error', errorCode: 'VALIDATION_FAILED' })
    : call === 3 ? json(500, { statusCode: 500, statusMessage: 'Error', errorCode: 'INTERNAL' }) : created(rows))
  const frame = await openPanel(page, state)
  await frame.getByLabel('Paste emails or CSV data').fill('one@example.test\ntwo@example.test')
  const send = frame.getByRole('button', { name: 'Send 2 invitations' })
  await send.click()
  await expect(frame.getByRole('alert')).toContainText('Too many attempts')
  await expect(frame.getByLabel('Paste emails or CSV data')).toHaveValue('one@example.test\ntwo@example.test')
  await expect(send).toBeEnabled()
  await send.click()
  await expect(frame.getByRole('alert')).toContainText('A batch holds 1 to 100 rows.')
  await send.click()
  await expect(frame.getByRole('alert')).toBeVisible()
  await expect(frame.getByRole('table', { name: 'Invitation results' })).toHaveCount(0)
  await send.click()
  await expect(frame.getByText('2 invited')).toBeVisible()
  await expect(frame.getByRole('alert')).toHaveCount(0)
  expect(state.bodies).toHaveLength(4)
})

test('the panel is in Thai, with reasons in Thai', async ({ page }) => {
  const state = newState(rows => created(rows, { 'member@example.test': { status: 'Skipped', reason: 'AlreadyMember' } }))
  const frame = await openPanel(page, state, 'th')
  await expect(frame.getByRole('heading', { name: 'เชิญหลายคน' })).toBeVisible()
  await frame.getByLabel('วางอีเมลหรือข้อมูล CSV').fill('new@example.test,ผู้ดูแลองค์กร,Sales\nmember@example.test\nbad')
  await expect(frame.getByText('พบปัญหา 1 แถว')).toBeVisible()
  await expect(frame.getByText('อีเมลไม่ถูกต้อง').first()).toBeVisible()
  await frame.getByRole('button', { name: 'ลบแถวที่มีปัญหา' }).click()
  await frame.getByRole('button', { name: 'ส่งคำเชิญ 2 คน' }).click()
  await expect(frame.getByText('ส่งคำเชิญแล้ว 1')).toBeVisible()
  await expect(frame.getByText('ข้าม 1')).toBeVisible()
  await expect(frame.getByText('เป็นสมาชิกขององค์กรนี้อยู่แล้ว')).toBeVisible()
  expect(state.bodies[0]!.rows[0]).toEqual({ email: 'new@example.test', role: 'TenantAdmin', groupId: 'g1' })
})

for (const width of [375, 320]) {
  test(`the panel, its preview, and its results fit a ${width}px screen without sideways scrolling`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 })
    const state = newState(rows => created(rows, { 'a.very.long.email.address.for.testing.layout@subdomain.example.test': { status: 'Skipped', reason: 'AlreadyMember' } }))
    const frame = await openPanel(page, state)
    await expectNoSidewaysScroll(page, frame)
    await frame.getByLabel('Paste emails or CSV data').fill([
      'a.very.long.email.address.for.testing.layout@subdomain.example.test,TenantAdmin,Ops Team', 'bad-address', 'short@example.test'].join('\n'))
    await expect(frame.getByRole('table', { name: 'Preview before sending' }).locator('tbody tr')).toHaveCount(3)
    await expectNoSidewaysScroll(page, frame)
    await frame.getByRole('button', { name: 'Remove rows with problems' }).click()
    await expectNoSidewaysScroll(page, frame)
    await frame.getByRole('button', { name: 'Send 2 invitations' }).click()
    await expect(frame.getByRole('table', { name: 'Invitation results' })).toBeVisible()
    await expectNoSidewaysScroll(page, frame)
  })
}
