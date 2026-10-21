<script setup lang="ts">
/**
 * Hours logged against what the month expects: the tinted track is the expectation, the blue fill
 * what was logged, amber anything beyond it, and an ink tick what was expected by today.
 */
const props = defineProps<{ logged: number; expected: number; toDate?: number | null; label: string }>()

// The bar spans the larger of the two, so overtime extends past the end of the track.
const scale = computed(() => Math.max(props.logged, props.expected, 1))
const percent = (minutes: number) => `${(minutes / scale.value) * 100}%`
const within = computed(() => Math.min(props.logged, props.expected))
const over = computed(() => Math.max(props.logged - props.expected, 0))
</script>

<template>
  <span class="hours-bar" role="img" :aria-label="label">
    <span class="hours-track" :style="{ width: percent(expected) }" />
    <span class="hours-logged" :style="{ width: percent(within) }" />
    <span v-if="over" class="hours-over" :style="{ left: percent(expected), width: percent(over) }" />
    <span v-if="toDate != null && toDate > 0 && toDate < expected" class="hours-today" :style="{ left: percent(toDate) }" />
  </span>
</template>

<style scoped>
.hours-bar { display: block; height: .75rem; min-width: 6rem; position: relative; }
.hours-bar > span { border-radius: .2rem; bottom: 0; position: absolute; top: 0; }
.hours-track { background: var(--color-work-track); left: 0; }
.hours-logged { background: var(--color-brand); left: 0; }
.hours-over { background: var(--color-overtime); border-radius: 0 .2rem .2rem 0; }
.hours-bar > .hours-today { border-left: 2px solid var(--color-ink); border-radius: 0; bottom: -.2rem; top: -.2rem; width: 0; }
</style>
