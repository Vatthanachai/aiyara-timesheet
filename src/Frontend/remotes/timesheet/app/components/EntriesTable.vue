<script setup lang="ts">
import { injectTimesheetDates, injectTimesheetText, type Catalog, type Entry, type EntryForm } from '../utils/timesheet'

defineProps<{ entries: Entry[]; catalog: Catalog; canEdit: boolean; editingId: string | null; form: EntryForm; monthLabel: string; monthStart: string; monthEnd: string }>()
const emit = defineEmits<{ edit: [entry: Entry]; remove: [id: string]; save: []; cancel: [] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()
</script>

<template>
  <section class="panel entries-panel">
    <div class="panel-heading"><div><span class="eyebrow">{{ monthLabel }}</span><h2>{{ labels.entryList }}</h2></div><span class="count-badge">{{ entries.length }}</span></div>
    <div v-if="!entries.length" class="empty-panel"><span aria-hidden="true">◷</span><h3>{{ labels.emptyTitle }}</h3><p>{{ canEdit ? labels.emptyHint : text.readOnly }}</p></div>
    <div v-else class="table-wrap"><table><thead><tr><th>{{ text.date }}</th><th>{{ text.start }}–{{ text.end }}</th><th>{{ text.duration }}</th><th>{{ text.task }}</th><th class="optional-column">{{ text.project }}</th><th class="optional-column">{{ text.category }}</th><th><span class="visually-hidden">{{ text.edit }}</span></th></tr></thead>
      <tbody><template v-for="entry in entries" :key="entry.id"><tr><td><time :datetime="entry.date">{{ dates.date(entry.date) }}</time></td><td>{{ dates.time(entry.startTime) }}–{{ dates.time(entry.endTime) }}</td><td><strong>{{ (entry.durationMinutes / 60).toFixed(2) }}</strong></td><td class="task-name-cell">{{ entry.taskName }}</td><td class="optional-column">{{ catalog.projects.find(x => x.id === entry.projectId)?.name || '—' }}</td><td class="optional-column">{{ catalog.categories.find(x => x.id === entry.categoryId)?.name || '—' }}</td><td class="row-actions"><button v-if="canEdit" class="quiet-button" type="button" @click="emit('edit', entry)">{{ text.edit }}</button><button v-if="canEdit" class="danger-button" type="button" @click="emit('remove', entry.id)">{{ text.remove }}</button></td></tr>
      <tr v-if="editingId === entry.id"><td colspan="7"><form class="entry-grid edit-grid" @submit.prevent="emit('save')"><label>{{ text.date }}<input v-model="form.date" type="date" required :min="monthStart" :max="monthEnd" /></label><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label><BreakToggle :form="form" :work-hours="catalog.workHours" /><label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" /></label><label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label><label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label><label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" /></label><label>{{ text.notes }}<input v-model="form.notes" maxlength="4000" /></label><div class="form-actions"><button class="primary-button" type="submit">{{ text.save }}</button><button class="quiet-button" type="button" @click="emit('cancel')">{{ text.cancel }}</button></div></form></td></tr></template></tbody></table></div>
  </section>
</template>
