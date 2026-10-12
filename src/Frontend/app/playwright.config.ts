import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:3100',
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome']
  },
  webServer: [
    {
      command: 'bun run dev --host 127.0.0.1 --port 3100',
      url: 'http://127.0.0.1:3100',
      env: {
        NUXT_PUBLIC_GATEWAY_URL: 'http://127.0.0.1:3100',
        NUXT_PUBLIC_IDENTITY_REMOTE_URL: 'http://127.0.0.1:3105',
        NUXT_PUBLIC_TIMESHEET_REMOTE_URL: 'http://127.0.0.1:3102',
        NUXT_PUBLIC_REPORTING_REMOTE_URL: 'http://127.0.0.1:3103',
        NUXT_PUBLIC_ADMINISTRATION_REMOTE_URL: 'http://127.0.0.1:3104'
      },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000
    },
    {
      command: 'bun run --cwd ../remotes/identity-profile nuxt dev --host 127.0.0.1 --port 3105',
      url: 'http://127.0.0.1:3105',
      env: { NUXT_PUBLIC_SHELL_ORIGIN: 'http://127.0.0.1:3100' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000
    },
    {
      command: 'bun run --cwd ../remotes/reporting nuxt dev --host 127.0.0.1 --port 3103',
      url: 'http://127.0.0.1:3103',
      env: { NUXT_PUBLIC_SHELL_ORIGIN: 'http://127.0.0.1:3100' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000
    },
    {
      command: 'bun run --cwd ../remotes/administration nuxt dev --host 127.0.0.1 --port 3104',
      url: 'http://127.0.0.1:3104',
      env: { NUXT_PUBLIC_SHELL_ORIGIN: 'http://127.0.0.1:3100' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000
    },
    {
      command: 'bun run --cwd ../remotes/timesheet nuxt dev --host 127.0.0.1 --port 3102',
      url: 'http://127.0.0.1:3102',
      env: { NUXT_PUBLIC_SHELL_ORIGIN: 'http://127.0.0.1:3100' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000
    }
  ]
})
