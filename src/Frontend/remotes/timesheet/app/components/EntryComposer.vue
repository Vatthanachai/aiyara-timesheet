<script setup lang="ts">
import { injectTimesheetDates, injectTimesheetText, workedMinutes, type Catalog, type EntryForm } from '../utils/timesheet'

const props = defineProps<{ form: EntryForm; catalog: Catalog; monthStart: string; monthEnd: string }>()
const emit = defineEmits<{ save: []; cancel: []; drop: [event: DragEvent] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()

// What the entry will count as work: overnight entries wrap, and the break is left out unless worked.
const durationPreview = computed(() => {
  const duration = workedMinutes(props.form, props.catalog.workHours)
  return duration > 0 ? `${(duration / 60).toFixed(2)} ${text.value.duration}` : ''
})
</script>

<template>
  <section class="panel entry-panel" @dragover.prevent @drop.prevent="emit('drop', $event)">
    <div class="panel-heading"><div><span class="eyebrow">{{ text.add }}</span><h2>{{ labels.selectedDate }} · {{ dates.date(form.date) }}</h2></div><span v-if="durationPreview" class="duration-pill">{{ labels.estimate }} <strong>{{ durationPreview }}</strong></span></div>
    <p class="subtle-hint">{{ text.drop }}</p>
    <form class="entry-grid" @submit.prevent="emit('save')">
      <label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" :placeholder="labels.taskPlaceholder" /></label>
      <div class="time-fields"><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><span aria-hidden="true">→</span><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label></div>
      <BreakToggle :form="form" :work-hours="catalog.workHours" />
      <label>{{ text.date }}<input v-model="form.date" type="date" required :min="monthStart" :max="monthEnd" /></label>
      <label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label>
      <label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label>
      <label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" :placeholder="labels.detailPlaceholder" /></label>
      <label class="field-wide">{{ text.notes }}<input v-model="form.notes" maxlength="4000" :placeholder="labels.notesPlaceholder" /></label>
      <div class="form-actions"><button class="primary-button" type="submit"><span aria-hidden="true">＋</span>{{ text.save }}</button><button class="quiet-button" type="button" @click="emit('cancel')">{{ text.cancel }}</button></div>
    </form>
  </section>
</template>
