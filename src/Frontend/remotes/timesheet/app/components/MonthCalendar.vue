<script setup lang="ts">
import { decimalHours, injectTimesheetText } from '../utils/timesheet'

/**
 * The month as a time card: weeks start on Monday and each day fills from the bottom in
 * proportion to the normal working day, so the month reads as a chart of hours worked.
 */
const props = defineProps<{
  month: string
  selected: string
  today: string
  minutesByDay: Map<string, number>
  leaveByDay: Map<string, string>
  holidayByDay: Map<string, string>
  workingDays: number[]
  standardMinutes: number
}>()
const emit = defineEmits<{ select: [day: string]; drop: [event: DragEvent, day: string] }>()
const { text, labels } = injectTimesheetText()

const days = computed(() => Array.from({ length: new Date(Number(props.month.slice(0, 4)), Number(props.month.slice(5)), 0).getDate() },
  (_, index) => `${props.month}-${String(index + 1).padStart(2, '0')}`))
const weekdayOf = (day: string) => new Date(`${day}T12:00:00`).getDay()
// Monday-first blanks before the 1st.
const leadingBlanks = computed(() => (weekdayOf(`${props.month}-01`) + 6) % 7)
// Blanks after the last day complete its week, so the grid has no gap.
const trailingBlanks = computed(() => (7 - (leadingBlanks.value + days.value.length) % 7) % 7)
const weekdayLabels = computed(() => labels.value.weekdays.split(','))
const cells = computed(() => days.value.map(day => {
  const minutes = props.minutesByDay.get(day) ?? 0
  return {
    day, minutes,
    number: Number(day.slice(-2)),
    // Days the organization does not work (Date.getDay() numbers outside its work week).
    weekend: !props.workingDays.includes(weekdayOf(day)),
    holiday: props.holidayByDay.get(day),
    leave: props.leaveByDay.get(day),
    overtime: minutes > props.standardMinutes,
    fill: Math.min(minutes / Math.max(props.standardMinutes, 1), 1)
  }
}))
</script>

<template>
  <section class="calendar-panel" :aria-label="text.chooseDay">
    <div class="calendar-grid">
      <span v-for="weekday in weekdayLabels" :key="weekday" class="weekday" aria-hidden="true">{{ weekday }}</span>
      <span v-for="blank in leadingBlanks" :key="`blank-${blank}`" class="calendar-blank" aria-hidden="true" />
      <button v-for="cell in cells" :key="cell.day" type="button" class="day-button"
        :class="{ selected: selected === cell.day, today: today === cell.day, weekend: cell.weekend, holiday: cell.holiday,
          'has-entry': cell.minutes > 0, 'has-leave': cell.leave, overtime: cell.overtime }"
        :aria-pressed="selected === cell.day" :aria-label="`${text.date} ${cell.day}`"
        @click="emit('select', cell.day)" @dragover.prevent @drop.prevent="emit('drop', $event, cell.day)">
        <span v-if="cell.minutes" class="day-fill" :style="{ '--fill': cell.fill }" aria-hidden="true" />
        <span class="day-number">{{ cell.number }}</span>
        <span v-if="cell.minutes" class="day-hours" :title="cell.overtime ? labels.overtime : undefined">{{ decimalHours(cell.minutes) }} {{ labels.hoursUnit }}</span>
        <span v-if="cell.holiday" class="holiday-marker">{{ cell.holiday }}</span>
        <span v-if="cell.leave" class="leave-marker">{{ cell.leave }}</span>
      </button>
      <span v-for="blank in trailingBlanks" :key="`end-${blank}`" class="calendar-blank" aria-hidden="true" />
    </div>
  </section>
</template>
