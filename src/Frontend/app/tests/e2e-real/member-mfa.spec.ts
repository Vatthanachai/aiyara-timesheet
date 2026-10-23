import { expect, test } from '@playwright/test'
import { addEmployee, createOrganization, enrollAuthenticator } from './support/api'
import { password } from './support/stack'
import { Authenticator } from './support/totp'
import { enterCode, openPage, openRemote, profileMenu, signIn, signOut, until } from './support/ui'
import { paceSpecs } from './support/pace'

paceSpecs()

// A Tenant Admin (who has an authenticator, as the setting requires) turns on "Require MFA for all
// members". A member without one must then set it up when signing in, is shown recovery codes, and
// signs in with a code afterwards. Codes are computed from the secret the page shows; the helper
// never reuses a 30-second step the server has accepted.
test('a Tenant Admin requires MFA and a member enrolls at sign-in, then signs in with a code', async ({ page }) => {
  const organization = await createOrganization('Mfa')
  const employee = await addEmployee(organization)
  const adminAuthenticator = await enrollAuthenticator(organization.adminToken)

  // The admin signs in with a code and turns the requirement on.
  await signIn(page, organization.adminEmail, password, adminAuthenticator)
  const administration = await openRemote(page, 'Administration')
  await administration.getByRole('tab', { name: 'Security' }).click()
  const requirement = administration.getByRole('switch', { name: 'Require MFA for all members' })
  await expect(requirement).toHaveAttribute('aria-checked', 'false')
  await until(async () => {
    if (!await administration.getByRole('button', { name: 'Turn on and sign out members without MFA' }).isVisible()) await requirement.click()
    await expect(administration.getByRole('button', { name: 'Turn on and sign out members without MFA' })).toBeVisible({ timeout: 5_000 })
  })
  await administration.getByRole('button', { name: 'Turn on and sign out members without MFA' }).click()
  await expect(administration.getByRole('status').filter({ hasText: 'MFA is now required for all members.' })).toBeVisible()
  await expect(requirement).toHaveAttribute('aria-checked', 'true')
  await signOut(page)

  // The member's password is accepted, but the page asks for an authenticator to be set up first.
  await openPage(page, '/')
  await until(async () => {
    await page.getByLabel('Email').fill(employee.email)
    await page.getByLabel('Password').fill(employee.password)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Set up an authenticator to sign in' })).toBeVisible({ timeout: 8_000 })
  })
  const secret = (await page.locator('.mfa-key code').innerText()).replace(/\s/g, '')
  const authenticator = new Authenticator(secret)
  await enterCode(page, authenticator)

  // First enrollment ends with one-time recovery codes that must be acknowledged.
  await expect(page.getByRole('heading', { name: 'Save your recovery codes' })).toBeVisible()
  await expect(page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem')).toHaveCount(10)
  await page.getByLabel('I have saved these codes').check()
  await page.getByRole('button', { name: 'Continue', exact: true }).click()
  await expect(profileMenu(page)).toBeVisible()
  await signOut(page)

  // From now on the member signs in with a code.
  await signIn(page, employee.email, employee.password, authenticator)
  await expect(profileMenu(page)).toBeVisible()
})
