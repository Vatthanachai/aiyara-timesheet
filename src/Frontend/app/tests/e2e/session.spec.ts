import { expect, test } from '@playwright/test'
import { renderSVG } from 'uqr'
import { readFileSync } from 'node:fs'

const organizationCode = 'ACME123'
const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })
const signInOk = <T>(session: T) => apiOk({ session, organizations: [] })
const emptyOverview = apiOk({ organizationCount: 0, activeCount: 0, suspendedCount: 0, awaitingAdministratorCount: 0, newestOrganizations: [], recentActivity: [] })

async function waitForHydration(page: import('@playwright/test').Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

test('sign in persists the session, restores it after reload, and signs out', async ({ page }) => {
  let loginRequest: unknown
  await page.route('**/api/v1/auth/sign-in', async route => {
    loginRequest = route.request().postDataJSON()
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(signInOk({
        accessToken: 'test-access-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'test-refresh-token',
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false,
        tenantId
      }))
    })
  })
  await page.route('**/api/v1/identity/profile/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(apiOk({
      email: 'admin@example.test',
      firstName: 'อรทัย',
      lastName: 'ใจดี',
      photoUrl: '/api/v1/identity/profile/me/photo',
      jobTitle: 'Administrator'
    }))
  }))
  await page.route('**/api/v1/identity/profile/me/photo', route => route.fulfill({
    status: 200,
    contentType: 'image/png',
    body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5WQAAAAASUVORK5CYII=', 'base64')
  }))
  await page.route('**/api/v1/auth/logout', route => route.fulfill({ status: 204 }))

  const navigation = page.getByRole('navigation', { name: 'Primary navigation' })
  await page.goto('/')
  await waitForHydration(page)
  await expect(navigation).toHaveCount(0)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  expect(loginRequest).toEqual({ email: 'admin@example.test', password: 'test-password' })
  await expect(page.getByRole('region', { name: 'Available applications' })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Timesheet' })).toBeVisible()
  await expect(page.getByRole('link', { name: /Reports.*View reports/ })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.access')))
    .toBe('test-access-token')
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.tenant')))
    .toBe(tenantId)

  await page.reload()
  await expect(page.locator('.profile-trigger')).toContainText('อรทัย ใจดี')
  await expect(page.locator('.profile-avatar')).toHaveAttribute('src', /^blob:/)
  await page.locator('.profile-trigger').click()
  await expect(page.getByRole('link', { name: 'Profile settings' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  await expect(navigation).toHaveCount(0)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.access')))
    .toBeNull()
})

test('profile settings previews and uploads an image then refreshes the shell avatar', async ({ page }) => {
  let uploadedFile = ''
  let photoUploaded = false
  const corsHeaders = {
    'access-control-allow-origin': 'http://127.0.0.1:3105',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    if (request.method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders })
      return
    }

    if (request.url().endsWith('/api/v1/auth/sign-in')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(signInOk({
        accessToken: 'profile-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'profile-refresh',
        mustChangePassword: false,
        tenantId,
        role: 'TenantAdmin'
      })) })
      return
    }

    if (request.url().endsWith('/api/v1/identity/profile/me/photo')) {
      if (request.method() === 'POST') {
        uploadedFile = request.postData() || ''
        photoUploaded = true
        await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json',
          body: JSON.stringify(apiOk({ photoUrl: '/api/v1/identity/profile/me/photo' })) })
      } else {
        await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'image/png',
          body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5WQAAAAASUVORK5CYII=', 'base64') })
      }
      return
    }

    if (request.url().endsWith('/api/v1/identity/profile/me')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk({
        email: 'admin@example.test', firstName: 'อรทัย', lastName: 'ใจดี',
        photoUrl: photoUploaded ? '/api/v1/identity/profile/me/photo' : null, jobTitle: null
      })) })
      return
    }
    await route.fulfill({ status: 404, headers: corsHeaders })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Profile.*Manage your profile/ }).click()

  const profileFrame = page.frameLocator('iframe')
  await expect(profileFrame.getByRole('heading', { name: 'Profile settings' })).toBeVisible()
  await expect.poll(async () => {
    const frameHeight = await page.locator('iframe').evaluate(frame => frame.getBoundingClientRect().height)
    const contentHeight = await profileFrame.locator('html').evaluate(element => element.scrollHeight)
    return Math.abs(frameHeight - contentHeight) <= 2
  }).toBe(true)
  for (const width of [768, 375, 320]) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    await expect.poll(() => profileFrame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(profileFrame.getByRole('heading', { name: 'Profile settings' })).toBeVisible()
  }
  await page.setViewportSize({ width: 1280, height: 900 })
  await profileFrame.locator('#profile-photo-input').setInputFiles({
    name: 'headshot.png',
    mimeType: 'image/png',
    buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5WQAAAAASUVORK5CYII=', 'base64')
  })
  await expect(profileFrame.locator('.photo-preview img')).toHaveAttribute('src', /^blob:/)
  await profileFrame.getByRole('button', { name: 'Save photo' }).click()
  await expect(profileFrame.getByRole('status')).toHaveText('Profile photo updated.')
  expect(uploadedFile).toContain('headshot.png')
  await expect(page.locator('.profile-trigger .profile-avatar')).toHaveAttribute('src', /^blob:/)
})

test('timesheet gives a clear month summary, quick date selection, and a responsive time form', async ({ page }) => {
  const now = new Date()
  const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  const entryDate = `${currentMonth}-03`
  const firstWeekendDate = Array.from({ length: 31 }, (_, index) => `${currentMonth}-${String(index + 1).padStart(2, '0')}`)
    .find(date => [0, 6].includes(new Date(`${date}T12:00:00`).getDay()))!
  const corsHeaders = {
    'access-control-allow-origin': 'http://127.0.0.1:3102',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  let savedEntry: Record<string, unknown> | null = null
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (request.method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders })
      return
    }
    if (url.pathname.endsWith('/api/v1/auth/sign-in')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(signInOk({
        accessToken: 'timesheet-token', refreshToken: 'timesheet-refresh',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false, tenantId, role: 'TenantAdmin'
      })) })
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/entries')) {
      if (request.method() === 'POST') {
        savedEntry = request.postDataJSON()
        await route.fulfill({ status: 201, headers: corsHeaders })
      } else {
        const sample = { id: 'entry-1', date: entryDate, startTime: '09:00:00', endTime: '10:30:00', durationMinutes: 90, taskName: 'วางแผนงานประจำสัปดาห์', detail: null, notes: null, projectId: null, categoryId: null, personalTaskId: null }
        await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk(savedEntry ? [sample, { ...sample, id: 'entry-2', ...savedEntry, durationMinutes: 60 }] : [sample])) })
      }
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/tasks')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk([])) })
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/leave')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk([
        { id: 'leave-1', date: `${currentMonth}-04`, kind: 'annual', notes: null }
      ])) })
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/catalog')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk({
        projects: [], categories: [], holidays: [{ id: 'holiday-1', date: `${currentMonth}-05`, name: 'วันหยุดทดสอบ' }]
      })) })
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/context')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk({ currentMonth, locked: false })) })
      return
    }
    await route.fulfill({ status: 404, headers: corsHeaders })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Timesheet.*Record time/ }).click()

  const timesheet = page.frameLocator('iframe')
  await expect(timesheet.getByRole('heading', { name: 'Time entries', level: 1 })).toBeVisible()
  await expect(timesheet.locator('.summary-hours strong')).toContainText('1.50')
  await expect(timesheet.getByText('วางแผนงานประจำสัปดาห์')).toBeVisible()
  const weekendCell = timesheet.locator(`.day-button[aria-label="Date ${firstWeekendDate}"]`)
  await expect(weekendCell).toHaveClass(/weekend/)
  await expect.poll(() => weekendCell.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(255, 221, 204)')
  await expect(timesheet.locator(`.day-button[aria-label="Date ${currentMonth}-04"] .leave-marker`)).toHaveCount(1)
  await expect(timesheet.locator(`.day-button[aria-label="Date ${currentMonth}-05"] .holiday-marker`)).toHaveCount(1)
  await timesheet.getByRole('button', { name: `Date ${entryDate}` }).click()
  await expect(timesheet.locator('.day-button[aria-pressed="true"]')).toHaveAttribute('aria-label', `Date ${entryDate}`)
  await timesheet.getByLabel('Task', { exact: true }).first().fill('ตรวจสอบ UX ของระบบ')
  await timesheet.getByLabel('Start', { exact: true }).fill('13:00')
  await timesheet.getByLabel('End', { exact: true }).fill('14:00')
  await expect(timesheet.locator('.duration-pill')).toContainText('1.00')

  for (const width of [768, 375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(timesheet.locator('.entry-panel .primary-button')).toBeVisible()
  }
  await timesheet.locator('.entry-panel .primary-button').click()
  await expect.poll(() => savedEntry).toMatchObject({ date: entryDate, startTime: '13:00:00', endTime: '14:00:00', taskName: 'ตรวจสอบ UX ของระบบ' })
  await expect(timesheet.locator('tbody')).toContainText('ตรวจสอบ UX ของระบบ')
  const savedRow = timesheet.locator('tbody tr').filter({ hasText: 'ตรวจสอบ UX ของระบบ' }).first()
  await savedRow.getByRole('button', { name: 'Edit' }).click()
  const editForm = timesheet.locator('.edit-grid')
  await expect(editForm).toBeVisible()
  await editForm.getByLabel('Task').fill('ข้อความที่ยกเลิก')
  await editForm.getByRole('button', { name: 'Cancel' }).click()
  await expect(editForm).toHaveCount(0)
  await expect(timesheet.locator('tbody')).toContainText('ตรวจสอบ UX ของระบบ')
  await expect(timesheet.locator('tbody')).not.toContainText('ข้อความที่ยกเลิก')
})

test('timesheet task tray keeps a rejected task name, prefills the entry form, and records leave', async ({ page }) => {
  const now = new Date()
  const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  const corsHeaders = {
    'access-control-allow-origin': 'http://127.0.0.1:3102',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  const tasks: Record<string, unknown>[] = []
  const leaves: Record<string, unknown>[] = []
  let taskPosts = 0
  let leaveRequest: Record<string, unknown> | null = null
  const json = (body: unknown) => ({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk(body)) })
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (request.method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders })
      return
    }
    if (url.pathname.endsWith('/api/v1/auth/sign-in')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(signInOk({
        accessToken: 'tray-token', refreshToken: 'tray-refresh',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false, tenantId, role: 'Employee'
      })) })
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/tasks')) {
      if (request.method() === 'POST') {
        taskPosts += 1
        if (taskPosts === 1) {
          await route.fulfill({ status: 400, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify({
            statusCode: 400, statusMessage: 'Bad Request', errorCode: 'INVALID_VALUE'
          }) })
          return
        }
        const body = request.postDataJSON()
        tasks.push({ id: `task-${taskPosts}`, name: body.name, status: body.status, projectId: null, categoryId: null })
        await route.fulfill({ status: 201, headers: corsHeaders })
        return
      }
      await route.fulfill(json(tasks))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/leave')) {
      if (request.method() === 'POST') {
        leaveRequest = request.postDataJSON()
        leaves.push({ id: 'leave-1', notes: null, ...leaveRequest })
        await route.fulfill({ status: 201, headers: corsHeaders })
        return
      }
      await route.fulfill(json(leaves))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/entries')) {
      await route.fulfill(json([]))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/catalog')) {
      await route.fulfill(json({ projects: [{ id: 'project-1', name: 'Website' }], categories: [], holidays: [] }))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/context')) {
      await route.fulfill(json({ currentMonth, locked: false }))
      return
    }
    await route.fulfill({ status: 404, headers: corsHeaders })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('employee@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Timesheet.*Record time/ }).click()

  const timesheet = page.frameLocator('iframe')
  const tray = timesheet.locator('.task-panel')
  const newTask = tray.locator('.add-task-form input')
  await newTask.fill('Weekly planning')
  await tray.getByRole('button', { name: 'Add entry' }).click()
  await expect(timesheet.locator('.feedback.error')).toBeVisible()
  await expect(newTask).toHaveValue('Weekly planning')
  await tray.getByRole('button', { name: 'Add entry' }).click()
  await expect(newTask).toHaveValue('')
  await expect(tray.locator('.task-list li input')).toHaveValue('Weekly planning')

  const entryTask = timesheet.locator('.entry-panel').getByLabel('Task', { exact: true })
  await tray.locator('.task-list').getByRole('button', { name: 'Use' }).click()
  await expect(entryTask).toHaveValue('Weekly planning')
  await tray.locator('.quick-picks').getByRole('button', { name: 'Website' }).click()
  await expect(entryTask).toHaveValue('Website')
  await expect(timesheet.locator('.entry-panel').getByLabel('Project')).toHaveValue('project-1')

  const leaveForm = timesheet.locator('.inline-form')
  await leaveForm.getByLabel('Date').fill(`${currentMonth}-10`)
  await leaveForm.getByLabel('Kind').selectOption('sick')
  await leaveForm.getByRole('button', { name: 'Add entry' }).click()
  await expect.poll(() => leaveRequest).toEqual({ date: `${currentMonth}-10`, kind: 'sick' })
  await expect(timesheet.locator('.simple-list li input[type="date"]')).toHaveValue(`${currentMonth}-10`)
})

test('timesheet task board moves cards by button and drag, reverts a rejected move, and logs time', async ({ page }) => {
  const now = new Date()
  const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  const corsHeaders = {
    'access-control-allow-origin': 'http://127.0.0.1:3102',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  const tasks = [
    { id: 'task-1', name: 'Write release notes', status: 'todo', projectId: 'project-1', categoryId: null },
    { id: 'task-2', name: 'Review designs', status: 'doing', projectId: null, categoryId: null }
  ]
  const updates: { id: string; status: string }[] = []
  let rejectNextUpdate = false
  const json = (body: unknown) => ({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk(body)) })
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (request.method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders })
      return
    }
    if (url.pathname.endsWith('/api/v1/auth/sign-in')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(signInOk({
        accessToken: 'board-token', refreshToken: 'board-refresh',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false, tenantId, role: 'Employee'
      })) })
      return
    }
    const taskUpdate = url.pathname.match(/\/api\/v1\/timesheets\/tasks\/([^/]+)$/)
    if (taskUpdate && request.method() === 'PUT') {
      if (rejectNextUpdate) {
        rejectNextUpdate = false
        await route.fulfill({ status: 400, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify({
          statusCode: 400, statusMessage: 'Bad Request', errorCode: 'VALIDATION_FAILED'
        }) })
        return
      }
      const body = request.postDataJSON()
      updates.push({ id: taskUpdate[1], status: body.status })
      Object.assign(tasks.find(task => task.id === taskUpdate[1])!, body)
      await route.fulfill(json(tasks.find(task => task.id === taskUpdate[1])))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/tasks')) {
      await route.fulfill(json(tasks))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/leave') || url.pathname.endsWith('/api/v1/timesheets/entries')) {
      await route.fulfill(json([]))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/catalog')) {
      await route.fulfill(json({ projects: [{ id: 'project-1', name: 'Website' }], categories: [], holidays: [] }))
      return
    }
    if (url.pathname.endsWith('/api/v1/timesheets/context')) {
      await route.fulfill(json({ currentMonth, locked: false }))
      return
    }
    await route.fulfill({ status: 404, headers: corsHeaders })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('employee@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Timesheet.*Record time/ }).click()

  const timesheet = page.frameLocator('iframe')
  await timesheet.getByRole('button', { name: 'Task board' }).click()
  const column = (name: string) => timesheet.getByRole('region', { name, exact: true })
  const card = (name: string) => timesheet.locator('.board-card', { hasText: name })
  await expect(column('To do').locator('.board-card')).toHaveText(/Write release notes\s*Website/)
  await expect(column('Doing').locator('.board-card')).toContainText('Review designs')
  await expect(column('Done')).toContainText('No tasks')

  await card('Write release notes').getByRole('button', { name: 'Move to Doing' }).click()
  await expect(column('Doing').locator('.board-card')).toHaveCount(2)
  await expect.poll(() => updates).toEqual([{ id: 'task-1', status: 'doing' }])

  await card('Review designs').dragTo(column('Done'))
  await expect(column('Done').locator('.board-card')).toContainText('Review designs')
  await expect.poll(() => updates.at(-1)).toEqual({ id: 'task-2', status: 'done' })

  rejectNextUpdate = true
  await card('Write release notes').getByRole('button', { name: 'Move to Done' }).click()
  await expect(column('Doing').locator('.board-card')).toContainText('Write release notes')
  // The reload that restores the card must not wipe the rejection message.
  await expect(timesheet.locator('.feedback.error')).toBeVisible()
  await page.waitForTimeout(500)
  await expect(timesheet.locator('.feedback.error')).toBeVisible()
  expect(updates).toHaveLength(2)

  // The columns stack on a phone; the board must not make the page scroll sideways.
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => timesheet.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(card('Write release notes').getByRole('button', { name: 'Log time' })).toBeVisible()
  }
  await page.setViewportSize({ width: 1280, height: 900 })

  await card('Write release notes').getByRole('button', { name: 'Log time' }).click()
  await expect(timesheet.locator('.entry-panel').getByLabel('Task', { exact: true })).toHaveValue('Write release notes')
  await expect(timesheet.locator('.entry-panel').getByLabel('Project')).toHaveValue('project-1')
})

test('failed sign in displays an error and does not persist credentials', async ({ page }) => {
  // Shaped like Identity's real ServiceErrorResponse for AuthenticationFailure.InvalidCredentials
  // (IdentityController.Auth's catch maps it to errorCode "INVALIDCREDENTIALS"), not a generic
  // problem+json body, so this exercises the same safeError() branch the real API triggers.
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill({
    status: 401,
    contentType: 'application/json',
    body: JSON.stringify({ statusCode: 401, statusMessage: 'The request could not be completed.', errorCode: 'INVALIDCREDENTIALS' })
  }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('wrong-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('alert')).toHaveText('Incorrect email or password.')
  await expect(page.getByRole('region', { name: 'Available applications' })).toHaveCount(0)
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.access'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.refresh'))).toBeNull()
})

test('rejected sign in fields show their own messages and clear on the next attempt', async ({ page }) => {
  // Shaped like the real Gateway response to a sign-in body missing a field: SafeApiErrors'
  // model-state factory keys errors by the C# property name with the INVALID_VALUE code.
  let attempts = 0
  await page.route('**/api/v1/auth/sign-in', route => {
    attempts++
    return route.fulfill(attempts === 1
      ? { status: 400, contentType: 'application/json', body: JSON.stringify({
          statusCode: 400, statusMessage: 'The request contains invalid fields.', errorCode: 'VALIDATION_FAILED',
          errors: { Email: ['INVALID_VALUE'] }, traceId: 'trace-1' }) }
      : { status: 401, contentType: 'application/json', body: JSON.stringify({
          statusCode: 401, statusMessage: 'The request could not be completed.', errorCode: 'INVALIDCREDENTIALS' }) })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('secret-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('alert')).toHaveText('Please check the entered information.')
  const email = page.getByRole('textbox', { name: 'Email', exact: true })
  await expect(email).toHaveAttribute('aria-invalid', 'true')
  await expect(email).toHaveAccessibleDescription('Invalid value')
  await expect(page.getByLabel('Password', { exact: true })).not.toHaveAttribute('aria-invalid', 'true')

  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('alert')).toHaveText('Incorrect email or password.')
  await expect(email).not.toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByText('Invalid value')).toHaveCount(0)
})

test('activation submits the email code and new password then links to sign in', async ({ page }) => {
  let activationRequest: unknown
  await page.route('**/api/v1/auth/activate', async route => {
    activationRequest = route.request().postDataJSON()
    await route.fulfill({ status: 204 })
  })

  await page.goto('/activate')
  await waitForHydration(page)
  await page.getByLabel('Activation code from email').fill('one-time-activation-code')
  await page.getByLabel('New password', { exact: true }).fill('New-Strong-Password-123!')
  await page.getByLabel('Confirm new password').fill('New-Strong-Password-123!')
  await page.getByRole('button', { name: 'Activate account' }).click()

  await expect.poll(() => activationRequest).toEqual({
    code: 'one-time-activation-code',
    password: 'New-Strong-Password-123!'
  })
  await expect(page.locator('section[role="status"]')).toContainText('Account activated')
  await expect(page.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute('href', '/')
})

test('forgot password sends only the email and points to the reset page from both sign in pages', async ({ page }) => {
  const forgotRequests: unknown[] = []
  await page.route('**/api/v1/auth/password/forgot', async route => {
    forgotRequests.push(route.request().postDataJSON())
    await route.fulfill({ status: 202 })
  })

  await page.goto('/sys-admin/login')
  await waitForHydration(page)
  await expect(page.getByRole('link', { name: 'Forgot password?' })).toHaveAttribute('href', '/forgot-password')

  await page.goto('/')
  await waitForHydration(page)
  await page.getByRole('link', { name: 'Forgot password?' }).click()
  await expect(page).toHaveURL(/\/forgot-password$/)
  await waitForHydration(page)
  await page.getByLabel('Email').fill('  person@example.test ')
  await page.getByRole('button', { name: 'Send reset code' }).click()

  await expect(page.locator('section[role="status"]')).toContainText('Check your email')
  expect(forgotRequests).toEqual([{ email: 'person@example.test' }])
  await page.getByRole('link', { name: 'I have a code' }).click()
  await expect(page).toHaveURL(/\/reset-password$/)
})

test('reset password explains a weak password and a bad code, then confirms the change', async ({ page }) => {
  const resetRequests: unknown[] = []
  // Shaped like Identity's real responses: a policy failure is AuthenticationFailure.InvalidInput (400)
  // and an unknown, used, or expired code is InvalidCredentials (401).
  const responses = [
    { status: 400, contentType: 'application/json', body: JSON.stringify({
      statusCode: 400, statusMessage: 'The request could not be completed.', errorCode: 'INVALIDINPUT' }) },
    { status: 401, contentType: 'application/json', body: JSON.stringify({
      statusCode: 401, statusMessage: 'The request could not be completed.', errorCode: 'INVALIDCREDENTIALS' }) },
    { status: 204 }
  ]
  await page.route('**/api/v1/auth/password/reset', async route => {
    resetRequests.push(route.request().postDataJSON())
    await route.fulfill(responses[resetRequests.length - 1])
  })

  await page.goto('/reset-password')
  await waitForHydration(page)
  await page.getByLabel('Reset code from email').fill(' reset-code-from-email ')
  await page.getByLabel('New password', { exact: true }).fill('New-Strong-Password-123!')
  await page.getByLabel('Confirm new password').fill('Different-Password-123!')
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByRole('alert')).toHaveText('The passwords do not match.')
  expect(resetRequests).toHaveLength(0)

  await page.getByLabel('Confirm new password').fill('New-Strong-Password-123!')
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByRole('alert')).toHaveText('The new password does not meet your organizations’ requirements.')
  await page.getByRole('button', { name: 'Set new password' }).click()
  await expect(page.getByRole('alert')).toHaveText('This reset code is not valid or has expired. Request a new one.')
  await page.getByRole('button', { name: 'Set new password' }).click()

  await expect(page.locator('section[role="status"]')).toContainText('Password updated')
  expect(resetRequests[0]).toEqual({ code: 'reset-code-from-email', password: 'New-Strong-Password-123!' })
  await expect(page.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute('href', '/')
  await expect(page.getByRole('link', { name: 'System administrator sign in' })).toHaveAttribute('href', '/sys-admin/login')
})

test('organization admin can sign up and sees the organization code without displaying onboarding key', async ({ page }) => {
  let signupRequest: unknown
  let activationRequest: unknown
  await page.route('**/api/v1/tenants', async route => {
    signupRequest = route.request().postDataJSON()
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
        tenantId,
        organizationCode: 'ACME123',
        accountId: 'ab7ada79-f0c9-4c10-8c6a-63422c329450',
        membershipId: 'bb88c5cc-6eef-42d8-8a20-210730a29977',
        role: 'TenantAdmin',
        status: 'PendingActivation',
        timeZoneId: 'Asia/Bangkok',
        onboardingKey: 'must-not-be-shown-or-stored',
        onboardingKeyExpiresAtUtc: new Date(Date.now() + 7 * 24 * 60 * 60_000).toISOString()
      }))
    })
  })
  await page.route('**/api/v1/auth/activation/request', async route => {
    activationRequest = route.request().postDataJSON()
    await route.fulfill({ status: 202 })
  })

  await page.goto('/signup')
  await waitForHydration(page)
  await page.getByLabel('Organization name').fill('Acme Studio')
  await page.getByLabel('Organization URL name').fill('acme-studio')
  await page.getByLabel('Organization administrator email').fill('admin@example.test')
  await page.getByRole('button', { name: 'Sign up organization' }).click()

  expect(signupRequest).toEqual({ name: 'Acme Studio', slug: 'acme-studio', adminEmail: 'admin@example.test' })
  const confirmation = page.locator('.signup-result')
  await expect(confirmation).toContainText('ACME123')
  await expect(confirmation).toContainText('admin@example.test')
  await expect(confirmation).not.toContainText('must-not-be-shown-or-stored')
  await expect(confirmation.getByRole('link', { name: 'Go to account activation' })).toHaveAttribute('href', '/activate')
  await expect(confirmation).toContainText('at least 12 characters')
  await confirmation.getByRole('button', { name: 'Didn’t receive the email? Send another code' }).click()
  expect(activationRequest).toEqual({ tenantId, email: 'admin@example.test' })
  await expect(confirmation).toContainText('a new code will be sent')
})

test('platform admin can manage organization details, administrators, and lifecycle', async ({ page }) => {
  const platformTenantId = '507b1b8b-b07f-48d5-9aa1-ecfb6a5c8af0'
  const organizationTenantId = 'c99358d1-e01f-4dfd-9693-38da8883562d'
  let listAuthorization = ''
  let resendRequest: unknown
  const managementRequests: Array<{ method: string; url: string; body: unknown }> = []
  page.on('dialog', dialog => dialog.accept())

  await page.route('**/api/v1/auth/sign-in', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(signInOk({
        accessToken: 'platform-admin-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'platform-admin-refresh',
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false,
        tenantId: platformTenantId,
        role: 'PlatformAdmin'
      }))
    })
  })
  await page.route('**/api/v1/platform/organizations**', async route => {
    if (route.request().method() !== 'GET') {
      if (route.request().url().endsWith('/administrators/activation')) {
        resendRequest = route.request().postDataJSON()
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({ deliveryStatus: 'accepted' })) })
        return
      }
      managementRequests.push({ method: route.request().method(), url: route.request().url(), body: route.request().postDataJSON() })
      await route.fulfill({ status: route.request().method() === 'POST' ? 202 : 200,
        contentType: 'application/json', body: JSON.stringify(apiOk({ deliveryStatus: 'accepted' })) })
      return
    }
    listAuthorization = route.request().headers().authorization || ''
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
        totalCount: 1,
        page: 1,
        pageSize: 25,
        items: [{
          tenantId: organizationTenantId,
          organizationCode: 'ACME123',
          name: 'Acme Studio',
          slug: 'acme-studio',
          status: 'Active',
          createdAtUtc: '2026-10-01T00:00:00Z',
          admins: [{ email: 'admin@acme.test', status: 'PendingActivation' }]
        }]
      }))
    })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('sysadmin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.route('**/api/v1/platform/overview', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(emptyOverview) }))
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'System admin' }).click()
  await page.getByRole('navigation', { name: 'System administration' }).getByRole('link', { name: 'Organizations' }).click()

  await expect(page.getByRole('heading', { name: 'Organizations' })).toBeVisible()
  await expect(page.getByText('Acme Studio')).toBeVisible()
  await expect(page.getByText('ACME123')).toBeVisible()
  await expect(page.getByText('Pending activation')).toBeVisible()
  expect(listAuthorization).toBe('Bearer platform-admin-token')
  await page.getByRole('button', { name: 'Resend code' }).click()
  expect(resendRequest).toEqual({ email: 'admin@acme.test' })
  await expect(page.locator('.admin-row')).toContainText('Request accepted')
  await page.getByRole('button', { name: 'Edit organization' }).click()
  await page.getByLabel('Organization name').last().fill('Acme Renamed')
  await page.getByLabel('Organization URL name').last().fill('acme-renamed')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('Organization details saved.')).toBeVisible()
  await page.getByRole('button', { name: 'Suspend organization' }).click()
  await expect(page.getByText('Organization status updated.')).toBeVisible()
  await page.getByLabel('Invite another administrator').fill('second@acme.test')
  await page.getByRole('button', { name: 'Send invitation' }).click()
  await expect(page.getByText('Administrator invitation sent.')).toBeVisible()
  expect(managementRequests.map(request => request.method)).toEqual(['PUT', 'PUT', 'POST'])
  expect(managementRequests[0].body).toEqual({ name: 'Acme Renamed', slug: 'acme-renamed', reason: null })
  expect(managementRequests[1].body).toEqual({ suspended: true, reason: null })
  expect(managementRequests[2].body).toEqual({ email: 'second@acme.test' })
})

test('tenant administrator cannot see the platform organization management page', async ({ page }) => {
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(signInOk({
      accessToken: 'tenant-admin-token',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshToken: 'tenant-admin-refresh',
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
      mustChangePassword: false,
      tenantId,
      role: 'TenantAdmin'
    }))
  }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('link', { name: 'System admin' })).toHaveCount(0)
  await page.goto('/sys-admin/organizations')
  await expect(page.getByRole('heading', { name: 'Access restricted' })).toBeVisible()
})

test('activation failure shows a safe message and does not expose the API error', async ({ page }) => {
  await page.route('**/api/v1/auth/activate', route => route.fulfill({
    status: 400,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: 'Invalid or expired challenge.', status: 400 })
  }))

  await page.goto('/activate')
  await waitForHydration(page)
  await page.getByLabel('Activation code from email').fill('expired-code')
  await page.getByLabel('New password', { exact: true }).fill('New-Strong-Password-123!')
  await page.getByLabel('Confirm new password').fill('New-Strong-Password-123!')
  await page.getByRole('button', { name: 'Activate account' }).click()

  await expect(page.getByRole('alert')).toHaveText(
    'Activation failed. Check the code and your organization’s password requirements.'
  )
  await expect(page.getByRole('alert')).not.toContainText('Invalid or expired challenge')
})

const platformSession = () => ({
  accessToken: 'platform-admin-token',
  accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
  refreshToken: 'platform-admin-refresh',
  refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
  mustChangePassword: false,
  tenantId: '00000000-0000-0000-0000-000000000001',
  role: 'PlatformAdmin'
})
const mfaRequired = { status: 401, contentType: 'application/json',
  body: JSON.stringify({ statusCode: 401, statusMessage: 'The request could not be completed.', errorCode: 'MFAREQUIRED' }) }

test('platform admin signs in with password and authenticator code and lands on the overview', async ({ page }) => {
  const loginRequests: unknown[] = []
  await page.route('**/api/v1/auth/platform-admin/login', async route => {
    const body = route.request().postDataJSON()
    loginRequests.push(body)
    if (!body.totpCode && !body.recoveryCode) return route.fulfill(mfaRequired)
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify(apiOk({ session: platformSession(), enrollment: null, recoveryCodes: [] })) })
  })
  await page.route('**/api/v1/platform/overview', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(emptyOverview) }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByRole('link', { name: 'System administrator sign in' }).click()
  await expect(page).toHaveURL(/\/sys-admin\/login$/)
  await waitForHydration(page)
  await expect(page.getByLabel('Organization code')).toHaveCount(0)
  await page.getByLabel('Email').fill('operator@platform.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  const code = page.getByLabel('Authentication code')
  await expect(code).toBeFocused()
  await expect(page.getByRole('alert')).toHaveCount(0)
  await code.fill('123456')
  await page.getByRole('button', { name: 'Continue' }).click()

  expect(loginRequests).toEqual([
    { email: 'operator@platform.test', password: 'test-password' },
    { email: 'operator@platform.test', password: 'test-password', totpCode: '123456' }
  ])
  await expect(page).toHaveURL(/\/sys-admin$/)
  await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.role')))
    .toBe('PlatformAdmin')
})

test('first platform admin sign in enrolls an authenticator and shows recovery codes before entering', async ({ page }) => {
  const loginRequests: unknown[] = []
  const recoveryCodes = ['ABCDE-FGHJK', 'LMNPQ-RSTUV']
  await page.route('**/api/v1/auth/platform-admin/login', async route => {
    const body = route.request().postDataJSON()
    loginRequests.push(body)
    const result = body.totpCode
      ? { session: platformSession(), enrollment: null, recoveryCodes }
      : { session: null, enrollment: { secret: 'JBSWY3DPEHPK3PXP',
          provisioningUri: 'otpauth://totp/Aiyara%20Timesheet:operator%40platform.test?secret=JBSWY3DPEHPK3PXP' }, recoveryCodes: [] }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
  })
  await page.route('**/api/v1/platform/overview', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(emptyOverview) }))

  await page.goto('/sys-admin/login')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('operator@platform.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('heading', { name: 'Set up your authenticator' })).toBeVisible()
  await expect(page.getByText('JBSWY3DPEHPK3PXP')).toBeVisible()
  const qr = page.getByRole('img', { name: 'QR code to add this account to an authenticator app' })
  await expect(qr).toBeVisible()
  expect(await qr.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
  // The QR carries the provisioning URI itself, drawn locally rather than by a remote QR service.
  expect(decodeURIComponent((await qr.getAttribute('src'))!.replace(/^data:image\/svg\+xml;charset=utf-8,/, '')))
    .toBe(renderSVG('otpauth://totp/Aiyara%20Timesheet:operator%40platform.test?secret=JBSWY3DPEHPK3PXP', { pixelSize: 6 }))
  await expect(page.getByRole('link', { name: 'Open in authenticator app' })).toHaveAttribute('href', /^otpauth:\/\/totp\//)
  await page.getByLabel('Authentication code').fill('654321')
  await page.getByRole('button', { name: 'Continue' }).click()

  const codes = page.getByRole('list', { name: 'Save your recovery codes' })
  await expect(codes.getByRole('listitem')).toHaveText(recoveryCodes)
  await expect(page).toHaveURL(/\/sys-admin\/login$/)
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.access'))).toBeNull()
  await page.getByRole('button', { name: 'I saved these codes, continue' }).click()
  await expect(page).toHaveURL(/\/sys-admin$/)
  expect(loginRequests.at(-1)).toEqual({ email: 'operator@platform.test', password: 'test-password', totpCode: '654321' })
})

test('platform admin can sign in with a recovery code and sees why a wrong code fails', async ({ page }) => {
  const loginRequests: { totpCode?: string; recoveryCode?: string }[] = []
  await page.route('**/api/v1/auth/platform-admin/login', async route => {
    const body = route.request().postDataJSON()
    loginRequests.push(body)
    if (!body.totpCode && !body.recoveryCode) return route.fulfill(mfaRequired)
    if (body.totpCode) return route.fulfill({ status: 401, contentType: 'application/json',
      body: JSON.stringify({ statusCode: 401, statusMessage: 'The request could not be completed.', errorCode: 'MFAINVALID' }) })
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify(apiOk({ session: platformSession(), enrollment: null, recoveryCodes: [] })) })
  })
  await page.route('**/api/v1/platform/overview', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(emptyOverview) }))

  await page.goto('/sys-admin/login')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('operator@platform.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByLabel('Authentication code').fill('000000')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('alert')).toHaveText('That code is not valid or has already been used.')

  await page.getByRole('button', { name: 'Use a recovery code instead' }).click()
  await page.getByLabel('Recovery code').fill('ABCDE-FGHJK')
  await page.getByRole('button', { name: 'Continue' }).click()

  await expect(page).toHaveURL(/\/sys-admin$/)
  expect(loginRequests.at(-1)).toMatchObject({ recoveryCode: 'ABCDE-FGHJK' })
  expect(loginRequests.at(-1)).not.toHaveProperty('totpCode')
})

test('email sign in for a platform admin account points to the system administrator page', async ({ page }) => {
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill(mfaRequired))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('operator@platform.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('alert')).toHaveText('This account signs in on the system administrator page.')
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.access'))).toBeNull()
})

test('platform admin sign in shows a safe error for rejected credentials', async ({ page }) => {
  await page.route('**/api/v1/auth/platform-admin/login', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: 'Invalid credentials.', status: 401 })
  }))

  await page.goto('/sys-admin/login')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@acme.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('alert')).toBeVisible()
  await expect(page).toHaveURL(/\/sys-admin\/login$/)
})

test('sign in asks which organization to open when the account belongs to several', async ({ page }) => {
  const loginRequests: unknown[] = []
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(apiOk({
      session: null,
      organizations: [
        { organizationCode, name: 'Acme' },
        { organizationCode: 'BETA456', name: 'Beta' }
      ]
    }))
  }))
  await page.route('**/api/v1/auth/login', async route => {
    loginRequests.push(route.request().postDataJSON())
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
        accessToken: 'beta-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'beta-refresh',
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false,
        tenantId,
        role: 'Employee'
      }))
    })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('heading', { name: 'Choose an organization' })).toBeVisible()
  await page.getByRole('button', { name: 'Beta (BETA456)' }).click()

  expect(loginRequests).toEqual([{ organizationCode: 'BETA456', email: 'admin@example.test', password: 'test-password' }])
  await expect(page.getByRole('region', { name: 'Available applications' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.access')))
    .toBe('beta-token')
})

async function signInAs(page: import('@playwright/test').Page, role: string) {
  await page.route('**/api/v1/auth/sign-in', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(signInOk({
      accessToken: `${role}-token`,
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshToken: `${role}-refresh`,
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
      mustChangePassword: false,
      tenantId,
      role
    }))
  }))
  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill(`${role.toLowerCase()}@example.test`)
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('region', { name: 'Available applications' })).toBeVisible()
}

test('platform admin sees only platform areas and cannot open tenant administration', async ({ page }) => {
  await signInAs(page, 'PlatformAdmin')

  const navigation = page.getByRole('navigation', { name: 'Primary navigation' })
  await expect(navigation.getByRole('link', { name: 'System admin' })).toBeVisible()
  for (const name of ['Timesheet', 'Reports', 'Administration'])
    await expect(navigation.getByRole('link', { name })).toHaveCount(0)
  const apps = page.getByRole('region', { name: 'Available applications' })
  await expect(apps.getByRole('link', { name: /System admin/ })).toBeVisible()
  await expect(apps.getByRole('link', { name: /Administration/ })).toHaveCount(0)

  await page.goto('/remote/administration')
  await expect(page.getByRole('heading', { name: 'Access restricted' })).toBeVisible()
  await expect(page.locator('iframe')).toHaveCount(0)
})

test('employee cannot see or open tenant administration', async ({ page }) => {
  await signInAs(page, 'Employee')

  const navigation = page.getByRole('navigation', { name: 'Primary navigation' })
  await expect(navigation.getByRole('link', { name: 'Timesheet' })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Administration' })).toHaveCount(0)

  await page.goto('/remote/administration')
  await expect(page.getByRole('heading', { name: 'Access restricted' })).toBeVisible()
  await expect(page.locator('iframe')).toHaveCount(0)
})

test('tenant admin can open tenant administration', async ({ page }) => {
  await signInAs(page, 'TenantAdmin')

  await expect(page.getByRole('navigation', { name: 'Primary navigation' })
    .getByRole('link', { name: 'Administration' })).toBeVisible()
  await page.goto('/remote/administration')
  await expect(page.getByRole('heading', { name: 'Access restricted' })).toHaveCount(0)
  await expect(page.locator('iframe')).toHaveCount(1)
})

test('administration asks the Shell for its session and keeps unsaved edits when the session is resent', async ({ page }) => {
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3104',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  let catalogCalls = 0
  let policyCalls = 0
  const policySaves: unknown[] = []
  await page.addInitScript(() => {
    if (window !== window.top) return
    const seen: string[] = []
    Object.assign(window, { remoteMessages: seen })
    window.addEventListener('message', event => { if (typeof event.data?.type === 'string') seen.push(event.data.type) })
  })
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const url = new URL(request.url())
    const ok = (result: unknown) => route.fulfill({ status: 200, headers: cors, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (url.pathname.endsWith('/auth/sign-in')) return ok({ session: {
      accessToken: 'admin-token', refreshToken: 'admin-refresh', mustChangePassword: false, tenantId, role: 'TenantAdmin',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (url.pathname.endsWith('/timesheets/catalog')) {
      catalogCalls++
      return ok({ projects: [{ id: 'p1', name: 'Apollo' }], categories: [], holidays: [] })
    }
    if (url.pathname.endsWith(`/identity/tenants/${tenantId}/password-policy`)) {
      policyCalls++
      return ok({ minimumLength: 14, expiryDays: 90, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: false })
    }
    if (url.pathname.endsWith(`/auth/tenants/${tenantId}/password-policy`) && request.method() === 'PUT') {
      policySaves.push(request.postDataJSON())
      return route.fulfill({ status: 400, headers: cors, contentType: 'application/json',
        body: JSON.stringify({ statusCode: 400, statusMessage: 'Bad Request', errorCode: 'INVALIDINPUT' }) })
    }
    if (url.pathname.endsWith('/identity/members')) return ok([])
    if (url.pathname.endsWith('/identity/members/invitations')) return ok([])
    if (url.pathname.endsWith('/identity/members/groups')) return ok([])
    if (url.pathname.endsWith('/identity/organization')) return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    if (url.pathname.endsWith('/identity/profile/me')) return ok({ email: 'admin@example.test', firstName: '', lastName: '', photoUrl: null })
    return route.fulfill({ status: 404, headers: cors })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()

  const frame = page.frameLocator('iframe')
  await expect(frame.getByRole('heading', { name: 'Administration', level: 1 })).toBeVisible()
  await expect.poll(() => page.evaluate(() => (window as unknown as { remoteMessages: string[] }).remoteMessages))
    .toContain('aiyara.ready')
  const minimumLength = frame.getByRole('spinbutton', { name: 'Minimum length' })
  await expect(minimumLength).toHaveValue('14')
  await expect.poll(() => catalogCalls).toBe(1)

  // The Shell resends the session on every locale change and every minute; that must not reload the
  // page and overwrite what the administrator is typing.
  await minimumLength.fill('20')
  await page.getByRole('button', { name: 'TH' }).click()
  await expect(frame.getByRole('heading', { name: 'จัดการองค์กร', level: 1 })).toBeVisible()
  await expect(frame.getByRole('spinbutton', { name: 'ความยาวขั้นต่ำ' })).toHaveValue('20')
  expect(catalogCalls).toBe(1)
  expect(policyCalls).toBe(1)

  // The field accepts only what Identity accepts (12–128), and a rejected save says why.
  const thaiMinimum = frame.getByRole('spinbutton', { name: 'ความยาวขั้นต่ำ' })
  await thaiMinimum.fill('8')
  await frame.getByRole('button', { name: 'บันทึกนโยบาย' }).click()
  expect(await thaiMinimum.evaluate(input => (input as HTMLInputElement).validity.rangeUnderflow)).toBe(true)
  expect(policySaves).toEqual([])
  await thaiMinimum.fill('13')
  await frame.getByRole('button', { name: 'บันทึกนโยบาย' }).click()
  await expect(frame.getByRole('alert')).toContainText('ความยาวขั้นต่ำต้องอยู่ระหว่าง 12–128 ตัวอักษร')
  expect(policySaves).toHaveLength(1)
})

test('tenant admin manages members and invitations from the Members panel', async ({ page }) => {
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3104',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  // A small stateful stand-in for Identity's member routes.
  const members = [
    { id: 'a1', firstName: 'Ada', lastName: 'Admin', email: 'admin@example.test', role: 'TenantAdmin', status: 'Active', isCurrentUser: true, groupId: null as string | null },
    { id: 'a2', firstName: 'Zoe', lastName: 'Staff', email: 'zoe@example.test', role: 'Employee', status: 'Active', isCurrentUser: false, groupId: null as string | null },
    { id: 'a3', firstName: '', lastName: '', email: 'new.hire@example.test', role: 'Employee', status: 'PendingActivation', isCurrentUser: false, groupId: null as string | null }
  ]
  const groups: { id: string; name: string }[] = []
  const invitations = [{ id: 'i1', email: 'later@example.test', role: 'Employee', invitedByEmail: 'admin@example.test',
    createdAtUtc: '2026-10-20T08:00:00Z', expiresAtUtc: '2026-10-27T08:00:00Z', groupName: null as string | null }]
  const calls: string[] = []
  let refuseNextRoleChange = false
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const url = new URL(request.url())
    const ok = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ ...apiOk(result), statusCode: status }) })
    const fail = (status: number, errorCode: string) => route.fulfill({ status, headers: cors, contentType: 'application/json',
      body: JSON.stringify({ statusCode: status, statusMessage: 'Error', errorCode }) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    const path = url.pathname.replace('/api/v1', '')
    if (path === '/auth/sign-in') return ok({ session: {
      accessToken: 'admin-token', refreshToken: 'admin-refresh', mustChangePassword: false, tenantId, role: 'TenantAdmin',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [] })
    if (path === `/identity/tenants/${tenantId}/password-policy`)
      return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (path === '/identity/profile/me') return ok({ email: 'admin@example.test', firstName: 'Ada', lastName: 'Admin', photoUrl: null })
    if (path === '/identity/members') return ok(members.map(x => ({ ...x, groupName: groups.find(g => g.id === x.groupId)?.name ?? null })))
    if (path === '/identity/members/invitations') return ok(invitations)
    if (path === '/identity/members/groups' && request.method() === 'GET')
      return ok(groups.map(g => ({ ...g, memberCount: members.filter(x => x.groupId === g.id).length })))
    if (path === '/identity/organization') return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    if (request.method() !== 'GET') calls.push(`${request.method()} ${path} ${request.postData() ?? ''}`.trim())
    if (path === `/auth/tenants/${tenantId}/invitations`) {
      const body = request.postDataJSON()
      if (body.email === 'taken@example.test') return fail(409, 'CONFLICT')
      invitations.unshift({ id: 'i2', email: body.email, role: body.role, invitedByEmail: 'admin@example.test',
        createdAtUtc: '2026-10-20T09:00:00Z', expiresAtUtc: '2026-10-27T09:00:00Z',
        groupName: groups.find(g => g.id === body.groupId)?.name ?? null })
      return ok({ invitationId: 'i2', tenantId, expiresAtUtc: '2026-10-27T09:00:00Z', code: 'secret-code' }, 201)
    }
    if (path === '/identity/members/groups') {
      const name = request.postDataJSON().name
      if (groups.some(g => g.name.toLowerCase() === name.toLowerCase())) return fail(409, 'GROUP_NAME_TAKEN')
      groups.push({ id: `g${groups.length + 1}`, name })
      return ok({ id: `g${groups.length}`, name, memberCount: 0 }, 201)
    }
    const group = groups.find(g => path === `/identity/members/groups/${g.id}`)
    if (group && request.method() === 'DELETE') {
      if (members.some(x => x.groupId === group.id)) return fail(409, 'GROUP_IN_USE')
      groups.splice(groups.indexOf(group), 1)
      return route.fulfill({ status: 204, headers: cors })
    }
    if (group) {
      group.name = request.postDataJSON().name
      return ok({ ...group, memberCount: members.filter(x => x.groupId === group.id).length })
    }
    const member = members.find(x => path.startsWith(`/identity/members/${x.id}/`))
    if (member && path.endsWith('/group')) {
      member.groupId = request.postDataJSON().groupId
      return route.fulfill({ status: 204, headers: cors })
    }
    if (member && path.endsWith('/role')) {
      if (refuseNextRoleChange) { refuseNextRoleChange = false; return fail(409, 'LAST_ADMINISTRATOR') }
      member.role = request.postDataJSON().role
      return route.fulfill({ status: 204, headers: cors })
    }
    if (member && path.endsWith('/status')) {
      member.status = request.postDataJSON().active ? 'Active' : 'Deactivated'
      return route.fulfill({ status: 204, headers: cors })
    }
    if (member && path.endsWith('/activation')) return ok(null, 202)
    const invitation = invitations.find(x => path.startsWith(`/identity/members/invitations/${x.id}`))
    if (invitation && path.endsWith('/resend')) return ok({ invitationId: 'i9', expiresAtUtc: '2026-10-28T08:00:00Z' }, 202)
    if (invitation && request.method() === 'DELETE') {
      invitations.splice(invitations.indexOf(invitation), 1)
      return route.fulfill({ status: 204, headers: cors })
    }
    return route.fulfill({ status: 404, headers: cors })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()

  const frame = page.frameLocator('iframe')
  const directory = frame.getByRole('list', { name: 'Members' })
  const row = (email: string) => directory.getByRole('listitem').filter({ hasText: email })
  await expect(directory.getByRole('listitem')).toHaveCount(3)
  await expect(row('admin@example.test')).toContainText('Ada Admin')
  await expect(row('admin@example.test')).toContainText('You')
  await expect(row('admin@example.test')).toContainText('Organization admin')
  // Your own row has no actions.
  await expect(row('admin@example.test').getByRole('button')).toHaveCount(0)
  await expect(row('new.hire@example.test')).toContainText('Pending activation')
  await expect(row('new.hire@example.test')).not.toContainText('You')

  // Invite: the new invitation appears under pending invitations; its code is never shown.
  await frame.getByLabel('Invitee email').fill(' colleague@example.test ')
  await frame.getByLabel('Role').selectOption('TenantAdmin')
  await frame.getByRole('button', { name: 'Send invitation' }).click()
  await expect(frame.getByText('Invitation sent to colleague@example.test.')).toBeVisible()
  expect(calls.at(-1)).toBe(`POST /auth/tenants/${tenantId}/invitations {"email":"colleague@example.test","role":"TenantAdmin"}`)
  const pending = frame.getByRole('list', { name: 'Pending invitations' })
  await expect(pending.getByRole('listitem')).toHaveCount(2)
  await expect(pending.getByRole('listitem').first()).toContainText('colleague@example.test')
  await expect(pending.getByRole('listitem').first()).toContainText('Organization admin')
  await expect(pending.getByRole('listitem').first()).toContainText('Invited by admin@example.test')
  await expect(frame.getByLabel('Invitee email')).toHaveValue('')
  await expect(frame.getByLabel('Role')).toHaveValue('Employee')
  await frame.getByLabel('Invitee email').fill('taken@example.test')
  await frame.getByRole('button', { name: 'Send invitation' }).click()
  await expect(frame.getByRole('alert')).toContainText('already belongs to a member of this organization')
  await expect(frame.locator('body')).not.toContainText('secret-code')

  // Role changes, including the server refusing to remove the last active admin.
  await frame.getByRole('button', { name: 'Make admin: zoe@example.test' }).click()
  await expect(frame.getByText('Changed the role of zoe@example.test.')).toBeVisible()
  await expect(row('zoe@example.test')).toContainText('Organization admin')
  refuseNextRoleChange = true
  await frame.getByRole('button', { name: 'Make employee: zoe@example.test' }).click()
  await expect(frame.getByRole('alert')).toContainText('The organization needs at least one active admin.')
  await expect(row('zoe@example.test')).toContainText('Organization admin')

  // Deactivation needs a second click; cancelling sends nothing.
  const before = calls.length
  await frame.getByRole('button', { name: 'Deactivate: zoe@example.test' }).click()
  await frame.getByRole('button', { name: 'Cancel' }).click()
  expect(calls.length).toBe(before)
  await frame.getByRole('button', { name: 'Deactivate: zoe@example.test' }).click()
  await frame.getByRole('button', { name: 'Confirm deactivation: zoe@example.test' }).click()
  await expect(frame.getByText('Deactivated zoe@example.test and signed them out everywhere.')).toBeVisible()
  await expect(row('zoe@example.test')).toContainText('Deactivated')
  await expect(row('zoe@example.test').getByRole('button', { name: /Make/ })).toHaveCount(0)
  await frame.getByRole('button', { name: 'Reactivate: zoe@example.test' }).click()
  await expect(frame.getByText('Reactivated zoe@example.test.')).toBeVisible()
  await expect(row('zoe@example.test')).toContainText('Active')

  // Pending members can get a new activation email; pending invitations can be re-sent or revoked.
  await expect(row('zoe@example.test').getByRole('button', { name: /Resend activation/ })).toHaveCount(0)
  await frame.getByRole('button', { name: 'Resend activation: new.hire@example.test' }).click()
  await expect(frame.getByText('Sent a new activation code to new.hire@example.test.')).toBeVisible()
  await frame.getByRole('button', { name: 'Resend: later@example.test' }).click()
  await expect(frame.getByText('Sent a new invitation to later@example.test; the old code no longer works.')).toBeVisible()
  await frame.getByRole('button', { name: 'Revoke: later@example.test' }).click()
  await expect(frame.getByText('Revoked the invitation for later@example.test.')).toBeVisible()
  await expect(pending.getByRole('listitem')).toHaveCount(1)
  expect(calls).toEqual(expect.arrayContaining([
    'PUT /identity/members/a2/role {"role":"TenantAdmin"}',
    'PUT /identity/members/a2/status {"active":false}',
    'PUT /identity/members/a2/status {"active":true}',
    'POST /identity/members/a3/activation',
    'POST /identity/members/invitations/i1/resend',
    'DELETE /identity/members/invitations/i1'
  ]))

  // Member groups: add, refuse a duplicate name, assign (yourself too), refuse deleting a used group, rename, clear.
  const groupList = frame.getByRole('list', { name: 'Member groups' })
  await expect(frame.getByText('No groups yet')).toBeVisible()
  await frame.getByPlaceholder('Group name').fill('Outsource')
  await frame.getByRole('button', { name: 'Add group' }).click()
  await expect(frame.getByText('Added the group Outsource.')).toBeVisible()
  await expect(frame.getByPlaceholder('Group name')).toHaveValue('')
  await expect(groupList.getByRole('listitem')).toHaveCount(1)
  await expect(groupList.getByRole('listitem')).toContainText('Members: 0')
  await frame.getByPlaceholder('Group name').fill('outsource')
  await frame.getByRole('button', { name: 'Add group' }).click()
  await expect(frame.getByRole('alert')).toContainText('A group with this name already exists.')
  await expect(frame.getByPlaceholder('Group name')).toHaveValue('outsource')
  await frame.getByLabel('Group: zoe@example.test').selectOption({ label: 'Outsource' })
  await expect(frame.getByText('Moved zoe@example.test to a new group.')).toBeVisible()
  await expect(frame.getByLabel('Group: zoe@example.test')).toHaveValue('g1')
  await frame.getByLabel('Group: admin@example.test').selectOption({ label: 'Outsource' })
  await expect(frame.getByText('Moved admin@example.test to a new group.')).toBeVisible()
  await expect(groupList.getByRole('listitem')).toContainText('Members: 2')
  await frame.getByRole('button', { name: 'Delete: Outsource' }).click()
  await expect(frame.getByRole('alert')).toContainText('This group still has members.')
  await groupList.getByLabel('Group name').fill('Outsourced')
  await groupList.getByRole('button', { name: 'Save' }).click()
  await expect(frame.getByText('Renamed the group to Outsourced.')).toBeVisible()
  await frame.getByLabel('Group: zoe@example.test').selectOption({ label: 'No group' })
  await expect(frame.getByLabel('Group: zoe@example.test')).toHaveValue('')

  // Once groups exist the invite form offers them, and the pending invitation shows its group.
  await frame.getByLabel('Invitee email').fill('contractor@example.test')
  await frame.locator('.invite-form').getByLabel('Group', { exact: true }).selectOption({ label: 'Outsourced' })
  await frame.getByRole('button', { name: 'Send invitation' }).click()
  await expect(frame.getByText('Invitation sent to contractor@example.test.')).toBeVisible()
  await expect(pending.getByRole('listitem').filter({ hasText: 'contractor@example.test' })).toContainText('Outsourced')
  await expect(frame.locator('.invite-form').getByLabel('Group', { exact: true })).toHaveValue('')
  expect(calls).toEqual(expect.arrayContaining([
    'POST /identity/members/groups {"name":"Outsource"}',
    'PUT /identity/members/a2/group {"groupId":"g1"}',
    'PUT /identity/members/a1/group {"groupId":"g1"}',
    'PUT /identity/members/groups/g1 {"name":"Outsourced"}',
    'PUT /identity/members/a2/group {"groupId":null}',
    `POST /auth/tenants/${tenantId}/invitations {"email":"contractor@example.test","role":"Employee","groupId":"g1"}`
  ]))

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 820 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('tenant admin renames the organization and sets its logo, and the Shell header follows', async ({ page }) => {
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3104',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5WQAAAAASUVORK5CYII=', 'base64')
  const organization = { name: 'Acme', organizationCode: 'ACME123', logoUrl: null as string | null }
  const changes: string[] = []
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const url = new URL(request.url())
    // The Shell (3100) and the remote (3104) both call these routes.
    const headers = { ...cors, 'access-control-allow-origin': request.headers().origin || 'http://127.0.0.1:3100' }
    const ok = (result: unknown) => route.fulfill({ status: 200, headers, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers })
    const path = url.pathname.replace('/api/v1', '')
    if (path === '/auth/sign-in') return ok({ session: {
      accessToken: 'admin-token', refreshToken: 'admin-refresh', mustChangePassword: false, tenantId, role: 'TenantAdmin',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [] })
    if (path === `/identity/tenants/${tenantId}/password-policy`)
      return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (path === '/identity/profile/me') return ok({ email: 'admin@example.test', firstName: 'Ada', lastName: 'Admin', photoUrl: null })
    if (path === '/identity/members' || path === '/identity/members/invitations' || path === '/identity/members/groups') return ok([])
    if (path === '/identity/organization') {
      if (request.method() === 'PUT') {
        changes.push(`rename ${request.postDataJSON().name}`)
        organization.name = request.postDataJSON().name.trim()
      }
      return ok(organization)
    }
    if (path === '/identity/organization/logo') {
      if (request.method() === 'GET') return route.fulfill({ status: 200, headers, contentType: 'image/png', body: png })
      if (request.method() === 'POST') {
        changes.push(`upload ${(request.postDataBuffer()?.includes('name="logo"')) ? 'logo' : 'missing'}`)
        organization.logoUrl = '/api/v1/identity/organization/logo?v=1'
      } else {
        changes.push('remove')
        organization.logoUrl = null
      }
      return ok(organization)
    }
    return route.fulfill({ status: 404, headers })
  })

  await page.goto('/')
  await waitForHydration(page)
  const brand = page.locator('.shell-header .brand')
  await expect(brand).toHaveText('Aiyara Timesheet')
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(brand).toHaveText('Acme')
  await expect(brand.locator('img')).toHaveCount(0)
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()

  const frame = page.frameLocator('iframe')
  const panel = frame.getByRole('region', { name: 'Organization' })
  await expect(panel.getByLabel('Organization name')).toHaveValue('Acme')
  await expect(panel.getByRole('button', { name: 'Save name' })).toBeDisabled()
  await panel.getByLabel('Organization name').fill('  Acme Studio  ')
  await panel.getByRole('button', { name: 'Save name' }).click()
  await expect(frame.getByText('Organization name saved.')).toBeVisible()
  await expect(brand).toHaveText('Acme Studio')

  await panel.locator('input[type=file]').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png })
  await expect(frame.getByText('Logo updated.')).toBeVisible()
  await expect(panel.getByRole('img', { name: 'Logo' })).toHaveAttribute('src', /^blob:/)
  await expect(brand.locator('img')).toHaveAttribute('src', /^blob:/)
  await expect(panel.getByText('Replace logo')).toBeVisible()

  await panel.getByRole('button', { name: 'Remove logo' }).click()
  await expect(frame.getByText('Logo removed.')).toBeVisible()
  await expect(brand.locator('img')).toHaveCount(0)
  await expect(panel.getByText('No logo yet')).toBeVisible()
  expect(changes).toEqual(['rename   Acme Studio  ', 'upload logo', 'remove'])

  await page.locator('.profile-trigger').click()
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(brand).toHaveText('Aiyara Timesheet')
})

test('invitation page accepts a code and points to activation, and explains a bad code', async ({ page }) => {
  const codes: unknown[] = []
  await page.route('**/api/v1/invitations/accept', route => {
    const body = route.request().postDataJSON()
    codes.push(body)
    if (body.code === 'expired-code') return route.fulfill({ status: 400, contentType: 'application/json',
      body: JSON.stringify({ statusCode: 400, statusMessage: 'Bad Request', errorCode: 'INVALIDINVITATION' }) })
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      tenantId, accountId: 'a2', membershipId: 'm2', role: 'Employee', status: 'PendingActivation' })) })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByRole('link', { name: 'Have an invitation code?' }).click()
  await expect(page.getByRole('heading', { name: 'Accept your invitation' })).toBeVisible()
  await waitForHydration(page)

  await page.getByLabel('Invitation code from email').fill('expired-code')
  await page.getByRole('button', { name: 'Accept invitation' }).click()
  await expect(page.getByRole('alert')).toContainText('invalid, already used, or expired')

  await page.getByLabel('Invitation code from email').fill('  fresh-code  ')
  await page.getByRole('button', { name: 'Accept invitation' }).click()
  await expect(page.getByRole('heading', { name: 'Invitation accepted' })).toBeVisible()
  expect(codes).toEqual([{ code: 'expired-code' }, { code: 'fresh-code' }])
  await page.getByRole('link', { name: 'Go to activation' }).click()
  await expect(page).toHaveURL(/\/activate$/)
})

test('an account that already has a password joins from the invitation page without activation', async ({ page }) => {
  await page.route('**/api/v1/invitations/accept', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify(apiOk({ tenantId, accountId: 'a3', membershipId: 'm3', role: 'Employee', status: 'Active' })) }))

  await page.goto('/accept-invitation')
  await waitForHydration(page)
  await page.getByLabel('Invitation code from email').fill('member-code')
  await page.getByRole('button', { name: 'Accept invitation' }).click()

  await expect(page.getByRole('heading', { name: 'Invitation accepted' })).toBeVisible()
  await expect(page.getByText('Sign in with your email and your existing password.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Go to activation' })).toHaveCount(0)
  await page.getByRole('link', { name: 'Go to sign in' }).click()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

test('platform admin creates an organization through the authenticated platform route', async ({ page }) => {
  let createRequest: { authorization: string; body: unknown } | undefined
  let publicSignupCalled = false
  await page.route('**/api/v1/tenants', route => {
    publicSignupCalled = true
    return route.fulfill({ status: 500 })
  })
  await page.route('**/api/v1/platform/organizations**', async route => {
    if (route.request().method() === 'POST') {
      createRequest = { authorization: route.request().headers().authorization || '', body: route.request().postDataJSON() }
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(apiOk({
        tenantId: 'c99358d1-e01f-4dfd-9693-38da8883562d', organizationCode: 'NEWO123',
        name: 'New Org', slug: 'new-org', status: 'Active'
      })) })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify(apiOk({ totalCount: 0, page: 1, pageSize: 25, items: [] })) })
  })
  await signInAs(page, 'PlatformAdmin')
  await page.goto('/sys-admin/organizations')

  await page.getByRole('button', { name: 'Add organization' }).click()
  await page.getByLabel('Organization name').fill('New Org')
  await page.getByLabel('Organization URL name').fill('new-org')
  await page.getByLabel('Organization administrator email').fill('owner@new.test')
  await page.getByRole('button', { name: 'Create organization and send activation email' }).click()

  await expect(page.getByText('Organization created. Code: NEWO123.')).toBeVisible()
  expect(createRequest).toEqual({
    authorization: 'Bearer PlatformAdmin-token',
    body: { name: 'New Org', slug: 'new-org', adminEmail: 'owner@new.test' }
  })
  expect(publicSignupCalled).toBe(false)
})

const auditEntry = (overrides: Record<string, unknown>) => ({
  id: crypto.randomUUID(), createdAtUtc: '2026-10-12T03:00:00Z', action: 'organization.updated',
  tenantId: 'c99358d1-e01f-4dfd-9693-38da8883562d', organizationName: 'Acme Studio', organizationCode: 'ACME123',
  actorEmail: 'operator@platform.test', reason: null, beforeJson: null, afterJson: null, ...overrides
})

test('platform admin overview shows organization counts, newest organizations, and recent activity', async ({ page }) => {
  await page.route('**/api/v1/platform/overview', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      organizationCount: 3, activeCount: 2, suspendedCount: 1, awaitingAdministratorCount: 1,
      newestOrganizations: [{ tenantId: 'c99358d1-e01f-4dfd-9693-38da8883562d', organizationCode: 'ACME123',
        name: 'Acme Studio', slug: 'acme-studio', status: 'Active', createdAtUtc: '2026-10-01T00:00:00Z', admins: [] }],
      recentActivity: [
        auditEntry({ reason: 'Rebrand', beforeJson: '{"Name":"Acme"}', afterJson: '{"Name":"Acme Studio"}' }),
        auditEntry({ action: 'platform_admin.sign_in_failed', tenantId: '00000000-0000-0000-0000-000000000001',
          organizationName: 'Aiyara Platform', organizationCode: 'AIYA001', actorEmail: null,
          afterJson: '{"Email":"<b>x</b>@evil.test"}' })
      ]
    })) }))
  await signInAs(page, 'PlatformAdmin')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'System admin' }).click()

  await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible()
  const stats = page.locator('.sysadmin-stats')
  await expect(stats).toContainText('Organizations3')
  await expect(stats).toContainText('Suspended1')
  await expect(stats).toContainText('Without an active administrator1')
  await expect(page.getByRole('link', { name: 'Acme Studio' }).first()).toHaveAttribute('href',
    '/sys-admin/organizations/c99358d1-e01f-4dfd-9693-38da8883562d')
  const activity = page.getByRole('region', { name: 'Recent activity' })
  await expect(activity).toContainText('Organization details changed')
  await expect(activity).toContainText('Name: Acme → Acme Studio')
  await expect(activity).toContainText('Reason: Rebrand')
  await expect(activity).toContainText('System administrator sign-in failed')
  await expect(activity).toContainText('Unknown account')
  // Audit values are rendered as text, never as markup.
  await expect(activity).toContainText('Email: <b>x</b>@evil.test')
  await expect(activity.locator('b')).toHaveCount(0)
})

test('platform audit log sends its filters and pages through results', async ({ page }) => {
  const requests: URL[] = []
  await page.route('**/api/v1/platform/audit**', async route => {
    const url = new URL(route.request().url())
    requests.push(url)
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      totalCount: 30, page: Number(url.searchParams.get('page')), pageSize: 25,
      items: [auditEntry({ action: 'organization.suspended', reason: 'Past due' })]
    })) })
  })
  await signInAs(page, 'PlatformAdmin')
  await page.goto('/sys-admin/audit?tenantId=c99358d1-e01f-4dfd-9693-38da8883562d')

  await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible()
  await expect(page.getByText('30 entries')).toBeVisible()
  expect(requests[0].searchParams.get('tenantId')).toBe('c99358d1-e01f-4dfd-9693-38da8883562d')
  await page.getByLabel('Actor email').fill('operator@')
  await page.getByLabel('Action').selectOption('organization.suspended')
  await page.getByLabel('From').fill('2026-10-01')
  await page.getByLabel('This organization only').uncheck()
  await page.getByRole('button', { name: 'Filter', exact: true }).click()
  await expect.poll(() => requests.length).toBe(2)
  const filtered = requests[1].searchParams
  expect(filtered.get('actor')).toBe('operator@')
  expect(filtered.get('action')).toBe('organization.suspended')
  expect(filtered.get('fromUtc')).toMatch(/^2026-09-30T|^2026-10-01T/)
  expect(filtered.get('tenantId')).toBeNull()
  expect(filtered.get('page')).toBe('1')
  await page.getByRole('button', { name: 'Next' }).click()
  await expect.poll(() => requests.length).toBe(3)
  expect(requests[2].searchParams.get('page')).toBe('2')
  expect(requests[2].searchParams.get('actor')).toBe('operator@')
})

test('organization detail shows members, administrators, and its own history', async ({ page }) => {
  const tenant = 'c99358d1-e01f-4dfd-9693-38da8883562d'
  let auditTenant: string | null = null
  await page.route(`**/api/v1/platform/organizations/${tenant}`, route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      tenantId: tenant, organizationCode: 'ACME123', name: 'Acme Studio', slug: 'acme-studio',
      status: 'Suspended', createdAtUtc: '2026-10-01T00:00:00Z', activeMemberCount: 7, pendingMemberCount: 2,
      admins: [{ email: 'admin@acme.test', status: 'Active' }]
    })) }))
  await page.route('**/api/v1/platform/audit**', route => {
    auditTenant = new URL(route.request().url()).searchParams.get('tenantId')
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      totalCount: 1, page: 1, pageSize: 10, items: [auditEntry({ action: 'organization.suspended', reason: 'Past due' })]
    })) })
  })
  await signInAs(page, 'PlatformAdmin')
  await page.goto(`/sys-admin/organizations/${tenant}`)

  await expect(page.getByRole('heading', { level: 1, name: 'Acme Studio' })).toBeVisible()
  await expect(page.locator('.sysadmin-stats')).toContainText('Active members7')
  await expect(page.locator('.sysadmin-stats')).toContainText('Pending activation2')
  await expect(page.getByText('ACME123')).toBeVisible()
  await expect(page.getByText('admin@acme.test')).toBeVisible()
  const history = page.getByRole('region', { name: 'History' })
  await expect(history).toContainText('Organization suspended')
  await expect(history).toContainText('Reason: Past due')
  expect(auditTenant).toBe(tenant)
  await expect(page.getByRole('link', { name: 'Full history' })).toHaveAttribute('href',
    `/sys-admin/audit?tenantId=${tenant}`)
})

test('platform admin roster invites, deactivates, and reactivates admins but never offers self-deactivation', async ({ page }) => {
  const roster = [
    { email: 'platform@aiyara.test', firstName: 'Ploy', lastName: 'Root', status: 'Active', createdAtUtc: '2026-10-01T00:00:00Z', isCurrentUser: true, mfaEnabled: true },
    { email: 'second@aiyara.test', firstName: '', lastName: '', status: 'Active', createdAtUtc: '2026-10-05T00:00:00Z', isCurrentUser: false, mfaEnabled: true },
    { email: 'gone@aiyara.test', firstName: 'Former', lastName: 'Admin', status: 'Deactivated', createdAtUtc: '2026-10-02T00:00:00Z', isCurrentUser: false, mfaEnabled: false }
  ]
  const requests: { method: string; path: string; body: unknown }[] = []
  await page.route('**/api/v1/platform/admins**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (request.method() === 'GET') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk(roster)) })
    const body = request.postDataJSON()
    requests.push({ method: request.method(), path, body })
    if (body.email === 'owner@acme.test') return route.fulfill({ status: 409, contentType: 'application/json',
      body: JSON.stringify({ statusCode: 409, statusMessage: 'The request could not be completed.', errorCode: 'CONFLICT' }) })
    return route.fulfill({ status: request.method() === 'POST' ? 202 : 200, contentType: 'application/json', body: JSON.stringify(apiOk({})) })
  })
  page.on('dialog', dialog => dialog.accept())
  await signInAs(page, 'PlatformAdmin')
  await page.goto('/sys-admin/admins')

  await expect(page.getByRole('navigation', { name: 'System administration' }).getByRole('link', { name: 'Platform admins' }))
    .toHaveClass(/is-active/)
  const rows = page.getByRole('region', { name: 'Roster' }).getByRole('listitem')
  await expect(rows).toHaveCount(3)
  await expect(rows.nth(0)).toContainText('Ploy Root')
  await expect(rows.nth(0)).toContainText('You')
  await expect(rows.nth(0).getByRole('button')).toHaveCount(0)
  await expect(rows.nth(1)).toContainText('second@aiyara.test')
  await expect(rows.nth(2)).toContainText('Deactivated')
  await expect(rows.nth(1)).toContainText('MFA on')
  await expect(rows.nth(2).getByRole('button', { name: 'Reset MFA' })).toHaveCount(0)

  await page.getByLabel('Reason (optional) — second@aiyara.test').fill('Left the team')
  await rows.nth(1).getByRole('button', { name: 'Deactivate' }).click()
  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('Deactivated.')
  await rows.nth(2).getByRole('button', { name: 'Reactivate' }).click()
  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('Reactivated.')
  await rows.nth(1).getByRole('button', { name: 'Reset MFA' }).click()
  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('MFA reset. They set up an authenticator again at their next sign-in.')

  const invite = page.getByRole('region', { name: 'Invite a platform admin' })
  await invite.getByLabel('Email').fill('owner@acme.test')
  await invite.getByRole('button', { name: 'Send invitation' }).click()
  await expect(page.getByRole('alert')).toHaveText('This email is already a platform admin or belongs to a customer organization.')
  await invite.getByLabel('Email').fill('new@aiyara.test')
  await invite.getByRole('button', { name: 'Send invitation' }).click()
  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('Invitation sent.')

  expect(requests).toEqual([
    { method: 'PUT', path: '/api/v1/platform/admins/status', body: { email: 'second@aiyara.test', active: false, reason: 'Left the team' } },
    { method: 'PUT', path: '/api/v1/platform/admins/status', body: { email: 'gone@aiyara.test', active: true, reason: null } },
    { method: 'POST', path: '/api/v1/platform/admins/mfa-reset', body: { email: 'second@aiyara.test', reason: null } },
    { method: 'POST', path: '/api/v1/platform/admins', body: { email: 'owner@acme.test' } },
    { method: 'POST', path: '/api/v1/platform/admins', body: { email: 'new@aiyara.test' } }
  ])
})

test('organization detail demotes or removes an administrator and explains the last-administrator rule', async ({ page }) => {
  const tenant = 'c99358d1-e01f-4dfd-9693-38da8883562d'
  const changes: { kind: string; body: unknown }[] = []
  await page.route(`**/api/v1/platform/organizations/${tenant}`, route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({
      tenantId: tenant, organizationCode: 'ACME123', name: 'Acme Studio', slug: 'acme-studio',
      status: 'Active', createdAtUtc: '2026-10-01T00:00:00Z', activeMemberCount: 3, pendingMemberCount: 0,
      admins: [{ email: 'owner@acme.test', status: 'Active' }, { email: 'second@acme.test', status: 'Active' },
        { email: 'former@acme.test', status: 'Deactivated' }]
    })) }))
  await page.route(`**/api/v1/platform/organizations/${tenant}/administrators/*`, route => {
    const kind = new URL(route.request().url()).pathname.split('/').at(-1)!
    const body = route.request().postDataJSON()
    changes.push({ kind, body })
    return body.email === 'owner@acme.test'
      ? route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({
          statusCode: 409, statusMessage: 'The request could not be completed.', errorCode: 'LAST_ADMINISTRATOR' }) })
      : route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk({})) })
  })
  await page.route('**/api/v1/platform/audit**', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify(apiOk({ totalCount: 0, page: 1, pageSize: 10, items: [] })) }))
  page.on('dialog', dialog => dialog.accept())
  await signInAs(page, 'PlatformAdmin')
  await page.goto(`/sys-admin/organizations/${tenant}`)

  const admins = page.locator('.sysadmin-card').filter({ hasText: 'Administrators' }).getByRole('listitem')
  await expect(admins).toHaveCount(3)
  await expect(admins.nth(2)).toContainText('Removed')
  await expect(admins.nth(2).getByRole('button')).toHaveCount(0)

  await page.getByLabel('Reason (optional) — second@acme.test').fill('Now an employee')
  await admins.nth(1).getByRole('button', { name: 'Make employee' }).click()
  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('Administrator demoted.')
  await admins.nth(0).getByRole('button', { name: 'Remove from organization' }).click()
  await expect(page.getByRole('alert')).toHaveText('An organization needs at least one active administrator. Invite a new one first.')

  expect(changes).toEqual([
    { kind: 'demote', body: { email: 'second@acme.test', reason: 'Now an employee' } },
    { kind: 'remove', body: { email: 'owner@acme.test', reason: null } }
  ])
})

test('security page loads and saves the platform password policy', async ({ page }) => {
  let saved: unknown
  await page.route('**/api/v1/platform/security/password-policy', async route => {
    const current = { minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: false }
    if (route.request().method() === 'PUT') {
      saved = route.request().postDataJSON()
      const { reason: _, ...policy } = saved as Record<string, unknown>
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk(policy)) })
    }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiOk(current)) })
  })
  await signInAs(page, 'PlatformAdmin')
  await page.goto('/sys-admin/security')

  const minimum = page.getByLabel('Minimum length (12–128)')
  await expect(minimum).toHaveValue('12')
  await expect(page.getByLabel('Require a symbol')).not.toBeChecked()
  await minimum.fill('16')
  await page.getByLabel('Require a symbol').check()
  await page.getByLabel('Reason (optional)').fill('Annual review')
  await page.getByRole('button', { name: 'Save policy' }).click()

  await expect(page.getByRole('status').filter({ hasText: /./ })).toHaveText('Policy saved.')
  expect(saved).toEqual({ minimumLength: 16, expiryDays: 180, requireUppercase: true, requireLowercase: true,
    requireDigit: true, requireSymbol: true, reason: 'Annual review' })
  await expect(page.getByRole('navigation', { name: 'System administration' }).getByRole('link', { name: 'Security' }))
    .toHaveClass(/is-active/)
})

test('console pages stay restricted for non platform admins', async ({ page }) => {
  await signInAs(page, 'TenantAdmin')
  for (const path of ['/sys-admin', '/sys-admin/audit', '/sys-admin/admins', '/sys-admin/security', '/sys-admin/organizations/c99358d1-e01f-4dfd-9693-38da8883562d']) {
    await page.goto(path)
    await expect(page.getByRole('heading', { name: 'Access restricted' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'System administration' })).toHaveCount(0)
  }
})

async function openReporting(page: import('@playwright/test').Page, role: 'TenantAdmin' | 'Employee') {
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3103',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  const state = {
    flipQueuedToReady: false,
    runQueries: [] as URLSearchParams[],
    created: [] as unknown[],
    renamed: [] as unknown[],
    memberCalls: 0,
    timesheetQueries: [] as string[],
    myQueries: [] as string[],
    regenerated: [] as string[],
    scheduleRequests: [] as Record<string, unknown>[],
    schedules: [] as Record<string, unknown>[]
  }
  const definitions = [
    { id: 'def-month', kind: 'Monthly', format: 'Pdf', name: 'Monthly timesheet' },
    { id: 'def-week', kind: 'Weekly', format: 'Xlsx', name: 'Weekly hours' }
  ]
  const run = (id: string, overrides: Record<string, unknown>) => ({
    id, definitionId: 'def-month', subjectUserId: null, status: 'Succeeded', statusReason: null, blockedMonth: null,
    periodStartUtc: '2026-08-31T17:00:00Z', periodEndUtc: '2026-09-30T17:00:00Z', attemptCount: 1,
    createdAtUtc: '2026-10-01T02:00:00Z', completedAtUtc: '2026-10-01T02:01:00Z', signedVersion: null,
    kind: 'Monthly', format: 'Pdf', name: 'Monthly timesheet', ...overrides
  })
  const runs = () => [
    run('run-queued', { definitionId: 'def-week', kind: 'Weekly', format: 'Xlsx', name: 'Weekly hours',
      status: state.flipQueuedToReady ? 'Succeeded' : 'Queued', completedAtUtc: state.flipQueuedToReady ? '2026-10-12T03:00:00Z' : null }),
    run('run-blocked', { status: 'Queued', statusReason: 'SOURCE_MONTH_NOT_LOCKED', blockedMonth: '2026-09',
      subjectUserId: 'member-somchai', completedAtUtc: null }),
    run('run-failed', { status: 'Failed', statusReason: 'GENERATION_FAILED' }),
    run('run-ready', { signedVersion: 2, subjectUserId: 'member-gone' }),
    run('run-group', { subjectGroupName: 'Outsource' })
  ]
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    const ok = (result: unknown, status = 200) => route.fulfill({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    if (url.pathname.endsWith('/auth/sign-in')) return ok({ session: {
      accessToken: 'reports-token', refreshToken: 'reports-refresh', mustChangePassword: false, tenantId, role,
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (url.pathname.endsWith('/identity/members')) {
      state.memberCalls++
      return ok([
        { id: 'member-anong', firstName: 'Anong', lastName: 'Admin', email: 'admin@acme.test', role: 'TenantAdmin', status: 'Active',
          groupId: 'g-staff', groupName: 'Permanent staff' },
        { id: 'member-somchai', firstName: 'Somchai', lastName: 'Staff', email: 'somchai@acme.test', role: 'Employee', status: 'Active',
          groupId: 'g-out', groupName: 'Outsource' },
        { id: 'member-new', firstName: '', lastName: '', email: 'new@acme.test', role: 'Employee', status: 'PendingActivation',
          groupId: null, groupName: null }
      ])
    }
    if (url.pathname.endsWith('/identity/profile/me')) return ok({ email: 'mali@acme.test', firstName: 'Mali', lastName: 'Member', photoUrl: null })
    if (url.pathname.endsWith('/timesheets/me/timesheet')) {
      state.myQueries.push(url.searchParams.toString())
      return ok({ ownerId: 'member-mali', year: 2026, month: 10, locked: false,
        entries: [{ id: 'm1', date: '2026-10-07', startTime: '09:00:00', endTime: '11:30:00', durationMinutes: 150, taskName: 'Sprint planning',
          detail: null, notes: null, projectName: 'Apollo', categoryName: null }],
        leaves: [], holidays: [], workingDays: [1, 2, 3, 4, 5] })
    }
    if (url.pathname.includes('/timesheets/members/')) {
      state.timesheetQueries.push(`${url.pathname.replace(/^.*\/timesheets/, '')}?${url.searchParams}`)
      if (role !== 'TenantAdmin') return route.fulfill({ status: 403, headers: cors })
      if (url.pathname.endsWith('/members/summary')) return ok({ year: 2026, month: 10, locked: false, members: [
        { ownerId: 'member-somchai', entryCount: 3, totalMinutes: 450, leaveDays: 1 },
        { ownerId: 'member-gone', entryCount: 1, totalMinutes: 60, leaveDays: 0 }
      ] })
      if (url.pathname.endsWith('/members/member-somchai/timesheet')) return ok({ ownerId: 'member-somchai', year: 2026, month: 10, locked: false,
        entries: [
          { id: 'e1', date: '2026-10-05', startTime: '09:00:00', endTime: '12:00:00', durationMinutes: 180, taskName: 'Design review',
            detail: 'Checkout flow', notes: null, projectName: 'Apollo', categoryName: 'Design' },
          { id: 'e2', date: '2026-10-05', startTime: '13:00:00', endTime: '15:30:00', durationMinutes: 150, taskName: 'Pairing',
            detail: null, notes: null, projectName: 'Apollo', categoryName: null },
          { id: 'e3', date: '2026-10-06', startTime: '22:00:00', endTime: '00:00:00', durationMinutes: 120, taskName: 'Night support',
            detail: null, notes: 'On call', projectName: null, categoryName: 'Support' }
        ],
        leaves: [{ id: 'l1', date: '2026-10-09', kind: 'Vacation', notes: null }],
        // A Monday-to-Saturday organization; the night support shift fell on a holiday.
        holidays: [{ date: '2026-10-06', name: 'Memorial day' }], workingDays: [1, 2, 3, 4, 5, 6] })
      return ok({ ownerId: 'x', year: 2026, month: 10, locked: true, entries: [], leaves: [], holidays: [] })
    }
    if (url.pathname.endsWith('/reports/context')) return ok({ isAdmin: role === 'TenantAdmin', timeZoneId: 'Asia/Bangkok' })
    if (url.pathname.endsWith('/reports/definitions')) return ok(definitions)
    if (url.pathname.match(/\/reports\/definitions\/[^/]+$/) && request.method() === 'PUT') {
      state.renamed.push(request.postDataJSON())
      return ok(definitions[0])
    }
    const regenerate = url.pathname.match(/\/reports\/runs\/([^/]+)\/regenerate$/)
    if (regenerate && request.method() === 'POST') {
      state.regenerated.push(regenerate[1]!)
      return ok({ id: 'run-regenerated' }, 202)
    }
    if (url.pathname.endsWith('/reports/dashboard')) return ok({ total: 4, ready: 1, queued: 1, blocked: 1, failed: 1 })
    if (url.pathname.endsWith('/reports/schedules') && request.method() === 'POST') {
      const body = request.postDataJSON()
      state.scheduleRequests.push(body)
      if (state.schedules.some(x => x.subjectGroupId === body.groupId))
        return route.fulfill({ status: 409, headers: cors, contentType: 'application/json',
          body: JSON.stringify({ statusCode: 409, statusMessage: 'Conflict', errorCode: 'SCHEDULE_EXISTS' }) })
      state.schedules.push({ id: `schedule-${state.schedules.length + 1}`, reportDefinitionId: body.reportDefinitionId,
        timeZoneId: body.timeZoneId, localTime: body.localTime, nextFireAtUtc: '2026-11-01T17:15:00Z', lastFireAtUtc: null,
        kind: 'Monthly', name: 'Monthly timesheet', subjectGroupId: body.groupId,
        subjectGroupName: body.groupId === 'g-out' ? 'Outsource' : null })
      return ok(state.schedules.at(-1), 201)
    }
    if (url.pathname.endsWith('/reports/schedules')) return ok(state.schedules)
    if (url.pathname.endsWith('/reports/retention')) return ok({ years: 7 })
    if (url.pathname.endsWith('/reports/runs')) {
      if (request.method() === 'POST') {
        state.created.push(request.postDataJSON())
        return ok({ id: 'run-new' }, 202)
      }
      state.runQueries.push(url.searchParams)
      const status = url.searchParams.get('status')
      const subjects = url.searchParams.getAll('subjectUserId')
      const items = runs().filter(item => (!status || item.status === status) &&
        (!subjects.length || subjects.includes(item.subjectUserId ?? '')))
      return ok({ totalCount: items.length, page: 1, pageSize: 10, items })
    }
    return route.fulfill({ status: 404, headers: cors })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('reports@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Reports.*View reports/ }).click()
  const frame = page.frameLocator('iframe')
  await expect(frame.getByRole('heading', { name: 'Reports and documents', level: 1 })).toBeVisible()
  return { frame, state }
}

test('reporting shows names and reasons, requests a member report by calendar period, and refreshes progress', async ({ page }) => {
  const { frame, state } = await openReporting(page, 'TenantAdmin')

  await expect(frame.locator('.summary').first()).toContainText('Needs attention2')
  const history = frame.getByRole('region', { name: 'Report history' }).locator('table')
  await expect(history).toContainText('Somchai Staff')
  await expect(history).toContainText('Former member')
  await expect(history).toContainText('Whole organization')
  await expect(history).toContainText('Group: Outsource')
  await expect(history).toContainText('Waiting for 2026-09 to be locked')
  await expect(history).toContainText('Request it again')
  await expect(history).toContainText('Signed v2')
  await expect(history).toContainText('Sep 1, 2026 – Sep 30, 2026')
  await expect(history).not.toContainText('member-')

  // Monthly report: month + year pickers, a member subject, and an exact period preview.
  const year = new Date().getFullYear()
  await frame.locator('.request-form').getByLabel(/^Month/).selectOption('1')
  await frame.locator('.request-form').getByLabel(/^Year/).selectOption(String(year))
  await expect(frame.getByLabel('Report on', { exact: true }).locator('option'))
    .toHaveText(['Whole organization', 'Outsource', 'Permanent staff', 'Anong Admin', 'Somchai Staff'])
  await frame.getByLabel('Report on', { exact: true }).selectOption({ label: 'Somchai Staff' })
  await expect(frame.locator('.preview')).toHaveText(`Period: Jan 1, ${year} – Jan 31, ${year} (Asia/Bangkok)`)
  await frame.getByRole('button', { name: 'Generate report' }).click()
  await expect.poll(() => state.created).toEqual([{
    definitionId: 'def-month', subjectUserId: 'member-somchai',
    periodStartUtc: `${year - 1}-12-31T17:00:00.000Z`, periodEndUtc: `${year}-01-31T17:00:00.000Z`
  }])

  // A member group report sends the group's name and its current members.
  await frame.getByLabel('Report on', { exact: true }).selectOption({ label: 'Outsource' })
  await frame.getByRole('button', { name: 'Generate report' }).click()
  await expect.poll(() => state.created.at(-1)).toEqual({
    definitionId: 'def-month', subjectUserId: null, groupName: 'Outsource', groupMemberIds: ['member-somchai'],
    periodStartUtc: `${year - 1}-12-31T17:00:00.000Z`, periodEndUtc: `${year}-01-31T17:00:00.000Z`
  })
  await frame.getByLabel('Report on', { exact: true }).selectOption({ label: 'Whole organization' })

  // Weekly report: any day picks its Monday-to-Sunday week.
  await frame.locator('.request-form select').first().selectOption('def-week')
  await frame.getByLabel('Week containing').fill('2026-10-14')
  await expect(frame.locator('.preview')).toHaveText('Period: Oct 12, 2026 – Oct 18, 2026 (Asia/Bangkok)')

  // Filters are sent to the API.
  await frame.locator('.filters').getByLabel('Status').selectOption('Failed')
  await expect.poll(() => state.runQueries.at(-1)?.get('status')).toBe('Failed')
  await expect(history.locator('tbody tr')).toHaveCount(1)
  await frame.locator('.filters').getByLabel('Status').selectOption('')

  // A group filter asks for its members' reports; whole-organization reports drop out.
  const groupFilter = frame.locator('.filters').getByLabel('Group', { exact: true })
  await expect(groupFilter.locator('option')).toHaveText(['All groups', 'Outsource', 'Permanent staff', 'No group'])
  await groupFilter.selectOption({ label: 'Outsource' })
  await expect.poll(() => state.runQueries.at(-1)?.getAll('subjectUserId')).toEqual(['member-somchai'])
  await expect(history.locator('tbody tr')).toHaveCount(1)
  await expect(history.locator('tbody tr')).toContainText('Somchai Staff')
  await groupFilter.selectOption({ label: 'No group' })
  await expect.poll(() => state.runQueries.at(-1)?.getAll('subjectUserId')).toEqual(['member-new'])
  await expect(frame.getByText('No reports match these filters.')).toBeVisible()
  await groupFilter.selectOption({ label: 'All groups' })
  await expect.poll(() => state.runQueries.at(-1)?.getAll('subjectUserId')).toEqual([])
  await expect(history.locator('tbody tr')).toHaveCount(5)

  // Finished reports can be generated again; queued ones cannot.
  await expect(history.locator('tbody tr').filter({ hasText: 'Waiting for 2026-09' }).getByRole('button', { name: /Regenerate/ }))
    .toHaveCount(0)
  await history.locator('tbody tr').filter({ hasText: 'Request it again' }).getByRole('button', { name: /Regenerate/ }).click()
  await expect(frame.getByText('Generating the report again; it appears as a new row in the history.')).toBeVisible()
  expect(state.regenerated).toEqual(['run-failed'])

  // While a report is queued the list refreshes itself.
  await expect(frame.getByText('Statuses refresh automatically')).toBeVisible()
  state.flipQueuedToReady = true
  await expect(history.locator('tbody tr').filter({ hasText: 'Weekly hours' }).getByRole('button', { name: 'Download' }))
    .toBeVisible({ timeout: 15_000 })

  // Settings: rename a format; monthly schedules explain their fixed time instead of asking for one.
  await frame.locator('.setting-list').getByLabel('Name').first().fill('Monthly timesheet (signed)')
  await frame.locator('.setting-list').getByRole('button', { name: 'Save' }).first().click()
  await expect.poll(() => state.renamed).toEqual([{ kind: 'Monthly', format: 'Pdf', name: 'Monthly timesheet (signed)' }])
  await expect(frame.getByText('Monthly reports run at 00:15 on the 1st of each month.')).toBeVisible()

  // Automatic reports can be scheduled for a member group, once per report and group.
  const scheduleSubject = frame.getByLabel('Automatic reports: Report on')
  await expect(scheduleSubject.locator('option')).toHaveText(['Whole organization', 'Group: Outsource', 'Group: Permanent staff'])
  await scheduleSubject.selectOption({ label: 'Group: Outsource' })
  await expect(frame.getByText('Automatic group reports cover whoever is in the group when each report is made.')).toBeVisible()
  await frame.getByRole('button', { name: 'Add schedule' }).click()
  await expect.poll(() => state.scheduleRequests.at(-1)).toMatchObject({ reportDefinitionId: 'def-month', groupId: 'g-out' })
  await expect(frame.locator('.setting-list').filter({ hasText: 'Group: Outsource' })).toBeVisible()
  await scheduleSubject.selectOption({ label: 'Group: Outsource' })
  await frame.getByRole('button', { name: 'Add schedule' }).click()
  await expect(frame.getByRole('alert')).toContainText('already scheduled for this group or the whole organization')
})

test('tenant admin reviews each member\'s monthly timesheet from Reports', async ({ page }) => {
  const { frame, state } = await openReporting(page, 'TenantAdmin')
  const section = frame.getByRole('region', { name: 'Member timesheets' })
  const table = section.getByRole('table', { name: 'Member timesheets' })
  // Admins find their own month among the members, so they get no separate My timesheet.
  await expect(section).toBeVisible()
  await expect(frame.getByRole('region', { name: 'My timesheet' })).toHaveCount(0)
  expect(state.myQueries).toEqual([])

  await expect(table.locator('tbody tr')).toHaveCount(4)
  // Busiest first; members with no entries show zero, and departed members keep their hours.
  await expect(table.locator('tbody tr').nth(0)).toContainText('Somchai Staff')
  await expect(table.locator('tbody tr').nth(0)).toContainText('7:30')
  await expect(table.locator('tbody tr').nth(1)).toContainText('Former member')
  await expect(table.locator('tbody tr').nth(1)).toContainText('1:00')
  await expect(table.locator('tbody tr').filter({ hasText: 'new@acme.test' })).toContainText('—')
  await expect(section).toContainText('Month open (may still change)')
  const month = await section.getByLabel('Month').inputValue()
  const [year, monthNumber] = month.split('-').map(Number)
  expect(state.timesheetQueries[0]).toBe(`/members/summary?year=${year}&month=${monthNumber}`)

  // Totals by group: busiest first, members in no group last, former members left out.
  const byGroup = section.getByRole('table', { name: 'Totals by group' })
  await expect(byGroup.locator('tbody tr')).toHaveCount(3)
  await expect(byGroup.locator('tbody tr').nth(0)).toContainText('Outsource17:3031')
  await expect(byGroup.locator('tbody tr').nth(1)).toContainText('Permanent staff10:0000')
  await expect(byGroup.locator('tbody tr').nth(2)).toContainText('No group10:0000')
  await expect(table.locator('tbody tr').filter({ hasText: 'Somchai Staff' })).toContainText('Outsource')
  // Filtering by a group shows only its members and hides the group totals.
  await section.getByLabel('Group', { exact: true }).selectOption({ label: 'Outsource' })
  await expect(table.locator('tbody tr')).toHaveCount(1)
  await expect(table.locator('tbody tr')).toContainText('Somchai Staff')
  await expect(byGroup).toHaveCount(0)
  await section.getByLabel('Group', { exact: true }).selectOption({ label: 'No group' })
  await expect(table.locator('tbody tr')).toHaveCount(1)
  await expect(table.locator('tbody tr')).toContainText('new@acme.test')
  await section.getByLabel('Group', { exact: true }).selectOption({ label: 'All groups' })
  await byGroup.getByRole('button', { name: 'Permanent staff' }).click()
  await expect(table.locator('tbody tr')).toHaveCount(1)
  await expect(table.locator('tbody tr')).toContainText('Anong Admin')
  await section.getByLabel('Group', { exact: true }).selectOption({ label: 'All groups' })
  await expect(table.locator('tbody tr')).toHaveCount(4)

  await section.getByRole('button', { name: 'View timesheet: Somchai Staff' }).click()
  await expect(section.getByRole('heading', { name: /Somchai Staff · / })).toBeVisible()
  await expect(section.locator('.member-totals')).toContainText('Total hours7:30')
  await expect(section.locator('.member-totals')).toContainText('Days worked2')
  await expect(section.locator('.member-totals')).toContainText('Leave days1')
  // Four Sundays and the holiday.
  await expect(section.locator('.member-totals')).toContainText('Days off5')
  const entries = section.getByRole('table', { name: 'Entries: Somchai Staff' })
  await expect(entries.locator('tbody tr')).toHaveCount(3)
  await expect(entries.locator('tbody tr').nth(0)).toContainText('09:00–12:00')
  await expect(entries.locator('tbody tr').nth(0)).toContainText('Checkout flow')
  await expect(entries.locator('tbody tr').nth(1)).toContainText('Day total 5:30')
  await expect(entries.locator('tbody tr').nth(2)).toContainText('22:00–00:00')
  await expect(entries.locator('tbody tr').nth(2)).toContainText('On call')
  await expect(entries.locator('tbody tr').nth(2).locator('.day-off')).toHaveText('Memorial day')
  await expect(entries.locator('tbody tr').nth(0).locator('.day-off')).toHaveCount(0)
  await expect(section).toContainText('Apollo5:30')
  await expect(section).toContainText('No project2:00')
  await expect(section).toContainText('Vacation')
  const daysOff = section.locator('.detail-lists').getByRole('heading', { name: 'Days off' }).locator('xpath=..')
  await expect(daysOff.getByRole('listitem')).toHaveCount(5)
  await expect(daysOff).toContainText('Memorial day')
  await expect(daysOff).toContainText('Weekly day off')

  // Both exports download the month on the spot, open month included, days off named in Remark.
  const exportGroup = section.getByRole('group', { name: 'Export the timesheet of Somchai Staff' })
  const [csvDownload] = await Promise.all([page.waitForEvent('download'), exportGroup.getByRole('button', { name: 'Export CSV' }).click()])
  expect(csvDownload.suggestedFilename()).toBe('timesheet-Somchai-Staff-2026-10.csv')
  const csv = readFileSync(await csvDownload.path(), 'utf8')
  expect(csv.charCodeAt(0)).toBe(0xFEFF)
  const csvLines = csv.slice(1).split('\r\n')
  expect(csvLines[0]).toBe('Member timesheets,Somchai Staff · October 2026')
  expect(csvLines[2]).toBe('Total hours,7.5,Days worked,2,Leave days,1,Days off,5')
  expect(csvLines[4]).toBe('Date,Start,End,Hours,Task,Detail,Notes,Project,Category,Remark')
  expect(csvLines).toContain('2026-10-04,,,,Weekly day off,,,,,Days off')
  expect(csvLines).toContain('2026-10-05,09:00,12:00,3,Design review,Checkout flow,,Apollo,Design,')
  expect(csvLines).toContain('2026-10-06,22:00,00:00,2,Night support,,On call,,Support,Memorial day')
  expect(csvLines).toContain('2026-10-09,,,,Vacation,,,,,Leave')
  // Five days off, one leave day, three entries, after five heading rows.
  expect(csvLines).toHaveLength(5 + 5 + 1 + 3)
  const [xlsxDownload] = await Promise.all([page.waitForEvent('download'), exportGroup.getByRole('button', { name: 'Export XLSX' }).click()])
  expect(xlsxDownload.suggestedFilename()).toBe('timesheet-Somchai-Staff-2026-10.xlsx')
  // Entries are stored uncompressed, so the sheet's text is readable in the raw package.
  const xlsx = readFileSync(await xlsxDownload.path())
  expect(xlsx.subarray(0, 2).toString()).toBe('PK')
  expect(xlsx.toString('utf8')).toContain('<t xml:space="preserve">Memorial day</t>')

  // Changing the month reloads the open member; going back shows everyone again.
  await section.getByLabel('Month').fill('2026-09')
  await expect.poll(() => state.timesheetQueries.at(-1)).toBe('/members/member-somchai/timesheet?year=2026&month=9')
  await section.getByRole('button', { name: /All members/ }).click()
  await expect(table.locator('tbody tr')).toHaveCount(4)

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
  }
})

test('employees request only their own reports and never load the member directory', async ({ page }) => {
  const { frame, state } = await openReporting(page, 'Employee')

  await expect(frame.getByRole('region', { name: 'Report history' }).locator('table')).toBeVisible()
  await expect(frame.getByRole('columnheader', { name: 'Person' })).toHaveCount(0)
  await expect(frame.getByLabel('Report on', { exact: true })).toHaveCount(0)
  await expect(frame.getByRole('heading', { name: 'Report settings' })).toHaveCount(0)
  await frame.getByRole('button', { name: 'Generate report' }).click()
  await expect.poll(() => state.created.length).toBe(1)
  expect((state.created[0] as { subjectUserId: unknown }).subjectUserId).toBeNull()
  expect(state.memberCalls).toBe(0)
  await expect(frame.getByRole('region', { name: 'Member timesheets' })).toHaveCount(0)
  expect(state.timesheetQueries).toEqual([])

  // Their own month shows live, before it is closed, and can be exported.
  const mine = frame.getByRole('region', { name: 'My timesheet' })
  await expect(mine.getByRole('heading', { name: /^Mali Member · / })).toBeVisible()
  await expect(mine.getByText('Month open (may still change)')).toBeVisible()
  await expect(mine.locator('.member-totals')).toContainText('Total hours2:30')
  await expect(mine.getByRole('table', { name: 'Entries: Mali Member' })).toContainText('Sprint planning')
  await expect(mine.getByRole('button', { name: /All members/ })).toHaveCount(0)
  await expect(mine.getByLabel('Group', { exact: true })).toHaveCount(0)
  expect(state.myQueries[0]).toMatch(/^year=\d{4}&month=\d{1,2}$/)
  const [download] = await Promise.all([page.waitForEvent('download'), mine.getByRole('button', { name: 'Export CSV' }).click()])
  expect(download.suggestedFilename()).toMatch(/^timesheet-Mali-Member-\d{4}-\d{2}\.csv$/)
  expect(readFileSync(await download.path(), 'utf8')).toContain('My timesheet,Mali Member · ')

  // Only the history table may scroll sideways on a phone; the page itself must not.
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(frame.getByRole('button', { name: 'Generate report' })).toBeVisible()
  }
})

test('tenant admin sets the working days from Administration and cannot clear the last one', async ({ page }) => {
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3104',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  let workingDays = [1, 2, 3, 4, 5]
  const saved: number[][] = []
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const ok = (result: unknown) => route.fulfill({ status: 200, headers: cors, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    const path = new URL(request.url()).pathname.replace('/api/v1', '')
    if (path === '/auth/sign-in') return ok({ session: {
      accessToken: 'admin-token', refreshToken: 'admin-refresh', mustChangePassword: false, tenantId, role: 'TenantAdmin',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [], workingDays })
    if (path === '/timesheets/work-week') {
      workingDays = [...request.postDataJSON().workingDays].sort()
      saved.push(workingDays)
      return ok({ workingDays })
    }
    if (path === `/identity/tenants/${tenantId}/password-policy`)
      return ok({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
    if (path === '/identity/members' || path === '/identity/members/invitations' || path === '/identity/members/groups') return ok([])
    if (path === '/identity/organization') return ok({ name: 'Acme', organizationCode: 'ACME123', logoUrl: null })
    return route.fulfill({ status: 404, headers: cors })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('link', { name: 'Administration' }).click()

  const frame = page.frameLocator('iframe')
  const panel = frame.getByRole('region', { name: 'Working days' })
  const day = (name: string) => panel.getByRole('checkbox', { name })
  await expect(day('Monday')).toBeChecked()
  await expect(day('Friday')).toBeChecked()
  await expect(day('Saturday')).not.toBeChecked()
  await expect(day('Sunday')).not.toBeChecked()

  for (const name of ['Tuesday', 'Wednesday', 'Thursday', 'Friday']) await day(name).uncheck()
  await expect(day('Monday')).toBeDisabled()
  await day('Saturday').check()
  await expect(day('Monday')).toBeEnabled()
  await panel.getByRole('button', { name: 'Save working days' }).click()
  await expect(frame.getByText('Working days saved.')).toBeVisible()
  expect(saved).toEqual([[1, 6]])

  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(day('Sunday')).toBeVisible()
  }
})

test('timesheet calendar shades the days the organization does not work', async ({ page }) => {
  const now = new Date()
  const currentMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  const firstDay = (weekday: number) => Array.from({ length: 7 }, (_, index) => `${currentMonth}-0${index + 1}`)
    .find(date => new Date(`${date}T12:00:00`).getDay() === weekday)!
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3102',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS'
  }
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const ok = (result: unknown) => route.fulfill({ status: 200, headers: cors, contentType: 'application/json', body: JSON.stringify(apiOk(result)) })
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors })
    const path = new URL(request.url()).pathname.replace('/api/v1', '')
    if (path === '/auth/sign-in') return ok({ session: {
      accessToken: 'timesheet-token', refreshToken: 'timesheet-refresh', mustChangePassword: false, tenantId, role: 'Employee',
      accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
      refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString()
    }, organizations: [] })
    // A Sunday-to-Thursday organization.
    if (path === '/timesheets/catalog') return ok({ projects: [], categories: [], holidays: [], workingDays: [0, 1, 2, 3, 4] })
    if (path === '/timesheets/entries' || path === '/timesheets/tasks' || path === '/timesheets/leave') return ok([])
    if (path === '/timesheets/context') return ok({ currentMonth, locked: false })
    return route.fulfill({ status: 404, headers: cors })
  })

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('employee@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: /Timesheet.*Record time/ }).click()

  const timesheet = page.frameLocator('iframe')
  const cell = (date: string) => timesheet.locator(`.day-button[aria-label="Date ${date}"]`)
  await expect(cell(firstDay(5))).toHaveClass(/weekend/)
  await expect(cell(firstDay(6))).toHaveClass(/weekend/)
  await expect(cell(firstDay(0))).not.toHaveClass(/weekend/)
  await expect(cell(firstDay(1))).not.toHaveClass(/weekend/)
})
