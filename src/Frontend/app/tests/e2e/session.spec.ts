import { expect, test } from '@playwright/test'

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

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('Email').fill('admin@example.test')
  await page.getByLabel('Password').fill('test-password')
  await page.getByRole('button', { name: 'Sign in' }).click()

  expect(loginRequest).toEqual({ email: 'admin@example.test', password: 'test-password' })
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

test('platform admin signs in without an organization code and lands on the overview', async ({ page }) => {
  let loginRequest: unknown
  await page.route('**/api/v1/auth/platform-admin/login', async route => {
    loginRequest = route.request().postDataJSON()
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(apiOk({
        accessToken: 'platform-admin-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'platform-admin-refresh',
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false,
        tenantId: '00000000-0000-0000-0000-000000000001',
        role: 'PlatformAdmin'
      }))
    })
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

  expect(loginRequest).toEqual({ email: 'operator@platform.test', password: 'test-password' })
  await expect(page).toHaveURL(/\/sys-admin$/)
  await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.role')))
    .toBe('PlatformAdmin')
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

test('console pages stay restricted for non platform admins', async ({ page }) => {
  await signInAs(page, 'TenantAdmin')
  for (const path of ['/sys-admin', '/sys-admin/audit', '/sys-admin/organizations/c99358d1-e01f-4dfd-9693-38da8883562d']) {
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
    memberCalls: 0
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
    run('run-ready', { signedVersion: 2, subjectUserId: 'member-gone' })
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
        { id: 'member-anong', firstName: 'Anong', lastName: 'Admin', email: 'admin@acme.test', role: 'TenantAdmin', status: 'Active' },
        { id: 'member-somchai', firstName: 'Somchai', lastName: 'Staff', email: 'somchai@acme.test', role: 'Employee', status: 'Active' },
        { id: 'member-new', firstName: '', lastName: '', email: 'new@acme.test', role: 'Employee', status: 'PendingActivation' }
      ])
    }
    if (url.pathname.endsWith('/reports/context')) return ok({ isAdmin: role === 'TenantAdmin', timeZoneId: 'Asia/Bangkok' })
    if (url.pathname.endsWith('/reports/definitions')) return ok(definitions)
    if (url.pathname.match(/\/reports\/definitions\/[^/]+$/) && request.method() === 'PUT') {
      state.renamed.push(request.postDataJSON())
      return ok(definitions[0])
    }
    if (url.pathname.endsWith('/reports/dashboard')) return ok({ total: 4, ready: 1, queued: 1, blocked: 1, failed: 1 })
    if (url.pathname.endsWith('/reports/schedules')) return ok([])
    if (url.pathname.endsWith('/reports/retention')) return ok({ years: 7 })
    if (url.pathname.endsWith('/reports/runs')) {
      if (request.method() === 'POST') {
        state.created.push(request.postDataJSON())
        return ok({ id: 'run-new' }, 202)
      }
      state.runQueries.push(url.searchParams)
      const status = url.searchParams.get('status')
      const items = runs().filter(item => !status || item.status === status)
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

  await expect(frame.locator('.summary')).toContainText('Needs attention2')
  const history = frame.locator('table')
  await expect(history).toContainText('Somchai Staff')
  await expect(history).toContainText('Former member')
  await expect(history).toContainText('Whole organization')
  await expect(history).toContainText('Waiting for 2026-09 to be locked')
  await expect(history).toContainText('Request it again')
  await expect(history).toContainText('Signed v2')
  await expect(history).toContainText('Sep 1, 2026 – Sep 30, 2026')
  await expect(history).not.toContainText('member-')

  // Monthly report: month + year pickers, a member subject, and an exact period preview.
  const year = new Date().getFullYear()
  await frame.locator('.request-form').getByLabel(/^Month/).selectOption('1')
  await frame.locator('.request-form').getByLabel(/^Year/).selectOption(String(year))
  await expect(frame.getByLabel('Report on').locator('option')).toHaveText(['Whole organization', 'Anong Admin', 'Somchai Staff'])
  await frame.getByLabel('Report on').selectOption({ label: 'Somchai Staff' })
  await expect(frame.locator('.preview')).toHaveText(`Period: Jan 1, ${year} – Jan 31, ${year} (Asia/Bangkok)`)
  await frame.getByRole('button', { name: 'Generate report' }).click()
  await expect.poll(() => state.created).toEqual([{
    definitionId: 'def-month', subjectUserId: 'member-somchai',
    periodStartUtc: `${year - 1}-12-31T17:00:00.000Z`, periodEndUtc: `${year}-01-31T17:00:00.000Z`
  }])

  // Weekly report: any day picks its Monday-to-Sunday week.
  await frame.locator('.request-form select').first().selectOption('def-week')
  await frame.getByLabel('Week containing').fill('2026-10-14')
  await expect(frame.locator('.preview')).toHaveText('Period: Oct 12, 2026 – Oct 18, 2026 (Asia/Bangkok)')

  // Filters are sent to the API.
  await frame.locator('.filters').getByLabel('Status').selectOption('Failed')
  await expect.poll(() => state.runQueries.at(-1)?.get('status')).toBe('Failed')
  await expect(history.locator('tbody tr')).toHaveCount(1)
  await frame.locator('.filters').getByLabel('Status').selectOption('')

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
})

test('employees request only their own reports and never load the member directory', async ({ page }) => {
  const { frame, state } = await openReporting(page, 'Employee')

  await expect(frame.locator('table')).toBeVisible()
  await expect(frame.getByRole('columnheader', { name: 'Person' })).toHaveCount(0)
  await expect(frame.getByLabel('Report on')).toHaveCount(0)
  await expect(frame.getByRole('heading', { name: 'Report settings' })).toHaveCount(0)
  await frame.getByRole('button', { name: 'Generate report' }).click()
  await expect.poll(() => state.created.length).toBe(1)
  expect((state.created[0] as { subjectUserId: unknown }).subjectUserId).toBeNull()
  expect(state.memberCalls).toBe(0)

  // Only the history table may scroll sideways on a phone; the page itself must not.
  for (const width of [375, 320]) {
    await page.setViewportSize({ width, height: 850 })
    await expect.poll(() => frame.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await expect(frame.getByRole('button', { name: 'Generate report' })).toBeVisible()
  }
})
