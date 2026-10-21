<script setup lang="ts">
import { isWorkingDate, monthDates, type WorkSchedule } from '../utils/expectedHours'

/**
 * A member's month as one bar per day, each filled against the normal working day: the same
 * picture as Timesheet's time card, small enough to sit above the entries.
 */
const props = defineProps<{
  month: string
  minutesByDay: Map<string, number>
  leaveDays: Set<string>
  schedule: WorkSchedule
  label: string
  /** The day's own label for its tooltip, e.g. "Mon 05/10/2026: 5:30". */
  describe: (date: string, minutes: number) => string
}>()

const days = computed(() => monthDates(props.month).map(date => {
  const minutes = props.minutesByDay.get(date) ?? 0
  return {
    date, minutes,
    number: Number(date.slice(-2)),
    off: !isWorkingDate(date, props.schedule),
    leave: props.leaveDays.has(date),
    over: minutes > props.schedule.standardMinutes,
    fill: `${Math.min(minutes / Math.max(props.schedule.standardMinutes, 1), 1) * 100}%`
  }
}))
</script>

<template>
  <figure class="month-strip">
    <ol role="img" :aria-label="label">
      <li v-for="day in days" :key="day.date" :class="{ off: day.off, leave: day.leave, over: day.over }" :title="describe(day.date, day.minutes)">
        <span class="strip-bar"><span v-if="day.minutes" :style="{ height: day.fill }" /></span>
        <span class="strip-date">{{ day.number }}</span>
      </li>
    </ol>
  </figure>
</template>

<style scoped>
.month-strip { margin: 0 0 1.25rem; overflow-x: auto; }
.month-strip ol { display: grid; gap: 2px; grid-auto-columns: minmax(.9rem, 1fr); grid-auto-flow: column; list-style: none; margin: 0; min-width: 20rem; padding: 0; }
.month-strip li { display: grid; gap: .25rem; justify-items: center; }
.strip-bar { align-items: flex-end; background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .2rem; display: flex; height: 3.5rem; overflow: hidden; width: 100%; }
.strip-bar span { background: var(--color-work-track); border-top: 2px solid var(--color-brand); display: block; width: 100%; }
.over .strip-bar span { border-top-color: var(--color-overtime); }
.off .strip-bar { background: var(--color-day-off); border-color: var(--color-day-off); }
.leave .strip-bar { background: repeating-linear-gradient(135deg, var(--color-day-off) 0 3px, #d7f0ec 3px 6px); border-color: #b9e1db; }
.strip-date { color: var(--color-muted); font-family: var(--font-display); font-size: .7rem; font-variant-numeric: tabular-nums; }
.off .strip-date { color: #a3adbd; }
</style>
