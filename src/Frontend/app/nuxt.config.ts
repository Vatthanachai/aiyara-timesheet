import tailwindcss from '@tailwindcss/vite'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  modules: ['@vite-pwa/nuxt'],
  css: ['~/assets/css/main.css'],
  routeRules: {
    // A deployed change must be noticed at once, so the worker and manifest are always revalidated.
    '/sw.js': { headers: { 'cache-control': 'no-cache', 'service-worker-allowed': '/' } },
    '/manifest.webmanifest': { headers: { 'cache-control': 'no-cache' } }
  },
  // Installable app. The manifest is a static file (public/manifest.webmanifest) and the icons are committed
  // PNGs. The worker precaches the build's own files and the two member pages, whose HTML holds no member
  // data (the session lives in the tab). There is no runtime caching rule, so a request to the Gateway is
  // never answered or stored by the worker. `prompt` lets a new version wait until the member reloads.
  pwa: {
    registerType: 'prompt',
    manifest: false,
    workbox: {
      globPatterns: ['**/*.{js,css,woff,woff2}'],
      additionalManifestEntries: ['/', '/remote/timesheet'].map(url => ({ url, revision: `${Date.now()}` })),
      cleanupOutdatedCaches: true,
      runtimeCaching: []
    },
    client: { installPrompt: false },
    devOptions: { enabled: false }
  },
  app: {
    head: {
      meta: [
        { name: 'viewport', content: 'width=device-width, initial-scale=1, viewport-fit=cover' },
        { name: 'theme-color', content: '#eef2f7', media: '(prefers-color-scheme: light)' },
        { name: 'theme-color', content: '#0d1524', media: '(prefers-color-scheme: dark)' },
        { name: 'mobile-web-app-capable', content: 'yes' },
        { name: 'apple-mobile-web-app-capable', content: 'yes' },
        { name: 'apple-mobile-web-app-title', content: 'Timesheet' },
        { name: 'apple-mobile-web-app-status-bar-style', content: 'default' }
      ],
      link: [
        { rel: 'manifest', href: '/manifest.webmanifest' },
        { rel: 'icon', type: 'image/png', sizes: '192x192', href: '/icons/icon-192.png' },
        { rel: 'apple-touch-icon', href: '/icons/apple-touch-icon.png' }
      ],
      // Applies the remembered theme before the first paint, so a dark page never flashes light.
      script: [{
        tagPosition: 'head',
        innerHTML: "try{var t=localStorage.getItem('aiyara.theme');if(t!=='light'&&t!=='dark')t=matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';document.documentElement.dataset.theme=t}catch(e){}"
      }]
    }
  },
  vite: {
    plugins: [tailwindcss()]
  },
  runtimeConfig: {
    public: {
      gatewayUrl: process.env.NUXT_PUBLIC_GATEWAY_URL || 'http://localhost:8081',
      remotes: {
        identity: process.env.NUXT_PUBLIC_IDENTITY_REMOTE_URL || 'http://localhost:3005',
        timesheet: process.env.NUXT_PUBLIC_TIMESHEET_REMOTE_URL || 'http://localhost:3002',
        reporting: process.env.NUXT_PUBLIC_REPORTING_REMOTE_URL || 'http://localhost:3003',
        administration: process.env.NUXT_PUBLIC_ADMINISTRATION_REMOTE_URL || 'http://localhost:3004'
      }
    }
  }
})
