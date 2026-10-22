import type { Locator } from '@playwright/test'

/** The shared autocomplete around a field found by its label. */
const box = (field: Locator) => field.locator('xpath=ancestor::span[contains(concat(" ", normalize-space(@class), " "), " autocomplete ")][1]')

/** The labels an autocomplete offers, without their hints. */
export const options = (field: Locator) => box(field).locator('[role="option"] .autocomplete-label')

/** Picks an autocomplete's option the way a person does: type part of it, then click it in the list. */
export async function choose(field: Locator, label: string) {
  await field.click()
  await field.fill(label)
  await box(field).locator(`xpath=.//li[@role="option"][span[contains(concat(" ", normalize-space(@class), " "), " autocomplete-label ") and normalize-space(.)="${label}"]]`).click()
}

/** A `YYYY-MM-DD` date as the date fields show it, dd/MM/yyyy. */
export const shownDate = (iso: string) => iso.split('-').reverse().join('/')

/** `GET /timesheets/me/months/{year}/{month}` for a month nobody has submitted: only the current month can be edited. */
export const monthState = (path: string, currentMonth: string, overrides: Record<string, unknown> = {}) => ({
  locked: false, status: 'open', returnReason: null,
  editable: path.endsWith(`/${Number(currentMonth.slice(0, 4))}/${Number(currentMonth.slice(5))}`), ...overrides
})
