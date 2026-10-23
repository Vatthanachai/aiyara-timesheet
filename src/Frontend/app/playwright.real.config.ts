import { defineConfig, devices } from '@playwright/test'

/**
 * Browser flows against a real Compose test stack (scripts/test-stack.ps1 up -Frontend), not mocked
 * APIs. It starts no dev server and uses none of the mocked suite's ports (3100-3105); every URL
 * comes from the environment that scripts/test-e2e-real.ps1 sets from the stack's own ports.
 */
const need = (name: string) => {
  const value = process.env[name]
  if (!value) throw new Error(`${name} is not set. Run scripts/test-e2e-real.ps1, or set E2E_REAL_BASE_URL, E2E_REAL_GATEWAY_URL, E2E_REAL_MAILDEV_URL and E2E_REAL_PLATFORM_ADMIN_EMAIL for a running test stack.`)
  return value
}

export default defineConfig({
  testDir: './tests/e2e-real',
  testMatch: '**/*.spec.ts',
  // The platform administrator is one shared account whose authenticator state is kept between specs.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  reporter: 'list',
  outputDir: './test-results-real',
  use: {
    baseURL: need('E2E_REAL_BASE_URL'),
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome']
  }
})
