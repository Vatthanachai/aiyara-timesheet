<script setup lang="ts">
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import DateField from '../../../../shared/components/DateField.vue'
import TimeField from '../../../../shared/components/TimeField.vue'
import { injectTimesheetDates, injectTimesheetText, weekOf, workedMinutes, type BulkDay, type Catalog, type EntryForm, type EntryTemplate } from '../utils/timesheet'

/** Adds an entry to the selected day, or to several chosen days, or edits one chosen from the day or
 * the month list. A template fills the form; the form can be saved as a template. */
const props = defineProps<{ form: EntryForm; catalog: Catalog; monthStart: string; monthEnd: string; editing: boolean
  templates: EntryTemplate[]; bulkDays: BulkDay[] }>()
const several = defineModel<boolean>('several', { required: true })
const bulkDates = defineModel<string[]>('bulkDates', { required: true })
/** The name typed for a new template; null while the name row is closed. */
const templateName = defineModel<string | null>('templateName', { required: true })
const emit = defineEmits<{ save: []; cancel: []; drop: [event: DragEvent]; template: [id: string]; saveTemplate: [] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()

// What the entry will count as work: overnight entries wrap, and the break is left out unless worked.
const projects = computed(() => [{ value: '', label: '—' }, ...props.catalog.projects.map(project => ({ value: project.id, label: project.name }))])
const categories = computed(() => [{ value: '', label: '—' }, ...props.catalog.categories.map(category => ({ value: category.id, label: category.name }))])
const templateOptions = computed(() => props.templates.map(template => ({ value: template.id, label: template.name, hint: template.taskName })))
const templateChoice = ref('')
const durationPreview = computed(() => {
  const duration = workedMinutes(props.form, props.catalog.workHours)
  return duration > 0 ? `${(duration / 60).toFixed(2)} ${text.value.duration}` : ''
})
const bulk = computed(() => several.value && !props.editing)
// Monday-first blanks before the 1st, as the calendar draws the month.
const leadingBlanks = computed(() => (new Date(`${props.monthStart}T00:00:00Z`).getUTCDay() + 6) % 7)
const weekdayLabels = computed(() => labels.value.weekdays.split(','))
const count = computed(() => bulkDates.value.length)

/** Ticks the working days of the selected day's week, or of the whole month, or none. */
function pick(range: 'week' | 'month' | 'none') {
  if (range === 'none') { bulkDates.value = []; return }
  const { start, end } = range === 'week' ? weekOf(props.form.date, props.monthStart, props.monthEnd) : { start: props.monthStart, end: props.monthEnd }
  bulkDates.value = props.bulkDays.filter(day => !day.offDay && day.date >= start && day.date <= end).map(day => day.date)
}
function toggle(day: string, on: boolean) {
  bulkDates.value = on ? [...bulkDates.value, day].sort() : bulkDates.value.filter(value => value !== day)
}
watch(several, on => { if (on) pick('week') })
function useTemplate(id: string | number) {
  emit('template', String(id))
  // The picker is a way in, not a field: it clears once the form is filled.
  void nextTick(() => { templateChoice.value = '' })
}
</script>

<template>
  <section class="entry-panel" :class="{ editing }" aria-labelledby="entry-heading" @dragover.prevent @drop.prevent="emit('drop', $event)">
    <div class="entry-heading">
      <h3 id="entry-heading">{{ editing ? labels.editEntry : text.add }}</h3>
      <span v-if="durationPreview" class="duration-pill">{{ labels.estimate }} <strong>{{ durationPreview }}</strong></span>
    </div>
    <p v-if="!editing" class="subtle-hint">{{ text.drop }}</p>
    <label v-if="!editing && templates.length" class="template-pick">{{ labels.fillFromTemplate }}<AutoComplete v-model="templateChoice" :label="labels.fillFromTemplate" :options="templateOptions" @change="useTemplate" /></label>
    <form class="entry-grid" @submit.prevent="emit('save')">
      <label class="field-wide">{{ text.task }}<input v-model="form.taskName" required maxlength="200" :placeholder="labels.taskPlaceholder" /></label>
      <div class="time-fields"><label>{{ text.start }}<TimeField v-model="form.startTime" :label="text.start" required /></label><span aria-hidden="true">–</span><label>{{ text.end }}<TimeField v-model="form.endTime" :label="text.end" required /></label></div>
      <BreakToggle :form="form" :work-hours="catalog.workHours" />
      <label v-if="!editing" class="several-toggle field-wide"><input v-model="several" type="checkbox" />{{ labels.severalDays }}</label>
      <fieldset v-if="bulk" class="bulk-days field-wide">
        <legend>{{ labels.days }} <span class="bulk-count">{{ labels.daysChosen.replace('{count}', String(count)) }}</span></legend>
        <p class="subtle-hint">{{ labels.severalHint }}</p>
        <div class="bulk-presets"><button class="quiet-button" type="button" @click="pick('week')">{{ labels.pickWeek }}</button><button class="quiet-button" type="button" @click="pick('month')">{{ labels.pickMonth }}</button><button class="quiet-button" type="button" @click="pick('none')">{{ labels.pickNone }}</button></div>
        <div class="bulk-grid">
          <span v-for="weekday in weekdayLabels" :key="weekday" class="bulk-weekday" aria-hidden="true">{{ weekday }}</span>
          <span v-for="blank in leadingBlanks" :key="`blank-${blank}`" aria-hidden="true" />
          <label v-for="day in bulkDays" :key="day.date" class="bulk-day" :class="{ chosen: bulkDates.includes(day.date), 'off-day': day.offDay }" :title="day.note || undefined">
            <input type="checkbox" :checked="bulkDates.includes(day.date)" :aria-label="day.note ? `${dates.date(day.date)}, ${day.note}` : dates.date(day.date)"
              @change="toggle(day.date, ($event.target as HTMLInputElement).checked)" />{{ Number(day.date.slice(-2)) }}</label>
        </div>
      </fieldset>
      <label v-else>{{ text.date }}<DateField v-model="form.date" :label="text.date" required :min="monthStart" :max="monthEnd" /></label>
      <label>{{ text.project }}<AutoComplete v-model="form.projectId" :label="text.project" :options="projects" /></label>
      <label>{{ text.category }}<AutoComplete v-model="form.categoryId" :label="text.category" :options="categories" /></label>
      <label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" :placeholder="labels.detailPlaceholder" /></label>
      <label class="field-wide">{{ text.notes }}<input v-model="form.notes" maxlength="4000" :placeholder="labels.notesPlaceholder" /></label>
      <div class="form-actions"><button class="primary-button" type="submit" :disabled="bulk && !count">{{ editing ? labels.saveChanges : bulk ? labels.addToDays.replace('{count}', String(count)) : text.save }}</button><button class="quiet-button" type="button" @click="emit('cancel')">{{ text.cancel }}</button><button v-if="templateName === null" class="quiet-button" type="button" @click="templateName = ''">{{ labels.saveAsTemplate }}</button></div>
    </form>
    <form v-if="templateName !== null" class="template-save" @submit.prevent="emit('saveTemplate')">
      <label>{{ labels.templateName }}<input v-model="templateName" required maxlength="100" /></label>
      <button class="primary-button" type="submit">{{ labels.saveTemplate }}</button><button class="quiet-button" type="button" @click="templateName = null">{{ text.cancel }}</button>
    </form>
  </section>
</template>
