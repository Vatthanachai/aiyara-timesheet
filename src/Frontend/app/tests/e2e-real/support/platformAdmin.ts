import { expect, type Page } from '@playwright/test'
import { call } from './api'
import { activationSubject, waitForMailCode } from './mail'
import { password, readPlatformAdminState, stack, writePlatformAdminState, type PlatformAdminState } from './stack'
import { Authenticator } from './totp'
import { openPage } from './ui'

/**
 * Signs the stack's bootstrap Platform Admin in through the System administrator page.
 *
 * The test stack starts with one pending Platform Admin (BOOTSTRAP_PLATFORM_ADMIN_EMAIL). The first
 * call activates it from the activation email and enrolls an authenticator, which Platform Admins must
 * have; the secret and last-used step are kept in tests/e2e-real/.state so later specs and later runs
 * on the same stack sign in with a fresh code. A run on a reset stack clears that file first
 * (scripts/test-e2e-real.ps1 does), so a stale file only matters if the stack was reset by hand.
 */
export async function signInPlatformAdmin(page: Page) {
  let state = readPlatformAdminState()
  if (!state) {
    // An interrupted earlier run may have activated the account already; then the code is refused and
    // the sign-in below decides whether an authenticator can still be set up.
    try { await call('POST', '/auth/activate', { code: await waitForMailCode(stack.platformEmail, activationSubject, 30_000), password }) }
    catch { /* checked by the sign-in */ }
  }

  await openPage(page, '/sys-admin/login')
  await expect(async () => {
    await page.getByLabel('Email').fill(stack.platformEmail)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page.getByLabel('Authentication code')).toBeVisible({ timeout: 8_000 })
  }).toPass({ timeout: 45_000 })

  if (!state) {
    // First sign-in: the page offers an authenticator; add it, then keep the recovery codes screen moving.
    if (!await page.locator('.setup-key code').isVisible()) {
      throw new Error(`The Platform Admin ${stack.platformEmail} already has an authenticator this run does not know. Reset the stack (scripts/test-e2e-real.ps1 does) or delete tests/e2e-real/.state after restoring it.`)
    }
    const secret = (await page.locator('.setup-key code').innerText()).replace(/\s/g, '')
    const authenticator = new Authenticator(secret)
    state = { email: stack.platformEmail, password, secret, lastStep: 0 }
    await page.getByLabel('Authentication code').fill(await authenticator.code())
    state.lastStep = authenticator.lastStep
    writePlatformAdminState(state)
    await page.getByRole('button', { name: 'Continue', exact: true }).click()
    await page.getByRole('button', { name: 'I saved these codes, continue' }).click()
  } else {
    const authenticator = new Authenticator(state.secret, state.lastStep)
    await page.getByLabel('Authentication code').fill(await authenticator.code())
    writePlatformAdminState({ ...state, lastStep: authenticator.lastStep } satisfies PlatformAdminState)
    await page.getByRole('button', { name: 'Continue', exact: true }).click()
  }
  await expect(page).toHaveURL(/\/sys-admin$/)
}
