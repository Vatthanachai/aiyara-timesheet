<script setup lang="ts">
import { injectTimesheetDates, injectTimesheetText } from '../utils/timesheet'
import type { ItemError, OutboxItem } from '../utils/offline'

/**
 * What the device holds that the server has not: whether it is offline, how many changes wait, and the
 * changes the server refused, each with Fix and Discard (decisions 8 and 9 of the Mobile And Offline Plan).
 */
const props = defineProps<{ online: boolean; stale: boolean; pending: number; failed: OutboxItem[]; syncing: boolean; retrying: boolean
  /** What a refusal says, in words. */
  explain: (error: ItemError) => string }>()
const emit = defineEmits<{ sync: []; fix: [item: OutboxItem]; discard: [item: OutboxItem] }>()
const { labels } = injectTimesheetText()
const dates = injectTimesheetDates()

const waiting = computed(() => props.pending === 1 ? labels.value.waitingOne : labels.value.waiting.replace('{count}', String(props.pending)))
const attention = computed(() => props.failed.length === 1 ? labels.value.needsAttentionOne : labels.value.needsAttention.replace('{count}', String(props.failed.length)))
const kind = (item: OutboxItem) => ({ create: labels.value.itemCreate, update: labels.value.itemUpdate, delete: labels.value.itemDelete, 'timer-discard': labels.value.itemTimer })[item.kind]
</script>

<template>
  <section v-if="!online || stale || pending || failed.length" class="sync-status" :aria-label="labels.syncStatus" data-testid="sync-status">
    <p v-if="!online" class="sync-line sync-offline" role="status">{{ labels.offlineNow }}</p>
    <p v-else-if="stale" class="sync-line sync-offline" role="status">{{ labels.savedCopy }}</p>
    <div v-if="pending" class="sync-line sync-waiting" role="status">
      <strong>{{ waiting }}</strong>
      <span v-if="syncing" class="sync-note">{{ labels.syncing }}</span>
      <span v-else-if="retrying" class="sync-note">{{ labels.syncRetry }}</span>
      <button class="quiet-button" type="button" :disabled="syncing || !online" @click="emit('sync')">{{ labels.syncNow }}</button>
    </div>
    <div v-if="failed.length" class="sync-failed">
      <p class="sync-line sync-attention" role="alert"><strong>{{ attention }}</strong></p>
      <ul>
        <li v-for="item in failed" :key="item.seq">
          <span class="sync-item"><strong>{{ kind(item) }}: {{ item.summary.taskName }}</strong>
            <small v-if="item.summary.date">{{ dates.date(item.summary.date) }} {{ dates.time(item.summary.startTime) }}–{{ dates.time(item.summary.endTime) }}</small></span>
          <span class="sync-reason">{{ item.error ? explain(item.error) : '' }}</span>
          <span class="row-actions">
            <button v-if="item.kind === 'create' || item.kind === 'update'" class="quiet-button" type="button" @click="emit('fix', item)">{{ labels.fixItem }}</button>
            <button class="danger-button" type="button" @click="emit('discard', item)">{{ labels.discardItem }}</button>
          </span>
        </li>
      </ul>
    </div>
  </section>
</template>
