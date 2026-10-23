<script setup lang="ts">
import DayRuler, { type RulerBand } from '../../../../shared/components/DayRuler.vue'
import { clockMinutes, decimalHours, injectTimesheetDates, injectTimesheetText, type Catalog, type Entry } from '../utils/timesheet'

/** The selected day: its hours on a ruler, then its entries. The entry form follows it in the page. */
const props = defineProps<{
  day: string
  heading: string
  entries: Entry[]
  catalog: Catalog
  canEdit: boolean
  editingId: string | null
  /** A holiday's name, a leave kind, or "Day off", when the day is not a working day. */
  note: string
}>()
const emit = defineEmits<{ edit: [entry: Entry]; remove: [id: string] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()

const total = computed(() => props.entries.reduce((sum, entry) => sum + entry.durationMinutes, 0))
const bands = computed<RulerBand[]>(() => props.entries.map(entry => {
  const start = clockMinutes(entry.startTime)
  const end = clockMinutes(entry.endTime)
  // An overnight entry is drawn up to midnight.
  return { start, end: end > start ? end : 24 * 60, label: entry.taskName }
}))
const hours = computed(() => props.catalog.workHours)
// The hour on its own, in the organization's clock: "09" for 24-hour, "9" for 12-hour.
const hourLabel = (hour: number) => {
  const padded = String(hour % 24).padStart(2, '0')
  return /[AP]M/.test(dates.value.time(`${padded}:00`)) ? String(hour % 12 || 12) : padded
}
const projectName = (entry: Entry) => props.catalog.projects.find(project => project.id === entry.projectId)?.name
</script>

<template>
  <section class="day-panel" aria-labelledby="day-heading">
    <header class="day-heading">
      <h2 id="day-heading">{{ heading }}</h2>
      <strong v-if="total" class="day-total">{{ decimalHours(total) }} {{ labels.hoursUnit }}</strong>
    </header>
    <p v-if="note" class="day-note">{{ note }}</p>
    <!-- Keyed by day so the bands grow in again when another day is chosen. -->
    <DayRuler :key="day" class="day-ruler" :work-start="clockMinutes(hours.startTime)" :work-end="clockMinutes(hours.endTime)"
      :break-start="hours.breakStart ? clockMinutes(hours.breakStart) : null" :break-end="hours.breakEnd ? clockMinutes(hours.breakEnd) : null"
      :bands="bands" :hour-label="hourLabel" animate />
    <ol v-if="entries.length" class="day-entries">
      <li v-for="entry in entries" :key="entry.id" :class="{ editing: editingId === entry.id }">
        <span class="day-entry-time">{{ dates.time(entry.startTime) }}–{{ dates.time(entry.endTime) }}</span>
        <span class="day-entry-task"><strong>{{ entry.taskName }}<span v-if="entry.sync" class="sync-badge" :class="entry.sync">{{ entry.sync === 'failed' ? labels.syncFailed : labels.notSynced }}</span></strong><small v-if="projectName(entry)">{{ projectName(entry) }}</small></span>
        <span class="day-entry-hours">{{ decimalHours(entry.durationMinutes) }} {{ labels.hoursUnit }}</span>
        <span v-if="canEdit" class="row-actions">
          <button class="quiet-button" type="button" :aria-label="`${text.edit} ${entry.taskName}`" @click="emit('edit', entry)">{{ text.edit }}</button>
          <button class="danger-button" type="button" :aria-label="`${text.remove} ${entry.taskName}`" @click="emit('remove', entry.id)">{{ text.remove }}</button>
        </span>
      </li>
    </ol>
    <p v-else class="day-empty">{{ canEdit ? labels.dayEmpty : labels.dayEmptyReadOnly }}</p>
  </section>
</template>
