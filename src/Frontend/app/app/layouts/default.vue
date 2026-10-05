<script setup lang="ts">
const { navigation } = useRemoteNavigation()
</script>

<template>
  <div class="shell">
    <header class="shell-header">
      <NuxtLink class="brand" to="/">Aiyara Timesheet</NuxtLink>
      <nav aria-label="Primary navigation" class="navigation">
        <NuxtLink v-for="item in navigation" :key="item.id" :to="item.path">
          {{ item.label }}
        </NuxtLink>
      </nav>
    </header>
    <main class="shell-main">
      <slot />
    </main>
  </div>
</template>
