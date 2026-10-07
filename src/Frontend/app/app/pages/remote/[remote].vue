<script setup lang="ts">
const route = useRoute()
const config = useRuntimeConfig()
const { navigation } = useRemoteNavigation()

const remote = computed(() => navigation.find(item => item.id === route.params.remote))
const remoteUrl = computed(() => {
  if (!remote.value) {
    return undefined
  }

  return config.public.remotes[remote.value.id]
})
const { token, tenantId, renew, restore } = useSession()
const { locale } = useLocale()
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
  <section v-if="remote && token" class="remote-workspace">
    <h1>{{ locale === 'th' ? remote.label : remote.labelEn }}</h1>
    <iframe v-if="remoteUrl" ref="frame" :src="remoteUrl" :title="locale === 'th' ? remote.label : remote.labelEn" @load="sendSession" />
  </section>
  <section v-else class="remote-delegation"><h1>{{ locale === 'th' ? 'กรุณาเข้าสู่ระบบ' : 'Please sign in' }}</h1><NuxtLink to="/">{{ locale === 'th' ? 'กลับหน้าแรก' : 'Go home' }}</NuxtLink></section>
</template>
