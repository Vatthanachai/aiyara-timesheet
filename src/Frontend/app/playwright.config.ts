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
  webServer: {
    command: 'bun run dev --host 127.0.0.1 --port 3100',
    url: 'http://127.0.0.1:3100',
    env: { NUXT_PUBLIC_GATEWAY_URL: 'http://127.0.0.1:3100' },
    reuseExistingServer: !process.env.CI,
    timeout: 120_000
  }
})
