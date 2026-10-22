<script setup lang="ts">
import { addDays, injectTimesheetDates, injectTimesheetText, weekOf } from '../utils/timesheet'

/** Copies the day before onto the selected day, or the week before onto the selected day's week
 * (kept within the month). The API skips days off, holidays, whole-day leave, and days with entries. */
const props = defineProps<{ day: string; monthStart: string; monthEnd: string; busy: boolean }>()
const emit = defineEmits<{ copy: [request: { from: string; to: string; days: number }] }>()
const { labels } = injectTimesheetText()
const dates = injectTimesheetDates()

const week = computed(() => {
  const { start, end } = weekOf(props.day, props.monthStart, props.monthEnd)
  const days = (Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86_400_000 + 1
  return { from: addDays(start, -7), to: start, days, end }
})
const previousDay = computed(() => addDays(props.day, -1))
const span = (start: string, end: string) => start === end ? dates.value.date(start) : `${dates.value.date(start)}–${dates.value.date(end)}`
const dayTitle = computed(() => labels.value.copyFrom.replace('{from}', dates.value.date(previousDay.value)).replace('{to}', dates.value.date(props.day)))
const weekTitle = computed(() => labels.value.copyFrom.replace('{from}', span(week.value.from, addDays(week.value.from, week.value.days - 1)))
  .replace('{to}', span(week.value.to, week.value.end)))
</script>

<template>
  <div class="copy-days" role="group" :aria-label="labels.copyLabel">
    <div class="copy-actions">
      <button class="quiet-button" type="button" :disabled="busy" :title="dayTitle"
        @click="emit('copy', { from: previousDay, to: day, days: 1 })">{{ labels.copyPreviousDay }}</button>
      <button class="quiet-button" type="button" :disabled="busy" :title="weekTitle"
        @click="emit('copy', { from: week.from, to: week.to, days: week.days })">{{ labels.copyPreviousWeek }}</button>
    </div>
    <p class="subtle-hint">{{ labels.copyHint }}</p>
  </div>
</template>
