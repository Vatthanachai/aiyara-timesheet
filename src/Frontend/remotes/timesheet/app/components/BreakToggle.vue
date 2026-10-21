<script setup lang="ts">
import { breakOverlapMinutes, injectTimesheetDates, injectTimesheetText, type EntryForm, type WorkHours } from '../utils/timesheet'

/** Offers "worked through the break" only when the entry overlaps the organization's break, and
 * says how much is deducted while it is not ticked. */
const props = defineProps<{ form: EntryForm; workHours: WorkHours }>()
const { labels } = injectTimesheetText()
const dates = injectTimesheetDates()
const overlap = computed(() => breakOverlapMinutes(props.form.startTime, props.form.endTime, props.workHours))
const label = computed(() => labels.value.includesBreak
  .replace('{start}', dates.value.time(props.workHours.breakStart ?? ''))
  .replace('{end}', dates.value.time(props.workHours.breakEnd ?? '')))
// Leaving the break behind unticks the box, so a later overlap starts from "deducted" again.
watch(overlap, value => { if (!value) props.form.includesBreak = false })
</script>

<template>
  <div v-if="overlap" class="break-toggle field-wide">
    <label><input v-model="form.includesBreak" type="checkbox" />{{ label }}</label>
    <small v-if="!form.includesBreak">{{ labels.breakDeducted.replace('{minutes}', String(overlap)) }}</small>
  </div>
</template>
