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
onMounted(() => {
  restore()
  refreshTimer = setInterval(sendSession, 60_000)
})
onUnmounted(() => { if (refreshTimer) clearInterval(refreshTimer) })
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
