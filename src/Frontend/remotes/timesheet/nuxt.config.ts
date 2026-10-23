import tailwindcss from '@tailwindcss/vite'

export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  // The page is a client-side app that waits for the Shell's session message, so it is built as a single
  // page: the service worker can precache that one HTML file and the remote then opens without a network.
  ssr: false,
  modules: ['@vite-pwa/nuxt'],
  routeRules: { '/sw.js': { headers: { 'cache-control': 'no-cache', 'service-worker-allowed': '/' } } },
  runtimeConfig: { public: { shellOrigin: process.env.NUXT_PUBLIC_SHELL_ORIGIN || 'http://localhost:3000' } },
  css: ['~/assets/main.css', '~/assets/timesheet.css'],
  vite: { plugins: [tailwindcss()] },
  // Only the build's own files (hashed scripts, styles, fonts, the page) are cached. There is no runtime
  // caching rule, so a request to the Gateway is never answered from the service worker: the member's
  // data is kept in IndexedDB by the page, under its tenant and account, not in a shared HTTP cache.
  pwa: {
    // A new version waits until the iframe is next opened, so a deploy never reloads a page in the middle of an entry.
    registerType: 'prompt',
    manifest: false,
    workbox: {
      globPatterns: ['**/*.{js,css,html,woff,woff2}'],
      // The single page is rendered by the server at run time, so the build has no HTML file to list: it is
      // added by hand, with a new revision on every build so a deployed change replaces the cached page.
      additionalManifestEntries: [{ url: '/', revision: `${Date.now()}` }],
      navigateFallback: '/',
      cleanupOutdatedCaches: true,
      runtimeCaching: []
    },
    client: { installPrompt: false },
    devOptions: { enabled: false }
  }
})
