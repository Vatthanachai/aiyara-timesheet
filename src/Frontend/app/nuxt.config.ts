import tailwindcss from '@tailwindcss/vite'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  css: ['~/assets/css/main.css'],
  app: {
    head: {
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
