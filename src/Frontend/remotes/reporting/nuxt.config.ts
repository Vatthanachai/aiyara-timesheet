import tailwindcss from '@tailwindcss/vite'

export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  runtimeConfig: { public: { shellOrigin: process.env.NUXT_PUBLIC_SHELL_ORIGIN || 'http://localhost:3000' } },
  css: ['~/assets/main.css'],
  vite: { plugins: [tailwindcss()] }
})
