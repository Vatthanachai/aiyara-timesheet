import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

/** Where the real test stack answers, read from the environment scripts/test-e2e-real.ps1 sets. */
const need = (name: string) => {
  const value = process.env[name]
  if (!value) throw new Error(`${name} is not set. Run scripts/test-e2e-real.ps1.`)
  return value.replace(/\/$/, '')
}

export const stack = {
  /** The Shell as the browser opens it (localhost, the origin the Gateway allows). */
  baseUrl: need('E2E_REAL_BASE_URL'),
  /** The Gateway for the tests' own API calls (setup that is not the point of a spec). */
  gatewayUrl: need('E2E_REAL_GATEWAY_URL'),
  mailUrl: need('E2E_REAL_MAILDEV_URL'),
  platformEmail: need('E2E_REAL_PLATFORM_ADMIN_EMAIL')
}

export const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

/** A short random suffix that keeps every run's organizations and emails unique. */
export const unique = () => Math.random().toString(36).slice(2, 8) + Date.now().toString(36).slice(-4)

/** Passes the default organization password policy (12+ characters, upper, lower, digit, symbol). */
export const password = 'Real-Stack-E2e-2026!'

const stateFile = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'platform-admin.json')

/** What the shared Platform Admin needs between specs: its authenticator secret and last-used step. */
export type PlatformAdminState = { email: string; password: string; secret: string; lastStep: number }

export function readPlatformAdminState(): PlatformAdminState | null {
  try {
    const state = JSON.parse(readFileSync(stateFile, 'utf8')) as PlatformAdminState
    return state.email === stack.platformEmail ? state : null
  } catch { return null }
}

export function writePlatformAdminState(state: PlatformAdminState) {
  if (!existsSync(dirname(stateFile))) mkdirSync(dirname(stateFile), { recursive: true })
  writeFileSync(stateFile, JSON.stringify(state))
}
