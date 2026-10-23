import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { test } from '@playwright/test'
import { sleep } from './stack'

/**
 * Gateway limits the sign-in, activation, and invitation routes to 60 requests a minute from one
 * address, and every spec makes a few dozen of them (its own setup, the browser's sign-ins and token
 * refreshes). Specs therefore start at least `gapMs` after the previous one ended, also across runs,
 * so that a limit hit is not a flaky failure. Call `paceSpecs()` once at the top of each spec file.
 */
const gapMs = 25_000
const stamp = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'last-spec-end')

export function paceSpecs() {
  test.beforeEach(async () => {
    let last = 0
    try { last = Number(readFileSync(stamp, 'utf8')) } catch { /* the first spec of a stack */ }
    const wait = last + gapMs - Date.now()
    if (wait > 0) await sleep(wait)
  })
  test.afterEach(() => {
    if (!existsSync(dirname(stamp))) mkdirSync(dirname(stamp), { recursive: true })
    writeFileSync(stamp, String(Date.now()))
  })
}
