<script setup lang="ts">
import { injectTimesheetText, type MonthState } from '../utils/timesheet'

/** The month's approval status, the reason it was returned, and Submit when the member can send it. */
const props = defineProps<{ state: MonthState; canSubmit: boolean; busy: boolean }>()
const emit = defineEmits<{ submit: [] }>()
const { labels } = injectTimesheetText()
const shown = computed(() => props.state.locked ? 'locked' : props.state.status)
const statusLabel = computed(() => ({ open: labels.value.statusOpen, submitted: labels.value.statusSubmitted,
  approved: labels.value.statusApproved, returned: labels.value.statusReturned, locked: labels.value.statusLocked })[shown.value])
const hint = computed(() => ({ open: labels.value.submitHint, submitted: labels.value.submittedHint,
  approved: labels.value.approvedHint, returned: labels.value.returnedHint, locked: labels.value.lockedHint })[shown.value])
</script>

<template>
  <section class="approval-bar" :data-status="shown" aria-labelledby="approval-status">
    <div class="approval-text">
      <p><span id="approval-status" class="approval-pill">{{ statusLabel }}</span> {{ hint }}</p>
      <p v-if="state.status === 'returned' && state.returnReason && !state.locked" class="return-reason">
        <strong>{{ labels.returnReason }}:</strong> {{ state.returnReason }}</p>
    </div>
    <button v-if="canSubmit" class="primary-button" type="button" :disabled="busy" @click="emit('submit')">
      {{ state.status === 'returned' ? labels.resubmitMonth : labels.submitMonth }}</button>
  </section>
</template>
