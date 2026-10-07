<script setup lang="ts">
const { navigation } = useRemoteNavigation()
const { locale, setLocale } = useLocale()
const { token, logout, restore } = useSession()
onMounted(restore)
</script>

<template>
  <div class="shell">
    <header class="shell-header">
      <NuxtLink class="brand" to="/">Aiyara Timesheet</NuxtLink>
      <nav aria-label="Primary navigation" class="navigation">
        <NuxtLink v-for="item in navigation" :key="item.id" :to="item.path">
          {{ locale === 'th' ? item.label : item.labelEn }}
        </NuxtLink>
      </nav>
      <div class="header-actions">
        <button type="button" @click="setLocale(locale === 'th' ? 'en' : 'th')">{{ locale === 'th' ? 'EN' : 'TH' }}</button>
        <button v-if="token" type="button" @click="logout">{{ locale === 'th' ? 'ออกจากระบบ' : 'Sign out' }}</button>
      </div>
    </header>
    <main class="shell-main">
      <slot />
    </main>
  </div>
</template>
