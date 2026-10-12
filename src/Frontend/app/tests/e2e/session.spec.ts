import { expect, test } from '@playwright/test'

const organizationCode = 'ACME123'
const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'
const apiOk = <T>(result: T) => ({ statusCode: 200, statusMessage: 'OK', result })

async function waitForHydration(page: import('@playwright/test').Page) {
  await page.waitForFunction(() => Boolean(
    (document.querySelector('button') as (HTMLElement & { __vnode?: unknown }) | null)?.__vnode
  ))
}

test('sign in persists the session, restores it after reload, and signs out', async ({ page }) => {
  let loginRequest: unknown
  await page.route('**/api/v1/auth/login', async route => {
    loginRequest = route.request().postDataJSON()
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
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

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Organization code').fill(organizationCode)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  expect(loginRequest).toMatchObject({ organizationCode, email: 'admin@example.test', password: 'test-password' })
  await expect(page.getByRole('region', { name: 'Available applications' })).toBeVisible()
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

    if (request.url().endsWith('/api/v1/auth/login')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk({
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
  await page.getByLabel('Organization code').fill(organizationCode)
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
    if (url.pathname.endsWith('/api/v1/auth/login')) {
      await route.fulfill({ status: 200, headers: corsHeaders, contentType: 'application/json', body: JSON.stringify(apiOk({
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
  await page.getByLabel('Organization code').fill(organizationCode)
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

test('failed sign in displays an error and does not persist credentials', async ({ page }) => {
  await page.route('**/api/v1/auth/login', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: 'Unauthorized', status: 401 })
  }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Organization code').fill(organizationCode)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('wrong-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('alert')).toHaveText('Session expired. Please sign in again.')
  await expect(page.getByRole('region', { name: 'Available applications' })).toHaveCount(0)
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.access'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('aiyara.refresh'))).toBeNull()
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

  await page.route('**/api/v1/auth/login', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
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
  await page.getByLabel('Organization code').fill('SYS0001')
  await page.getByLabel('Email').fill('sysadmin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('link', { name: 'Organizations' }).click()

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
  await page.route('**/api/v1/auth/login', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(apiOk({
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
  await page.getByLabel('Organization code').fill(organizationCode)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('link', { name: 'Organizations' })).toHaveCount(0)
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
