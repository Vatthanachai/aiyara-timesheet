<script setup lang="ts">
import { decimalHours, injectTimesheetDates, injectTimesheetText, type Catalog, type Entry } from '../utils/timesheet'

/** The month's entries grouped by day, each day with its total. Edit opens the entry in the form. */
const props = defineProps<{ entries: Entry[]; catalog: Catalog; canEdit: boolean; editingId: string | null; heading: string }>()
const emit = defineEmits<{ edit: [entry: Entry]; remove: [id: string] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()

const groups = computed(() => {
  const byDay = new Map<string, Entry[]>()
  for (const entry of [...props.entries].sort((a, b) => a.date.localeCompare(b.date) || a.startTime.localeCompare(b.startTime)))
    byDay.set(entry.date, [...(byDay.get(entry.date) ?? []), entry])
  return [...byDay].map(([day, items]) => ({ day, items, total: items.reduce((sum, entry) => sum + entry.durationMinutes, 0) }))
})
const nameOf = (items: Catalog['projects'], id: string | null) => items.find(item => item.id === id)?.name || '—'
</script>

<template>
  <section class="entries-panel" aria-labelledby="entries-heading">
    <div class="section-heading"><h2 id="entries-heading">{{ heading }}</h2><span class="count-badge">{{ entries.length }}</span></div>
    <div v-if="!entries.length" class="empty-panel"><h3>{{ labels.emptyTitle }}</h3><p>{{ canEdit ? labels.emptyHint : text.readOnly }}</p></div>
    <div v-else class="table-wrap"><table>
      <thead><tr><th>{{ text.start }}–{{ text.end }}</th><th>{{ text.task }}</th><th class="optional-column">{{ text.project }}</th><th class="optional-column">{{ text.category }}</th><th class="number-cell">{{ text.duration }}</th><th><span class="visually-hidden">{{ text.edit }}</span></th></tr></thead>
      <tbody v-for="group in groups" :key="group.day">
        <tr class="day-row"><th colspan="4" scope="rowgroup"><time :datetime="group.day">{{ dates.date(group.day) }}</time></th><td class="number-cell">{{ decimalHours(group.total) }} {{ labels.hoursUnit }}</td><td /></tr>
        <tr v-for="entry in group.items" :key="entry.id" :class="{ editing: editingId === entry.id }">
          <td class="time-cell">{{ dates.time(entry.startTime) }}–{{ dates.time(entry.endTime) }}</td>
          <td class="task-name-cell">{{ entry.taskName }}<span v-if="entry.sync" class="sync-badge" :class="entry.sync">{{ entry.sync === 'failed' ? labels.syncFailed : labels.notSynced }}</span></td>
          <td class="optional-column">{{ nameOf(catalog.projects, entry.projectId) }}</td>
          <td class="optional-column">{{ nameOf(catalog.categories, entry.categoryId) }}</td>
          <td class="number-cell"><strong>{{ (entry.durationMinutes / 60).toFixed(2) }}</strong></td>
          <td class="row-actions"><button v-if="canEdit" class="quiet-button" type="button" @click="emit('edit', entry)">{{ text.edit }}</button><button v-if="canEdit" class="danger-button" type="button" @click="emit('remove', entry.id)">{{ text.remove }}</button></td>
        </tr>
      </tbody>
    </table></div>
  </section>
</template>
