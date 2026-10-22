<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { safeError, type Locale } from '../api-response'
import { formatDate, type Formats } from '../date-format'
import { useLocaleText } from '../i18n'
import DateField from './DateField.vue'
import TimeField from './TimeField.vue'

/**
 * The member's running timer, shared by Today (the Shell) and Timesheet (a remote). The server
 * keeps the one timer, so it is read on load and counted from the server's `nowUtc`, not the
 * device's clock alone; a reload picks it up again. Stopping creates the entry on the server and
 * emits `stopped` so the page can reload its entries. The page supplies `api`, which calls the
 * Timesheet API under /timer with the page's own session and returns the unwrapped result.
 */
export type RunningTimer = { id: string; startedAtUtc: string; taskName: string | null; detail: string | null; notes: string | null
  projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak: boolean }
type TimerState = { timer: RunningTimer | null; nowUtc: string }
const props = defineProps<{
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
  locale: Locale
  formats: Formats
  /** False when the member cannot add time now (a closed month): a timer cannot be started. */
  canEdit?: boolean
  /** The times offered when the member chooses them by hand. */
  defaultTimes?: { startTime: string; endTime: string }
  /** The browser's local date, for the by-hand form. */
  today: string
}>()
const emit = defineEmits<{ stopped: [] }>()

const text = useLocaleText({ get value() { return props.locale } }, {
  th: {
    heading: 'จับเวลา', hint: 'เริ่มจับเวลาเมื่อเริ่มงาน แล้วหยุดเพื่อสร้างรายการเวลา ช่วงพักขององค์กรไม่นับ',
    task: 'งานที่ทำ', taskPlaceholder: 'เช่น ประชุมทีม (ใส่ตอนหยุดก็ได้)', start: 'เริ่มจับเวลา', stop: 'หยุดและบันทึก', discard: 'ทิ้งตัวจับเวลา',
    running: 'กำลังจับเวลา', elapsed: 'เวลาที่ผ่านไป', loading: 'กำลังโหลดตัวจับเวลา…', loadFailed: 'โหลดตัวจับเวลาไม่สำเร็จ', retry: 'ลองอีกครั้ง',
    readOnly: 'เดือนนี้แก้ไขไม่ได้ จึงเริ่มจับเวลาไม่ได้',
    started: 'เริ่มจับเวลาแล้ว', stoppedNotice: 'หยุดและบันทึกรายการเวลาแล้ว', discarded: 'ทิ้งตัวจับเวลาแล้ว',
    confirmDiscard: 'ทิ้งตัวจับเวลานี้? เวลาที่จับไว้จะไม่ถูกบันทึก', needName: 'ใส่ชื่องานก่อนหยุดจับเวลา',
    chooseTimes: 'เลือกวันและเวลาเอง', hideTimes: 'ซ่อนการเลือกเวลา', stopWithTimes: 'หยุดด้วยวันและเวลานี้', date: 'วันที่', startTime: 'เริ่ม', endTime: 'สิ้นสุด',
    includesBreak: 'ทำงานในช่วงพัก (นับเป็นเวลาทำงาน)', fixHint: 'ตัวจับเวลายังอยู่ เลือกวันและเวลาที่ถูกต้อง หรือทิ้งตัวจับเวลา',
    monthClosed: 'เดือนของวันที่เริ่มจับเวลาแก้ไขไม่ได้แล้ว', monthClosedDays: 'แก้ไขวันเหล่านี้ไม่ได้: {dates}',
    tooLong: 'จับเวลานานเกิน 24 ชั่วโมง รายการเวลายาวได้ไม่เกิน 24 ชั่วโมง ให้เลือกเวลาที่ถูกต้อง หรือทิ้งตัวจับเวลา',
    tooShort: 'จับเวลาไม่ถึงหนึ่งนาที หรือเวลาสิ้นสุดไม่ถูกต้อง รอให้ครบหนึ่งนาที เลือกเวลาเอง หรือทิ้งตัวจับเวลา',
    inBreak: 'ช่วงเวลานี้อยู่ในช่วงพักทั้งหมด ถ้าทำงานจริงให้เลือก "ทำงานในช่วงพัก" หรือเลือกเวลาใหม่',
    checkInput: 'กรุณาตรวจสอบข้อมูลที่กรอก', alreadyRunning: 'มีตัวจับเวลาที่กำลังทำงานอยู่แล้ว', noTimer: 'ไม่มีตัวจับเวลาที่กำลังทำงาน'
  },
  en: {
    heading: 'Timer', hint: 'Start the timer when you begin work, then stop it to create a time entry. The organization break is left out.',
    task: 'Task', taskPlaceholder: 'For example Team meeting (you can add it when you stop)', start: 'Start timer', stop: 'Stop and save', discard: 'Discard timer',
    running: 'Timer running', elapsed: 'Elapsed time', loading: 'Loading the timer…', loadFailed: 'The timer could not be loaded', retry: 'Try again',
    readOnly: 'This month cannot be changed, so a timer cannot be started.',
    started: 'Timer started', stoppedNotice: 'Timer stopped and the time entry saved', discarded: 'Timer discarded',
    confirmDiscard: 'Discard this timer? The time it has counted will not be saved.', needName: 'Enter a task name before stopping the timer.',
    chooseTimes: 'Choose date and times', hideTimes: 'Hide date and times', stopWithTimes: 'Stop with these times', date: 'Date', startTime: 'Start', endTime: 'End',
    includesBreak: 'Worked through the break (counts as work)', fixHint: 'The timer is still running. Choose the right date and times, or discard the timer.',
    monthClosed: 'The month the timer started in cannot be changed any more.', monthClosedDays: 'These days cannot be changed: {dates}.',
    tooLong: 'The timer has run for 24 hours or more, and an entry can be at most 24 hours. Choose the right times, or discard the timer.',
    tooShort: 'The timer has run under a minute, or the end time is not valid. Wait a minute, choose the times yourself, or discard the timer.',
    inBreak: 'This time falls entirely in the break. If you worked then, tick "Worked through the break", or choose other times.',
    checkInput: 'Please check the entered information.', alreadyRunning: 'A timer is already running.', noTimer: 'No timer is running.'
  }
})

const state = ref<TimerState | null>(null)
const loaded = ref(false)
const loadFailed = ref(false)
const busy = ref(false)
const error = ref('')
const notice = ref('')
const name = ref('')
const byHand = ref(false)
const fix = reactive({ date: props.today, startTime: props.defaultTimes?.startTime ?? '09:00', endTime: props.defaultTimes?.endTime ?? '17:00', includesBreak: false })
const nameField = ref<HTMLInputElement | null>(null)
const clock = ref(Date.now())
/** Server time minus the device's clock at the moment it answered. */
let skew = 0
let ticker: ReturnType<typeof setInterval> | undefined

const timer = computed(() => state.value?.timer ?? null)
const elapsedMs = computed(() => timer.value ? Math.max(0, clock.value + skew - Date.parse(timer.value.startedAtUtc)) : 0)
const pad = (value: number) => String(value).padStart(2, '0')
const elapsed = computed(() => {
  const seconds = Math.floor(elapsedMs.value / 1000)
  return `${Math.floor(seconds / 3600)}:${pad(Math.floor(seconds / 60) % 60)}:${pad(seconds % 60)}`
})
// Announced a minute at a time, so a screen reader is not read the seconds.
const elapsedSpoken = computed(() => {
  const minutes = Math.floor(elapsedMs.value / 60_000)
  const [hours, rest] = [Math.floor(minutes / 60), minutes % 60]
  return props.locale === 'th' ? `${text.value.elapsed} ${hours} ชั่วโมง ${rest} นาที` : `${text.value.elapsed} ${hours} h ${rest} min`
})

function apply(next: TimerState | null | undefined) {
  state.value = next ?? { timer: null, nowUtc: new Date().toISOString() }
  const server = Date.parse(state.value.nowUtc)
  skew = Number.isNaN(server) ? 0 : server - Date.now()
  clock.value = Date.now()
  name.value = state.value.timer ? (state.value.timer.taskName ?? name.value) : ''
  if (!state.value.timer) byHand.value = false
}
// The timer is an extra: a failed read leaves the rest of the page as it was.
async function load() {
  try { apply(await props.api<TimerState>('/timer')); loadFailed.value = false }
  catch { loadFailed.value = true }
  finally { loaded.value = true }
}
onMounted(() => {
  void load()
  ticker = setInterval(() => { clock.value = Date.now() }, 1000)
})
onBeforeUnmount(() => { if (ticker) clearInterval(ticker) })

const codeOf = (cause: unknown) => (cause as { data?: { errorCode?: string } } | null)?.data?.errorCode
const datesOf = (cause: unknown) => (cause as { data?: { errors?: { dates?: string[] } } } | null)?.data?.errors?.dates ?? []
/** What a refused timer call says; no raw code is ever shown. */
function failure(cause: unknown) {
  switch (codeOf(cause)) {
    case 'MONTH_CLOSED': {
      const days = datesOf(cause)
      return days.length ? text.value.monthClosedDays.replace('{dates}', days.map(day => formatDate(day, props.formats, props.locale)).join(', ')) : text.value.monthClosed
    }
    case 'TIMER_TOO_LONG': return text.value.tooLong
    case 'INVALID_TIME_RANGE': return text.value.tooShort
    case 'ENTRY_IN_BREAK': return text.value.inBreak
    case 'VALIDATION_FAILED': return text.value.checkInput
    case 'TIMER_RUNNING': return text.value.alreadyRunning
    case 'NOT_FOUND': return text.value.noTimer
    default: return safeError(cause, props.locale)
  }
}

async function start() {
  if (!props.canEdit || busy.value) return
  busy.value = true; error.value = ''; notice.value = ''
  try {
    const typed = name.value.trim()
    apply(await props.api<TimerState>('/timer/start', { method: 'POST', body: typed ? { taskName: typed } : {} }))
    notice.value = text.value.started
  } catch (cause) {
    error.value = failure(cause)
    // Another tab started one first: show the one that is running.
    if (codeOf(cause) === 'TIMER_RUNNING') await load()
  } finally { busy.value = false }
}
async function stop(withTimes = false) {
  if (busy.value || !timer.value) return
  const typed = name.value.trim()
  if (!typed && !timer.value.taskName?.trim()) {
    error.value = text.value.needName; notice.value = ''
    nameField.value?.focus()
    return
  }
  const body: Record<string, unknown> = {}
  if (typed) body.taskName = typed
  if (withTimes) Object.assign(body, { date: fix.date, startTime: `${fix.startTime}:00`, endTime: `${fix.endTime}:00`, includesBreak: fix.includesBreak })
  busy.value = true; error.value = ''; notice.value = ''
  try {
    await props.api('/timer/stop', { method: 'POST', body })
    apply({ timer: null, nowUtc: new Date().toISOString() })
    notice.value = text.value.stoppedNotice
    emit('stopped')
  } catch (cause) {
    error.value = failure(cause)
    const code = codeOf(cause)
    // The timer is kept; these refusals are mended with a date and times, or by discarding it.
    if (code === 'MONTH_CLOSED' || code === 'TIMER_TOO_LONG' || code === 'INVALID_TIME_RANGE' || code === 'ENTRY_IN_BREAK') byHand.value = true
    if (code === 'NOT_FOUND') await load()
  } finally { busy.value = false }
}
async function discard() {
  if (busy.value || !timer.value || !confirm(text.value.confirmDiscard)) return
  busy.value = true; error.value = ''; notice.value = ''
  try {
    await props.api('/timer', { method: 'DELETE' })
    apply({ timer: null, nowUtc: new Date().toISOString() })
    notice.value = text.value.discarded
  } catch (cause) {
    error.value = failure(cause)
    if (codeOf(cause) === 'NOT_FOUND') await load()
  } finally { busy.value = false }
}
</script>

<template>
  <section class="timer-panel" aria-labelledby="timer-heading" :data-running="timer ? 'true' : 'false'">
    <div class="timer-head">
      <div><h2 id="timer-heading">{{ text.heading }}</h2><p class="timer-hint">{{ text.hint }}</p></div>
      <div v-if="timer" class="timer-clock">
        <span class="timer-state">{{ text.running }}</span>
        <time class="timer-elapsed" aria-hidden="true">{{ elapsed }}</time>
        <span class="timer-sr" role="timer" :aria-label="text.elapsed">{{ elapsedSpoken }}</span>
      </div>
    </div>

    <p v-if="!loaded" class="timer-hint">{{ text.loading }}</p>
    <div v-else-if="loadFailed && !timer" class="timer-load-failed">
      <p>{{ text.loadFailed }}</p>
      <button type="button" class="timer-button" @click="load">{{ text.retry }}</button>
    </div>

    <form v-else class="timer-form" @submit.prevent="timer ? stop() : start()">
      <label class="timer-field"><span>{{ text.task }}</span>
        <input ref="nameField" v-model="name" maxlength="200" :placeholder="text.taskPlaceholder" :disabled="!timer && !canEdit" /></label>
      <p v-if="!timer && !canEdit" class="timer-hint">{{ text.readOnly }}</p>
      <div class="timer-actions">
        <button v-if="!timer" type="submit" class="timer-button primary" :disabled="!canEdit || busy">{{ text.start }}</button>
        <template v-else>
          <button type="submit" class="timer-button primary" :disabled="busy">{{ text.stop }}</button>
          <button type="button" class="timer-button" :aria-expanded="byHand" aria-controls="timer-by-hand" @click="byHand = !byHand">{{ byHand ? text.hideTimes : text.chooseTimes }}</button>
          <button type="button" class="timer-button danger" :disabled="busy" @click="discard">{{ text.discard }}</button>
        </template>
      </div>
      <fieldset v-if="timer && byHand" id="timer-by-hand" class="timer-by-hand">
        <legend class="timer-hint">{{ text.fixHint }}</legend>
        <DateField v-model="fix.date" :label="text.date" :locale="locale" required />
        <TimeField v-model="fix.startTime" :label="text.startTime" :locale="locale" required />
        <TimeField v-model="fix.endTime" :label="text.endTime" :locale="locale" required />
        <label class="timer-check"><input v-model="fix.includesBreak" type="checkbox" /> {{ text.includesBreak }}</label>
        <button type="button" class="timer-button primary" :disabled="busy" @click="stop(true)">{{ text.stopWithTimes }}</button>
      </fieldset>
    </form>

    <p v-if="error" role="alert" class="timer-message error">{{ error }}</p>
    <p v-if="notice" role="status" class="timer-message success">{{ notice }}</p>
  </section>
</template>

<style scoped>
.timer-panel { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-card); box-shadow: var(--elev-1); color: var(--color-ink); display: grid; gap: .75rem; padding: 1rem 1.1rem; }
.timer-panel[data-running="true"] { border-left: 4px solid var(--color-brand); }
.timer-head { align-items: start; display: flex; flex-wrap: wrap; gap: .5rem 1.5rem; justify-content: space-between; }
.timer-panel h2 { font-family: var(--font-display); font-size: 1.2rem; font-weight: 600; margin: 0; }
.timer-hint { color: var(--color-muted); font-size: .9rem; margin: .2rem 0 0; }
.timer-clock { display: grid; justify-items: end; }
.timer-state { color: var(--color-brand-ink); font-size: .85rem; font-weight: 600; }
.timer-elapsed { font-family: var(--font-display); font-size: 2rem; font-variant-numeric: tabular-nums; font-weight: 600; line-height: 1.1; }
.timer-sr { border: 0; clip: rect(0 0 0 0); height: 1px; margin: -1px; overflow: hidden; padding: 0; position: absolute; white-space: nowrap; width: 1px; }
.timer-form { display: grid; gap: .75rem; min-width: 0; }
.timer-field { display: grid; font-size: .9rem; font-weight: 600; gap: .25rem; }
.timer-field input { background: var(--color-field); border: 1px solid var(--color-border-strong); border-radius: .5rem; box-shadow: var(--inset-field); color: var(--color-ink); font: inherit; font-weight: 400; min-height: 2.5rem; min-width: 0; padding: .4rem .65rem; width: 100%; }
.timer-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
.timer-button { background: var(--color-surface); border: 1px solid var(--color-border-strong); border-radius: .5rem; color: var(--color-ink); cursor: pointer; font: inherit; font-weight: 600; min-height: 2.5rem; padding: .4rem 1rem; }
.timer-button:hover:not(:disabled) { background: var(--color-surface-muted); }
.timer-button.primary { background: var(--color-brand); border-color: var(--color-brand); color: var(--color-on-brand); }
.timer-button.primary:hover:not(:disabled) { background: var(--color-brand-strong); }
.timer-button.danger { border-color: var(--color-danger-line); color: var(--color-danger); }
.timer-button:disabled { cursor: not-allowed; opacity: .55; }
.timer-button:focus-visible, .timer-field input:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.timer-by-hand { border: 1px dashed var(--color-border-strong); border-radius: .5rem; display: grid; gap: .6rem; margin: 0; min-width: 0; padding: .75rem; }
.timer-by-hand .timer-button { justify-self: start; }
.timer-check { align-items: center; display: flex; font-size: .9rem; gap: .5rem; }
.timer-load-failed { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem 1rem; }
.timer-load-failed p { margin: 0; }
.timer-message { border-radius: .5rem; margin: 0; padding: .6rem .8rem; }
.timer-message.error { background: var(--color-danger-soft); border: 1px solid var(--color-danger-line); color: var(--color-danger); }
.timer-message.success { background: var(--color-success-soft); border: 1px solid var(--color-success-line); color: var(--color-success); }
</style>
