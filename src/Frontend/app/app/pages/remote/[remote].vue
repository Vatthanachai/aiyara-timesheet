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

if (!remote.value) {
  throw createError({ statusCode: 404, statusMessage: 'Remote application not found' })
}
</script>

<template>
  <section v-if="remote" class="remote-delegation">
    <p class="eyebrow">Route delegation</p>
    <h1>{{ remote.label }}</h1>
    <p>{{ remote.description }}</p>
    <p class="notice">
      This route is reserved for the independently deployed {{ remote.label }} remote. Configure its URL
      using the matching <code>NUXT_PUBLIC_*_REMOTE_URL</code> environment variable.
    </p>
    <a v-if="remoteUrl" class="button" :href="remoteUrl">Open {{ remote.label }} remote</a>
  </section>
</template>
