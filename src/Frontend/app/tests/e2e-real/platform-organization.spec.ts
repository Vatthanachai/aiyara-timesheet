import { expect, test } from '@playwright/test'
import { activationSubject, waitForMailCode } from './support/mail'
import { signInPlatformAdmin } from './support/platformAdmin'
import { password, stack, unique } from './support/stack'
import { openPage, profileMenu, signIn, until } from './support/ui'
import { paceSpecs } from './support/pace'

paceSpecs()

// A Platform Admin signs in (with an authenticator) and creates an organization; the organization's
// administrator then activates the account from the emailed code and signs in.
test('a Platform Admin creates an organization and its administrator activates and signs in', async ({ page, browser }) => {
  const id = unique()
  const name = `Real E2E Platform ${id}`
  const adminEmail = `platform-created-${id}@example.test`

  await signInPlatformAdmin(page)
  await page.goto('/sys-admin/organizations')
  await until(async () => {
    const nameField = page.getByLabel('Organization name', { exact: true })
    if (!await nameField.isVisible()) await page.getByRole('button', { name: 'Add organization' }).click()
    await nameField.fill(name)
    await page.getByLabel('Organization URL name').fill(`real-e2e-platform-${id}`)
    await page.getByLabel('Organization administrator email').fill(adminEmail)
    await page.getByRole('button', { name: 'Create organization and send activation email' }).click()
    await expect(page.getByRole('status').filter({ hasText: /Organization created\. Code: \S+\./ })).toBeVisible({ timeout: 8_000 })
  })

  // The new organization is listed with its administrator waiting for activation.
  const find = async () => {
    await page.getByLabel('Search organizations').fill(name)
    await page.getByRole('button', { name: 'Search', exact: true }).click()
    return page.getByRole('article').filter({ hasText: name })
  }
  const card = await find()
  await expect(card).toHaveCount(1)
  await expect(card.getByText(`real-e2e-platform-${id}`)).toBeVisible()
  await expect(card.getByText(adminEmail)).toBeVisible()
  await expect(card.getByText('Pending activation')).toBeVisible()

  // The administrator uses the code from the email, on the public activation page, in a fresh browser.
  const code = await waitForMailCode(adminEmail, activationSubject)
  const admin = await browser.newContext({ baseURL: stack.baseUrl })
  try {
    const adminPage = await admin.newPage()
    await openPage(adminPage, '/activate')
    await until(async () => {
      await adminPage.getByLabel('Activation code from email').fill(code)
      await adminPage.getByLabel('New password', { exact: true }).fill(password)
      await adminPage.getByLabel('Confirm new password').fill(password)
      await adminPage.getByRole('button', { name: 'Activate account' }).click()
      await expect(adminPage.getByRole('heading', { name: 'Account activated' })).toBeVisible({ timeout: 8_000 })
    })
    await signIn(adminPage, adminEmail, password)
    await expect(profileMenu(adminPage)).toBeVisible()
  } finally { await admin.close() }

  // The Platform Admin's list now shows the administrator as active.
  await page.reload()
  await expect(await find()).toContainText('Active')
  await expect(await find()).not.toContainText('Pending activation')
})
