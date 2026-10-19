<script setup lang="ts">
import type { Locale } from '../../../../shared/i18n'
import { injectTimesheetText } from '../utils/timesheet'

const props = defineProps<{ month: string; monthLabel: string; locale: Locale; entryDays: Set<string>; leaveDays: Set<string>; holidayDays: Set<string> }>()
const selected = defineModel<string>('selected', { required: true })
const emit = defineEmits<{ drop: [event: DragEvent, day: string] }>()
const { text, labels } = injectTimesheetText()

const days = computed(() => Array.from({ length: new Date(Number(props.month.slice(0, 4)), Number(props.month.slice(5)), 0).getDate() },
  (_, index) => `${props.month}-${String(index + 1).padStart(2, '0')}`))
const weekendDays = computed(() => new Set(days.value.filter(day => {
  const weekday = new Date(`${day}T12:00:00`).getDay()
  return weekday === 0 || weekday === 6
})))
const calendarCells = computed(() => [
  ...Array.from({ length: new Date(Number(props.month.slice(0, 4)), Number(props.month.slice(5)) - 1, 1).getDay() }, () => null),
  ...days.value
])
const weekdayLabels = computed(() => props.locale === 'th'
  ? ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส']
  : ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'])
</script>

<template>
  <section class="panel calendar-panel">
    <div class="panel-heading"><div><span class="eyebrow">{{ labels.selectedDate }}</span><h2>{{ monthLabel }}</h2></div><div class="calendar-legend"><span><i class="legend-work"></i>{{ labels.entryList }}</span><span><i class="legend-leave"></i>{{ text.leaves }}</span></div></div>
    <div class="calendar-grid" :aria-label="text.chooseDay">
      <span v-for="(weekday, index) in weekdayLabels" :key="index" class="weekday">{{ weekday }}</span>
      <span v-for="(day, index) in calendarCells" :key="day || `blank-${index}`" class="calendar-cell">
        <button v-if="day" type="button" class="day-button" :class="{ selected: selected === day, weekend: weekendDays.has(day), 'has-entry': entryDays.has(day), 'has-leave': leaveDays.has(day), holiday: holidayDays.has(day) }" :aria-pressed="selected === day" :aria-label="`${text.date} ${day}`" @click="selected = day" @dragover.prevent @drop.prevent="emit('drop', $event, day)"><span>{{ Number(day.slice(-2)) }}</span><i v-if="entryDays.has(day) || leaveDays.has(day) || holidayDays.has(day)" class="day-markers" aria-hidden="true"><b v-if="entryDays.has(day)" class="entry-marker"></b><b v-if="leaveDays.has(day)" class="leave-marker"></b><b v-if="holidayDays.has(day)" class="holiday-marker"></b></i></button>
      </span>
    </div>
  </section>
</template>
