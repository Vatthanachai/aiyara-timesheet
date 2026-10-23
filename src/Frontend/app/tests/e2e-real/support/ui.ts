import { expect, type FrameLocator, type Locator, type Page } from '@playwright/test'
import type { Authenticator } from './totp'

/**
 * Production Nuxt builds do not expose a hydration flag, so a page counts as ready once the app has
 * loaded and its scripts have gone quiet. Forms are still filled inside `toPass` below, so a value
 * wiped by a late hydration is typed again instead of failing the spec.
 */
export async function openPage(page: Page, path: string) {
  await page.goto(path)
  await page.waitForLoadState('networkidle')
}

/** The Shell's signed-in marker: the profile menu in the header. */
export const profileMenu = (page: Page) => page.locator('.profile-trigger')

/** Types a code into the second-factor form that sign-in shows and continues. */
export async function enterCode(scope: Page, authenticator: Authenticator, button = 'Continue') {
  const field = scope.getByLabel('Authentication code')
  await expect(field).toBeVisible()
  await field.fill(await authenticator.code())
  await scope.getByRole('button', { name: button, exact: true }).click()
}

/** Signs a member in by email and password, and with an authenticator when the account has one. */
export async function signIn(page: Page, email: string, password: string, authenticator?: Authenticator) {
  await openPage(page, '/')
  await expect(async () => {
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(profileMenu(page).or(page.getByLabel('Authentication code'))).toBeVisible({ timeout: 8_000 })
  }).toPass({ timeout: 45_000 })
  if (authenticator) {
    await enterCode(page, authenticator)
    await expect(profileMenu(page)).toBeVisible()
  }
}

export async function signOut(page: Page) {
  await profileMenu(page).click()
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(page.getByRole('heading', { name: /signed out/ })).toBeVisible()
}

/** Opens a Shell navigation link; the remote it names loads in the page's only iframe. */
export async function openRemote(page: Page, link: string | RegExp): Promise<FrameLocator> {
  await page.getByRole('navigation').getByRole('link', { name: link }).first().click()
  await expect(page.locator('iframe')).toBeVisible()
  return page.frameLocator('iframe')
}

export async function openProfile(page: Page): Promise<FrameLocator> {
  await profileMenu(page).click()
  await page.getByRole('link', { name: 'Profile settings' }).click()
  await expect(page.locator('iframe')).toBeVisible()
  return page.frameLocator('iframe')
}

/**
 * Runs a form interaction until the page accepts it. A remote inside the iframe may still be
 * hydrating when the spec first types into it, which clears the field again.
 */
export const until = (action: () => Promise<void>, timeout = 30_000) => expect(action).toPass({ timeout })

/** Picks an autocomplete option the way a person does: type part of it, then click it in the list. */
export async function choose(field: Locator, label: string) {
  await field.click()
  await field.fill(label)
  const box = field.locator('xpath=ancestor::span[contains(concat(" ", normalize-space(@class), " "), " autocomplete ")][1]')
  await box.getByRole('option').filter({ hasText: label }).first().click()
}
