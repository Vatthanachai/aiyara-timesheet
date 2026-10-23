import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

/**
 * The installable app (Mobile And Offline Plan, decisions 1, 15 to 17). The manifest and its links are checked on the
 * development Shell; the service workers exist only in production builds, so those specs use the production copies of
 * the Shell (3106) and the Timesheet remote (3107) that playwright.config.ts starts.
 */
const shell = 'http://127.0.0.1:3106'
const remote = 'http://127.0.0.1:3107'

test('the manifest is served, linked, and installable, with icons and theme colors from the shared tokens', async ({ page, request }) => {
  await page.goto('/')
  const href = await page.locator('link[rel="manifest"]').getAttribute('href')
  expect(href).toBe('/manifest.webmanifest')
  const response = await request.get(new URL(href!, page.url()).toString())
  expect(response.status()).toBe(200)
  expect(response.headers()['content-type']).toContain('manifest+json')
  const manifest = await response.json()
  expect(manifest).toMatchObject({ name: 'Aiyara Timesheet', start_url: '/', scope: '/', display: 'standalone', theme_color: '#155eef', background_color: '#eef2f7' })
  const sizes = manifest.icons.map((icon: { sizes: string; purpose: string }) => `${icon.sizes}:${icon.purpose}`)
  expect(sizes).toEqual(expect.arrayContaining(['192x192:any', '512x512:any', '512x512:maskable']))
  for (const icon of manifest.icons) expect((await request.get(new URL(icon.src, page.url()).toString())).status()).toBe(200)

  // The manifest cannot read CSS variables, so its colors must match the shared tokens (the light theme).
  const tokens = readFileSync(join(process.cwd(), '../shared/tokens.css'), 'utf-8')
  expect(tokens).toContain(`--color-brand: ${manifest.theme_color};`)
  expect(tokens).toContain(`--color-canvas: ${manifest.background_color};`)
  await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute('href', '/icons/apple-touch-icon.png')
  await expect(page.locator('meta[name="apple-mobile-web-app-capable"]')).toHaveAttribute('content', 'yes')
  await expect(page.locator('meta[name="theme-color"]')).toHaveCount(2)
  await expect(page.locator('meta[name="viewport"]')).toHaveAttribute('content', /viewport-fit=cover/)
  expect((await request.get('/icons/apple-touch-icon.png')).headers()['content-type']).toBe('image/png')
})

async function controlled(page: Page, url: string) {
  await page.goto(url)
  await page.evaluate(() => navigator.serviceWorker.ready.then(() => undefined))
  // A first visit is not controlled by the worker it installs; the next load is.
  await page.reload()
  await expect.poll(() => page.evaluate(() => Boolean(navigator.serviceWorker.controller))).toBe(true)
}

/** Every URL in every cache the page can see. */
const cachedUrls = (page: Page) => page.evaluate(async () => {
  const urls: string[] = []
  // The precache keys a page with a revision in its query; the page's own address is what matters.
  for (const name of await caches.keys()) for (const request of await (await caches.open(name)).keys()) urls.push(request.url.replace(/\?__WB_REVISION__=\w+$/, ''))
  return urls
})

test('the Shell registers a service worker, serves no-cache sw.js and manifest, and opens its member pages offline', async ({ page, request }) => {
  const worker = await request.get(`${shell}/sw.js`)
  expect(worker.headers()['cache-control']).toBe('no-cache')
  expect(worker.headers()['service-worker-allowed']).toBe('/')
  expect((await request.get(`${shell}/manifest.webmanifest`)).headers()['cache-control']).toBe('no-cache')

  await controlled(page, `${shell}/`)
  const urls = await cachedUrls(page)
  expect(urls.some(url => url === `${shell}/`)).toBe(true)
  expect(urls.some(url => url === `${shell}/remote/timesheet`)).toBe(true)
  expect(urls.some(url => url.includes('/_nuxt/') && url.endsWith('.js'))).toBe(true)
  // No answer from the Gateway, or anything holding a member's data, is ever stored by the worker.
  expect(urls.filter(url => /\/api\/|gateway|:8081/.test(url))).toEqual([])

  await page.context().setOffline(true)
  await page.reload()
  await expect(page.getByLabel('Email')).toBeVisible()
  await page.goto(`${shell}/remote/timesheet`)
  await expect(page.getByRole('heading', { name: 'Please sign in' })).toBeVisible()
  await page.context().setOffline(false)
})

test('the Timesheet remote registers its own service worker and opens offline, caching only its build', async ({ page, request }) => {
  const worker = await request.get(`${remote}/sw.js`)
  expect(worker.headers()['cache-control']).toBe('no-cache')
  await controlled(page, `${remote}/`)
  const urls = await cachedUrls(page)
  expect(urls.some(url => url === `${remote}/`)).toBe(true)
  expect(urls.filter(url => /\/api\/|gateway|:8081/.test(url))).toEqual([])

  await page.context().setOffline(true)
  await page.reload()
  await expect(page.locator('.sign-in-card')).toBeVisible()
  await page.context().setOffline(false)
})
