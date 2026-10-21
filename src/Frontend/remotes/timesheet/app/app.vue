<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, type Locale } from '../../../shared/i18n'
import { formatDate, formatMonth, formatTime, readFormats } from '../../../shared/date-format'
import { readDroppedTrayItem, timesheetDatesKey, timesheetTextKey, useTimesheetText, type Catalog, type Entry, type EntryForm, type Leave, type Task, type TaskStatus, type TrayItem } from './utils/timesheet'

const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
const month = ref(new Date().toISOString().slice(0, 7))
const entries = ref<Entry[]>([])
const tasks = ref<Task[]>([])
const leaves = ref<Leave[]>([])
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [], workingDays: [1, 2, 3, 4, 5] })
const error = ref('')
const notice = ref('')
const editingId = ref<string | null>(null)
const form = reactive<EntryForm>({ date: new Date().toISOString().slice(0, 10), startTime: '09:00', endTime: '17:00', taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
const taskName = ref('')
const view = ref<'entries' | 'board'>('entries')
const leaveDate = ref(new Date().toISOString().slice(0, 10))
const leaveKind = ref('annual')
const currentMonth = ref('')
const locked = ref(false)
const canEdit = computed(() => month.value === currentMonth.value && !locked.value)
const formats = ref(readFormats(null))
const monthLabel = computed(() => formatMonth(month.value, formats.value, locale.value))
const monthStart = computed(() => `${month.value}-01`)
const monthEnd = computed(() => `${month.value}-${new Date(Number(month.value.slice(0, 4)), Number(month.value.slice(5)), 0).getDate()}`)
const totalHours = computed(() => entries.value.reduce((total, entry) => total + entry.durationMinutes, 0) / 60)
const daysWithEntries = computed(() => new Set(entries.value.map(entry => entry.date)))
const activeDays = computed(() => daysWithEntries.value.size)
const daysWithLeave = computed(() => new Set(leaves.value.map(leave => leave.date)))
const daysWithHoliday = computed(() => new Set(catalog.value.holidays.filter(holiday => holiday.date.startsWith(month.value)).map(holiday => holiday.date)))
const copy = useTimesheetText(locale)
const { text, labels } = copy
provide(timesheetTextKey, copy)
provide(timesheetDatesKey, computed(() => ({
  date: (value: string) => formatDate(value, formats.value, locale.value),
  time: (value: string) => formatTime(value, formats.value)
})))
function onSessionMessage(event: MessageEvent) {
  if (event.source !== window.parent || event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
  token.value = event.data.token || ''
  gateway.value = event.data.gatewayUrl || gateway.value
  locale.value = readSessionLocale(event.data)
  formats.value = readFormats(event.data)
  if (token.value) load()
}
onMounted(() => {
  window.addEventListener('message', onSessionMessage)
  if (window.parent !== window) window.parent.postMessage({ type: 'aiyara.ready' }, config.public.shellOrigin)
})
onBeforeUnmount(() => window.removeEventListener('message', onSessionMessage))
async function api<T>(path: string, options: { method?: string; body?: object } = {}) {
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1/timesheets${path}`, {
      ...options, headers: { Authorization: `Bearer ${accessToken}` }
    }), token, config.public.shellOrigin))
}
async function load() {
  if (!token.value) return
  try {
    const [e, t, l, c, context] = await Promise.all([
      api<Entry[]>(`/entries?year=${month.value.slice(0, 4)}&month=${Number(month.value.slice(5))}`),
      api<Task[]>('/tasks'),
      api<Leave[]>(`/leave?year=${month.value.slice(0, 4)}&month=${Number(month.value.slice(5))}`),
      api<Catalog>('/catalog'),
      api<{ currentMonth: string; locked: boolean }>('/context')
    ])
    entries.value = e; tasks.value = t; leaves.value = l; catalog.value = { ...c, workingDays: c.workingDays ?? [1, 2, 3, 4, 5] }
    currentMonth.value = context.currentMonth; locked.value = context.locked
    if (!entries.value.length && month.value === new Date().toISOString().slice(0, 7) &&
        month.value !== context.currentMonth) month.value = context.currentMonth
    error.value = ''
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
watch(month, load)
watch(month, value => {
  if (!form.date.startsWith(value)) form.date = `${value}-01`
  if (!leaveDate.value.startsWith(value)) leaveDate.value = `${value}-01`
})
function resetForm(date = form.date) {
  editingId.value = null
  Object.assign(form, { date, startTime: '09:00', endTime: '17:00', taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
}
function moveMonth(offset: number) {
  const [year, monthNumber] = month.value.split('-').map(Number)
  const next = new Date(year, monthNumber - 1 + offset, 1)
  month.value = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, '0')}`
  const nextDate = `${month.value}-01`
  form.date = nextDate
  leaveDate.value = nextDate
}
function edit(entry: Entry) {
  editingId.value = entry.id
  Object.assign(form, { date: entry.date, startTime: entry.startTime.slice(0, 5), endTime: entry.endTime.slice(0, 5),
    taskName: entry.taskName, detail: entry.detail || '', notes: entry.notes || '',
    projectId: entry.projectId || '', categoryId: entry.categoryId || '', personalTaskId: entry.personalTaskId || '' })
}
function prefill(task: TrayItem, date?: string) {
  if (!canEdit.value) return
  if (date) form.date = date
  form.taskName = task.name; form.personalTaskId = task.personalTaskId || ''
  form.projectId = task.projectId || ''; form.categoryId = task.categoryId || ''
}
function logTask(task: TrayItem) {
  view.value = 'entries'
  prefill(task)
}
function dropTask(event: DragEvent, date?: string) {
  const item = readDroppedTrayItem(event)
  if (item) prefill(item, date)
}
async function saveEntry() {
  if (!canEdit.value) return
  try {
    const savedDate = form.date
    await api(`/entries${editingId.value ? `/${editingId.value}` : ''}`, { method: editingId.value ? 'PUT' : 'POST',
      body: { ...form, startTime: `${form.startTime}:00`, endTime: `${form.endTime}:00`,
        projectId: form.projectId || null, categoryId: form.categoryId || null, personalTaskId: form.personalTaskId || null } })
    notice.value = text.value.saved
    resetForm(savedDate); await load()
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
async function removeEntry(id: string) {
  if (!canEdit.value || !confirm(text.value.confirmDeleteEntry)) return
  try { await api(`/entries/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function addTask() {
  if (!taskName.value.trim()) return
  try { await api('/tasks', { method: 'POST', body: { name: taskName.value, status: 'todo' } }); taskName.value = ''; await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function setStatus(task: Task, status: TaskStatus) {
  // Move the card immediately so a board drop does not snap back while the request runs; a rejection reloads the real state.
  task.status = status
  try { await api(`/tasks/${task.id}`, { method: 'PUT', body: { name: task.name, status, projectId: task.projectId, categoryId: task.categoryId } }); await load() }
  // load() clears the error on success, so reload first and report the rejection after it.
  catch (cause) { await load(); error.value = safeError(cause, locale.value) }
}
async function renameTask(task: Task) {
  try { await api(`/tasks/${task.id}`, { method: 'PUT', body: { name: task.name, status: task.status, projectId: task.projectId, categoryId: task.categoryId } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function removeTask(id: string) {
  try { await api(`/tasks/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function addLeave() {
  if (!canEdit.value) return
  try { await api('/leave', { method: 'POST', body: { date: leaveDate.value, kind: leaveKind.value } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function removeLeave(id: string) {
  if (!canEdit.value) return
  try { await api(`/leave/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function updateLeave(leave: Leave) {
  if (!canEdit.value) return
  try { await api(`/leave/${leave.id}`, { method: 'PUT', body: { date: leave.date, kind: leave.kind, notes: leave.notes } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
</script>

<template>
  <main class="workspace">
    <p v-if="!token" class="sign-in-card">{{ text.signIn }}</p>
    <template v-else>
      <header class="page-heading">
        <div><span class="eyebrow">AIYARA · WORKSPACE</span><h1>{{ text.title }}</h1><p>{{ text.subtitle }}</p></div>
        <div class="month-control" :aria-label="text.month">
          <button class="icon-button" type="button" :aria-label="labels.previousMonth" @click="moveMonth(-1)">‹</button>
          <label class="month-picker"><span>{{ text.month }}</span><input v-model="month" type="month" /></label>
          <button class="icon-button" type="button" :aria-label="labels.nextMonth" @click="moveMonth(1)">›</button>
        </div>
      </header>

      <p v-if="!canEdit" class="notice" role="status"><span aria-hidden="true">ⓘ</span>{{ text.readOnly }}</p>
      <p v-if="error" role="alert" class="feedback error">{{ error }}</p>
      <p v-if="notice" role="status" class="feedback success">{{ notice }}</p>

      <MonthSummary :total-hours="totalHours" :entry-count="entries.length" :active-days="activeDays" :leave-count="leaves.length" />

      <div class="view-switch" role="group" :aria-label="text.view">
        <button type="button" :aria-pressed="view === 'entries'" @click="view = 'entries'">{{ text.entriesView }}</button>
        <button type="button" :aria-pressed="view === 'board'" @click="view = 'board'">{{ text.board }}</button>
      </div>

      <TaskBoard v-if="view === 'board'" v-model:task-name="taskName" :tasks="tasks" :catalog="catalog" :can-log="canEdit"
        @add="addTask" @status="setStatus" @remove="removeTask" @log="logTask" />

      <template v-else>
        <div class="work-layout">
          <div class="main-column">
            <MonthCalendar v-if="canEdit" v-model:selected="form.date" :month="month" :month-label="monthLabel" :locale="locale"
              :entry-days="daysWithEntries" :leave-days="daysWithLeave" :holiday-days="daysWithHoliday" :working-days="catalog.workingDays" @drop="dropTask" />
            <EntryComposer v-if="canEdit && !editingId" :form="form" :catalog="catalog" :month-start="monthStart" :month-end="monthEnd"
              @save="saveEntry" @cancel="resetForm()" @drop="dropTask" />
          </div>

          <TaskTray v-model:task-name="taskName" :tasks="tasks" :catalog="catalog" @add="addTask" @status="setStatus"
            @rename="renameTask" @remove="removeTask" @use="prefill" />
        </div>

        <EntriesTable :entries="entries" :catalog="catalog" :can-edit="canEdit" :editing-id="editingId" :form="form" :month-label="monthLabel"
          :month-start="monthStart" :month-end="monthEnd" @edit="edit" @remove="removeEntry" @save="saveEntry" @cancel="resetForm()" />

        <div class="support-grid">
          <LeavePanel v-model:leave-date="leaveDate" v-model:leave-kind="leaveKind" :leaves="leaves" :holidays="catalog.holidays" :month="month"
            :can-edit="canEdit" :month-start="monthStart" :month-end="monthEnd" @add="addLeave" @update="updateLeave" @remove="removeLeave" />
        </div>
      </template>
    </template>
  </main>
</template>
