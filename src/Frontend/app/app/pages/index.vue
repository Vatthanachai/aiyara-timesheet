<script setup lang="ts">
const { navigation } = useRemoteNavigation()
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Micro-frontend shell</p>
    <h1>Timesheet workspace</h1>
    <p>
      This shell owns shared navigation, session boundaries, design tokens, and route delegation.
      Product features remain independently deployable remotes.
    </p>
  </section>

  <section aria-label="Available applications" class="remote-grid">
    <NuxtLink v-for="remote in navigation" :key="remote.id" class="remote-card" :to="remote.path">
      <h2>{{ remote.label }}</h2>
      <p>{{ remote.description }}</p>
    </NuxtLink>
  </section>
</template>
