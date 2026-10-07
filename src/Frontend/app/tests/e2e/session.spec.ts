import { expect, test } from '@playwright/test'

const organizationCode = 'ACME123'
const tenantId = 'ae15beba-50e1-4ca3-8495-b4a8af1fca08'

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
      body: JSON.stringify({
        accessToken: 'test-access-token',
        accessExpiresAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
        refreshToken: 'test-refresh-token',
        refreshExpiresAtUtc: new Date(Date.now() + 24 * 60 * 60_000).toISOString(),
        mustChangePassword: false,
        tenantId
      })
    })
  })
  await page.route('**/api/v1/auth/logout', route => route.fulfill({ status: 204 }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('รหัสองค์กร').fill(organizationCode)
  await page.getByLabel('อีเมล').fill('admin@example.test')
  await page.getByLabel('รหัสผ่าน').fill('test-password')
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  expect(loginRequest).toMatchObject({ organizationCode, email: 'admin@example.test', password: 'test-password' })
  await expect(page.getByRole('region', { name: 'Available applications' })).toBeVisible()
  await expect(page.getByRole('link', { name: /รายงาน.*ดูรายงาน/ })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.access')))
    .toBe('test-access-token')
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.tenant')))
    .toBe(tenantId)

  await page.reload()
  await expect(page.getByRole('button', { name: 'ออกจากระบบ' })).toBeVisible()
  await page.getByRole('button', { name: 'ออกจากระบบ' }).click()
  await expect(page.getByRole('button', { name: 'เข้าสู่ระบบ' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('aiyara.access')))
    .toBeNull()
})

test('failed sign in displays an error and does not persist credentials', async ({ page }) => {
  await page.route('**/api/v1/auth/login', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: 'Unauthorized', status: 401 })
  }))

  await page.goto('/')
  await waitForHydration(page)
  await page.getByLabel('รหัสองค์กร').fill(organizationCode)
  await page.getByLabel('อีเมล').fill('admin@example.test')
  await page.getByLabel('รหัสผ่าน').fill('wrong-password')
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  await expect(page.getByRole('alert')).toHaveText('เข้าสู่ระบบไม่สำเร็จ')
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
  await page.getByLabel('รหัสเปิดใช้งานจากอีเมล').fill('one-time-activation-code')
  await page.getByLabel('ตั้งรหัสผ่านใหม่').fill('New-Strong-Password-123!')
  await page.getByLabel('ยืนยันรหัสผ่านใหม่').fill('New-Strong-Password-123!')
  await page.getByRole('button', { name: 'เปิดใช้งานบัญชี' }).click()

  await expect.poll(() => activationRequest).toEqual({
    code: 'one-time-activation-code',
    password: 'New-Strong-Password-123!'
  })
  await expect(page.locator('section[role="status"]')).toContainText('เปิดใช้งานสำเร็จ')
  await expect(page.getByRole('link', { name: 'ไปหน้าเข้าสู่ระบบ' })).toHaveAttribute('href', '/')
})

test('activation failure shows a safe message and does not expose the API error', async ({ page }) => {
  await page.route('**/api/v1/auth/activate', route => route.fulfill({
    status: 400,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: 'Invalid or expired challenge.', status: 400 })
  }))

  await page.goto('/activate')
  await waitForHydration(page)
  await page.getByLabel('รหัสเปิดใช้งานจากอีเมล').fill('expired-code')
  await page.getByLabel('ตั้งรหัสผ่านใหม่').fill('New-Strong-Password-123!')
  await page.getByLabel('ยืนยันรหัสผ่านใหม่').fill('New-Strong-Password-123!')
  await page.getByRole('button', { name: 'เปิดใช้งานบัญชี' }).click()

  await expect(page.getByRole('alert')).toHaveText(
    'เปิดใช้งานไม่สำเร็จ กรุณาตรวจสอบรหัสและข้อกำหนดรหัสผ่านขององค์กร'
  )
  await expect(page.getByRole('alert')).not.toContainText('Invalid or expired challenge')
})
