<script setup lang="ts">
import { injectTimesheetText, workedMinutes, type Catalog, type EntryForm } from '../utils/timesheet'

/** Adds an entry to the selected day, or edits one chosen from the day or the month list. */
const props = defineProps<{ form: EntryForm; catalog: Catalog; monthStart: string; monthEnd: string; editing: boolean }>()
const emit = defineEmits<{ save: []; cancel: []; drop: [event: DragEvent] }>()
const { text, labels } = injectTimesheetText()

// What the entry will count as work: overnight entries wrap, and the break is left out unless worked.
const durationPreview = computed(() => {
  const duration = workedMinutes(props.form, props.catalog.workHours)
  return duration > 0 ? `${(duration / 60).toFixed(2)} ${text.value.duration}` : ''
})
</script>

<template>
  <section class="entry-panel" :class="{ editing }" aria-labelledby="entry-heading" @dragover.prevent @drop.prevent="emit('drop', $event)">
    <div class="entry-heading">
      <h3 id="entry-heading">{{ editing ? labels.editEntry : text.add }}</h3>
      <span v-if="durationPreview" class="duration-pill">{{ labels.estimate }} <strong>{{ durationPreview }}</strong></span>
    </div>
    <p v-if="!editing" class="subtle-hint">{{ text.drop }}</p>
    <form class="entry-grid" @submit.prevent="emit('save')">
      <label class="field-wide">{{ text.task }}<input v-model="form.taskName" required maxlength="200" :placeholder="labels.taskPlaceholder" /></label>
      <div class="time-fields"><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><span aria-hidden="true">–</span><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label></div>
      <BreakToggle :form="form" :work-hours="catalog.workHours" />
      <label>{{ text.date }}<input v-model="form.date" type="date" required :min="monthStart" :max="monthEnd" /></label>
      <label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label>
      <label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label>
      <label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" :placeholder="labels.detailPlaceholder" /></label>
      <label class="field-wide">{{ text.notes }}<input v-model="form.notes" maxlength="4000" :placeholder="labels.notesPlaceholder" /></label>
      <div class="form-actions"><button class="primary-button" type="submit">{{ editing ? labels.saveChanges : text.save }}</button><button class="quiet-button" type="button" @click="emit('cancel')">{{ text.cancel }}</button></div>
    </form>
  </section>
</template>
