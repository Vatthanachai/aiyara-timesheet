<script setup lang="ts">
import { useLocaleText } from '../../../../shared/i18n'
const route = useRoute()
const config = useRuntimeConfig()
const { navigation, canOpen } = useRemoteNavigation()

const remote = computed(() => navigation.find(item => item.id === route.params.remote))
const allowed = computed(() => Boolean(remote.value && canOpen(remote.value)))
const remoteUrl = computed(() => {
  if (!remote.value || !allowed.value) {
    return undefined
  }

  return config.public.remotes[remote.value.id]
})
const { token, tenantId, renew, restore } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: { signIn: 'กรุณาเข้าสู่ระบบ', goHome: 'กลับหน้าแรก', accessRestrictedTitle: 'ไม่มีสิทธิ์เข้าถึง', accessRestrictedBody: 'บัญชีของคุณไม่มีสิทธิ์ใช้งานส่วนนี้' },
  en: { signIn: 'Please sign in', goHome: 'Go home', accessRestrictedTitle: 'Access restricted', accessRestrictedBody: 'Your account does not have access to this area.' }
})
const frame = ref<HTMLIFrameElement | null>(null)
let refreshTimer: ReturnType<typeof setInterval> | undefined
function onRemoteMessage(event: MessageEvent) {
  if (!remoteUrl.value || event.source !== frame.value?.contentWindow ||
      event.origin !== new URL(remoteUrl.value).origin) return
  if (event.data?.type === 'aiyara.ready') {
    void sendSession()
    return
  }
  if (event.data?.type === 'aiyara.sign-in-required') {
    void navigateTo('/')
    return
  }
  if (event.data?.type === 'aiyara.refresh-request' && typeof event.data.requestId === 'string') {
    void renew(true).then(success => {
      frame.value?.contentWindow?.postMessage({ type: 'aiyara.refresh-result',
        requestId: event.data.requestId, token: success ? token.value : null }, event.origin)
      if (!success) void navigateTo('/')
    })
    return
  }
  if (event.data?.type === 'aiyara.resize' && Number.isFinite(event.data.height)) {
    frame.value.style.height = `${Math.max(200, Math.min(100_000, Math.ceil(event.data.height)))}px`
  }
}
onMounted(() => {
  restore()
  window.addEventListener('message', onRemoteMessage)
  refreshTimer = setInterval(sendSession, 60_000)
})
onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer)
  window.removeEventListener('message', onRemoteMessage)
})
async function sendSession() {
  if (!import.meta.client || !frame.value || !remoteUrl.value) return
  if (token.value) await renew()
  frame.value.contentWindow?.postMessage({ type: 'aiyara.session', token: token.value,
    locale: locale.value, tenantId: tenantId.value,
    gatewayUrl: config.public.gatewayUrl }, new URL(remoteUrl.value).origin)
}
watch([token, locale], () => sendSession())

if (!remote.value) {
  throw createError({ statusCode: 404, statusMessage: 'Remote application not found' })
}
</script>

<template>
  <section v-if="remote && token && !allowed" class="remote-delegation" role="alert">
    <h1>{{ text.accessRestrictedTitle }}</h1>
    <p>{{ text.accessRestrictedBody }}</p>
    <NuxtLink to="/">{{ text.goHome }}</NuxtLink>
  </section>
  <section v-else-if="remote && token" class="remote-workspace">
    <h1>{{ remote.label[locale] }}</h1>
    <iframe v-if="remoteUrl" ref="frame" :src="remoteUrl" :title="remote.label[locale]" @load="sendSession" />
  </section>
  <section v-else class="remote-delegation"><h1>{{ text.signIn }}</h1><NuxtLink to="/">{{ text.goHome }}</NuxtLink></section>
</template>
