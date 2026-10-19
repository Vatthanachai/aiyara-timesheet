<script setup lang="ts">
import { injectTimesheetText } from '../utils/timesheet'

defineProps<{ totalHours: number; entryCount: number; activeDays: number; leaveCount: number }>()
const { text, labels } = injectTimesheetText()
</script>

<template>
  <section class="summary-grid" :aria-label="labels.allMonth">
    <article class="summary-card summary-hours"><span class="summary-icon" aria-hidden="true">◷</span><div><p>{{ labels.hoursLogged }}</p><strong>{{ totalHours.toFixed(2) }} <small>{{ text.duration }}</small></strong></div></article>
    <article class="summary-card"><span class="summary-icon" aria-hidden="true">▤</span><div><p>{{ labels.entryList }}</p><strong>{{ entryCount }}</strong></div></article>
    <article class="summary-card"><span class="summary-icon" aria-hidden="true">▦</span><div><p>{{ labels.daysTracked }}</p><strong>{{ activeDays }}</strong></div></article>
    <article class="summary-card"><span class="summary-icon" aria-hidden="true">☼</span><div><p>{{ labels.leaveDays }}</p><strong>{{ leaveCount }}</strong></div></article>
  </section>
</template>
