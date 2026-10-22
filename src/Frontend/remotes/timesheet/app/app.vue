<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, type Locale, fieldLocaleKey } from '../../../shared/i18n'
import { applyTheme, readSessionTheme } from '../../../shared/theme'
import { formatDate, formatMonth, formatTime, readFormats } from '../../../shared/date-format'
import { closedDatesOf, daysFrom, decimalHours, defaultWorkHours, errorCodeOf, readDroppedTrayItem, standardMinutes, timesheetDatesKey, timesheetTextKey, useTimesheetText, type BulkDay, type Catalog, type CopyResult, type Entry, type EntryForm, type EntryTemplate, type Leave, type LeaveAmount, type LeaveBalance, type MonthState, type SkipReason, type Task, type TaskStatus, type TrayItem } from './utils/timesheet'

const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
provide(fieldLocaleKey, locale)
const pad = (value: number) => String(value).padStart(2, '0')
// The browser's local date, so the morning in Thailand is not still yesterday in UTC.
const now = new Date()
const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
const month = ref(today.slice(0, 7))
const entries = ref<Entry[]>([])
const tasks = ref<Task[]>([])
const leaves = ref<Leave[]>([])
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [], leaveTypes: [], workingDays: [1, 2, 3, 4, 5], workHours: defaultWorkHours })
const balances = ref<LeaveBalance[]>([])
let workHoursApplied = false
const error = ref('')
const notice = ref('')
/** Lines under the notice: the days a copy skipped and why. */
const noticeDetails = ref<string[]>([])
const templates = ref<EntryTemplate[]>([])
const several = ref(false)
const bulkDates = ref<string[]>([])
const templateName = ref<string | null>(null)
const copying = ref(false)
const editingId = ref<string | null>(null)
const form = reactive<EntryForm>({ date: today, startTime: defaultWorkHours.startTime,
  endTime: defaultWorkHours.endTime, includesBreak: false, taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
const taskName = ref('')
const view = ref<'entries' | 'board'>('entries')
const leaveDate = ref(today)
/** A built-in kind, or the leave type's id once the organization has leave types. */
const leaveKind = ref('annual')
const leaveAmount = ref<LeaveAmount>('day')
const leaveHours = ref(1)
const currentMonth = ref('')
/** The viewed month's approval state; the API decides whether it can be edited. */
const monthState = ref<MonthState>({ locked: false, status: 'open', returnReason: null, editable: false })
const canEdit = computed(() => monthState.value.editable)
// Months are YYYY-MM, so they compare as text. A future month cannot be submitted.
const canSubmit = computed(() => Boolean(currentMonth.value) && month.value <= currentMonth.value && !monthState.value.locked &&
  (monthState.value.status === 'open' || monthState.value.status === 'returned'))
const submitting = ref(false)
const formats = ref(readFormats(null))
const monthLabel = computed(() => formatMonth(month.value, formats.value, locale.value))
const monthStart = computed(() => `${month.value}-01`)
const monthEnd = computed(() => `${month.value}-${new Date(Number(month.value.slice(0, 4)), Number(month.value.slice(5)), 0).getDate()}`)
const copy = useTimesheetText(locale)
const { text, labels } = copy
const minutesByDay = computed(() => entries.value.reduce((days, entry) =>
  days.set(entry.date, (days.get(entry.date) ?? 0) + entry.durationMinutes), new Map<string, number>()))
const leaveLabel = (leave: Leave) => {
  const kind = leave.kind === 'annual' || leave.kind === 'sick' || leave.kind === 'other' ? text.value[leave.kind] : leave.kind
  return leave.minutes ? `${kind} (${labels.value.leaveHoursShort.replace('{hours}', decimalHours(leave.minutes))})` : kind
}
const leaveByDay = computed(() => new Map(leaves.value.map(leave => [leave.date, leaveLabel(leave)])))
const holidayByDay = computed(() => new Map(catalog.value.holidays.filter(holiday => holiday.date.startsWith(month.value))
  .map(holiday => [holiday.date, holiday.name])))
const normalDay = computed(() => standardMinutes(catalog.value.workHours))
const monthTotals = computed(() => {
  const minutes = [...minutesByDay.value.values()].reduce((sum, value) => sum + value, 0)
  // Leave in days: a part day counts against the normal working day.
  const leave = Math.round(leaves.value.reduce((sum, item) => sum + (item.minutes && normalDay.value ? item.minutes / normalDay.value : 1), 0) * 100) / 100
  const logged = minutes
    ? (minutesByDay.value.size === 1 ? labels.value.monthTotalsOneDay : labels.value.monthTotals)
      .replace('{hours}', decimalHours(minutes)).replace('{days}', String(minutesByDay.value.size))
    : labels.value.monthEmpty
  // Thai sentences do not end with a full stop.
  return `${logged}${leave === 1 ? labels.value.monthLeaveOne : leave ? labels.value.monthLeave.replace('{count}', String(leave)) : ''}${locale.value === 'th' ? '' : '.'}`
})
const dayEntries = computed(() => entries.value.filter(entry => entry.date === form.date)
  .sort((a, b) => a.startTime.localeCompare(b.startTime)))
const dayHeading = computed(() => {
  const weekday = new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH-u-ca-gregory' : 'en-GB', { weekday: 'long', timeZone: 'UTC' })
    .format(new Date(`${form.date}T00:00:00Z`))
  return `${weekday} ${formatDate(form.date, formats.value, locale.value)}`
})
const dayNote = computed(() => holidayByDay.value.get(form.date) ?? leaveByDay.value.get(form.date) ??
  (catalog.value.workingDays.includes(new Date(`${form.date}T12:00:00`).getDay()) ? '' : labels.value.dayOff))
const isWorkingDay = (day: string) => catalog.value.workingDays.includes(new Date(`${day}T12:00:00`).getDay())
/** The month's days for the several-days form: days off, holidays, and whole-day leave start unticked. */
const bulkDays = computed<BulkDay[]>(() => daysFrom(monthStart.value, monthEnd.value).map(date => {
  const holiday = holidayByDay.value.get(date)
  const wholeLeave = leaves.value.some(leave => leave.date === date && !leave.minutes)
  const note = holiday ?? (wholeLeave ? leaveByDay.value.get(date) : undefined) ?? (isWorkingDay(date) ? '' : labels.value.dayOff)
  return { date, offDay: Boolean(holiday) || wholeLeave || !isWorkingDay(date), note: note ?? '' }
}))
const entriesHeading = computed(() => labels.value.entriesIn.replace('{month}', monthLabel.value))
const workHoursLabel = computed(() => {
  const { startTime, endTime, breakStart, breakEnd } = catalog.value.workHours
  const hours = labels.value.workHours.replace('{start}', formatTime(startTime, formats.value)).replace('{end}', formatTime(endTime, formats.value))
  return breakStart && breakEnd
    ? `${hours}${locale.value === 'th' ? ' ' : ', '}${labels.value.breakHours.replace('{start}', formatTime(breakStart, formats.value)).replace('{end}', formatTime(breakEnd, formats.value))}`
    : hours
})
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
  applyTheme(readSessionTheme(event.data))
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
    const [e, t, l, c, context, state, balance, saved] = await Promise.all([
      api<Entry[]>(`/entries?year=${month.value.slice(0, 4)}&month=${Number(month.value.slice(5))}`),
      api<Task[]>('/tasks'),
      api<Leave[]>(`/leave?year=${month.value.slice(0, 4)}&month=${Number(month.value.slice(5))}`),
      api<Catalog>('/catalog'),
      api<{ currentMonth: string }>('/context'),
      api<MonthState>(`/me/months/${month.value.slice(0, 4)}/${Number(month.value.slice(5))}`),
      api<{ balances: LeaveBalance[] }>(`/me/leave-balance?year=${month.value.slice(0, 4)}`),
      api<EntryTemplate[]>('/entry-templates')
    ])
    entries.value = e; tasks.value = t; leaves.value = l; catalog.value = { ...c, leaveTypes: c.leaveTypes ?? [], workingDays: c.workingDays ?? [1, 2, 3, 4, 5], workHours: c.workHours ?? defaultWorkHours }
    balances.value = balance.balances; templates.value = saved ?? []
    // With leave types the kind is a type's id; keep a valid choice as the types change.
    const types = catalog.value.leaveTypes
    if (types.length && !types.some(type => type.id === leaveKind.value)) leaveKind.value = types[0]!.id
    if (!types.length && !['annual', 'sick', 'other'].includes(leaveKind.value)) leaveKind.value = 'annual'
    // The first time the organization's hours are known, an untouched new entry starts with them.
    if (!workHoursApplied && !editingId.value && !form.taskName)
      Object.assign(form, { startTime: catalog.value.workHours.startTime, endTime: catalog.value.workHours.endTime })
    workHoursApplied = true
    currentMonth.value = context.currentMonth; monthState.value = state
    if (!entries.value.length && month.value === new Date().toISOString().slice(0, 7) &&
        month.value !== context.currentMonth) month.value = context.currentMonth
    error.value = ''
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
watch(month, load)
watch(() => form.date, day => { if (day.startsWith(month.value)) leaveDate.value = day })
watch(month, value => {
  // The several-days choice belongs to the month it was made in.
  several.value = false; bulkDates.value = []
  if (!form.date.startsWith(value)) form.date = `${value}-01`
  if (!leaveDate.value.startsWith(value)) leaveDate.value = `${value}-01`
})
function resetForm(date = form.date) {
  editingId.value = null
  Object.assign(form, { date, startTime: catalog.value.workHours.startTime, endTime: catalog.value.workHours.endTime,
    includesBreak: false, taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
}
function cancelEntry() {
  resetForm(); several.value = false; templateName.value = null
}
function moveMonth(offset: number) {
  const [year, monthNumber] = month.value.split('-').map(Number)
  const next = new Date(year, monthNumber - 1 + offset, 1)
  month.value = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, '0')}`
  const nextDate = `${month.value}-01`
  form.date = nextDate
  leaveDate.value = nextDate
}
function selectDay(day: string) {
  // Choosing another day while editing leaves the edit, so the entry is not moved by accident.
  if (editingId.value) resetForm(day)
  else form.date = day
}
function edit(entry: Entry) {
  editingId.value = entry.id; several.value = false
  Object.assign(form, { date: entry.date, startTime: entry.startTime.slice(0, 5), endTime: entry.endTime.slice(0, 5),
    taskName: entry.taskName, detail: entry.detail || '', notes: entry.notes || '',
    projectId: entry.projectId || '', categoryId: entry.categoryId || '', personalTaskId: entry.personalTaskId || '',
    includesBreak: entry.includesBreak ?? false })
  // Edit from the month list brings the form into view; it sits beside the calendar.
  void nextTick(() => document.getElementById('entry-heading')?.scrollIntoView({ block: 'nearest',
    behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' }))
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
function announce(message: string, details: string[] = []) {
  notice.value = message; noticeDetails.value = details
}
/** What a refused change says: a closed month names its days, and the entry rules have their own words. */
function failure(cause: unknown) {
  const code = errorCodeOf(cause)
  if (code === 'MONTH_CLOSED') {
    const days = closedDatesOf(cause)
    return days.length ? labels.value.monthClosedDays.replace('{dates}', days.map(day => formatDate(day, formats.value, locale.value)).join(', '))
      : labels.value.monthClosed
  }
  if (code === 'ENTRY_IN_BREAK') return labels.value.entryInBreak
  if (code === 'INVALID_TIME_RANGE') return labels.value.invalidTimeRange
  return safeError(cause, locale.value)
}
const entryBody = () => ({ startTime: `${form.startTime}:00`, endTime: `${form.endTime}:00`, taskName: form.taskName, detail: form.detail,
  notes: form.notes, projectId: form.projectId || null, categoryId: form.categoryId || null, includesBreak: form.includesBreak })
async function saveEntry() {
  if (!canEdit.value) return
  const bulk = several.value && !editingId.value
  if (bulk && !bulkDates.value.length) return
  try {
    const savedDate = form.date
    if (bulk) {
      const count = bulkDates.value.length
      await api('/entries/bulk', { method: 'POST', body: { dates: [...bulkDates.value].sort(), ...entryBody(),
        personalTaskId: form.personalTaskId || null, reason: null } })
      announce(labels.value.addedToDays.replace('{count}', String(count)))
      several.value = false; bulkDates.value = []
    } else {
      await api(`/entries${editingId.value ? `/${editingId.value}` : ''}`, { method: editingId.value ? 'PUT' : 'POST',
        body: { ...form, startTime: `${form.startTime}:00`, endTime: `${form.endTime}:00`,
          projectId: form.projectId || null, categoryId: form.categoryId || null, personalTaskId: form.personalTaskId || null } })
      announce(text.value.saved)
    }
    resetForm(savedDate); await load()
  } catch (cause) { error.value = failure(cause) }
}
const skipLabel = (reason: SkipReason) => ({ holiday: labels.value.skipHoliday, 'day-off': labels.value.skipDayOff,
  leave: labels.value.skipLeave, 'has-entries': labels.value.skipHasEntries })[reason] ?? reason
async function copyEntries(request: { from: string; to: string; days: number }) {
  if (!canEdit.value || copying.value) return
  copying.value = true
  try {
    const result = await api<CopyResult>('/entries/copy', { method: 'POST', body: request })
    const created = result?.created ?? []
    const skipped = result?.skipped ?? []
    await load()
    announce(created.length === 1 ? labels.value.copiedOne : created.length ? labels.value.copied.replace('{count}', String(created.length)) : labels.value.copiedNone,
      skipped.map(day => `${formatDate(day.date, formats.value, locale.value)}: ${skipLabel(day.reason)}`))
  } catch (cause) { error.value = failure(cause) }
  finally { copying.value = false }
}
function useTemplate(template: EntryTemplate | undefined) {
  if (!template || !canEdit.value) return
  if (editingId.value) resetForm()
  Object.assign(form, { taskName: template.taskName, startTime: template.startTime.slice(0, 5), endTime: template.endTime.slice(0, 5),
    projectId: template.projectId || '', categoryId: template.categoryId || '', detail: template.detail || '', notes: template.notes || '',
    includesBreak: template.includesBreak ?? false, personalTaskId: '' })
}
const templateBody = (name: string) => ({ name: name.trim(), ...entryBody(), detail: form.detail || null, notes: form.notes || null })
function templateFailure(cause: unknown, name: string) {
  error.value = errorCodeOf(cause) === 'TEMPLATE_EXISTS' ? labels.value.templateExists.replace('{name}', name.trim()) : failure(cause)
}
async function saveTemplate() {
  const name = templateName.value ?? ''
  if (!name.trim()) return
  if (!form.taskName.trim()) { error.value = labels.value.templateTaskFirst; return }
  try {
    await api('/entry-templates', { method: 'POST', body: templateBody(name) })
    templateName.value = null
    await load()
    announce(labels.value.templateSaved)
  } catch (cause) { templateFailure(cause, name) }
}
/** Saves a template's new name, or (from the form) replaces what it fills in. */
async function updateTemplate(template: EntryTemplate, fromForm = false) {
  if (!template.name.trim()) return
  if (fromForm && !form.taskName.trim()) { error.value = labels.value.templateTaskFirst; return }
  const body = fromForm ? templateBody(template.name) : { name: template.name.trim(), taskName: template.taskName, startTime: template.startTime,
    endTime: template.endTime, projectId: template.projectId, categoryId: template.categoryId, detail: template.detail, notes: template.notes,
    includesBreak: template.includesBreak }
  try { await api(`/entry-templates/${template.id}`, { method: 'PUT', body }); await load(); announce(labels.value.templateSaved) }
  catch (cause) { templateFailure(cause, template.name) }
}
async function removeTemplate(template: EntryTemplate) {
  if (!confirm(labels.value.confirmDeleteTemplate.replace('{name}', template.name))) return
  try { await api(`/entry-templates/${template.id}`, { method: 'DELETE' }); await load(); announce(labels.value.templateDeleted) }
  catch (cause) { error.value = failure(cause) }
}
async function removeEntry(id: string) {
  if (!canEdit.value || !confirm(text.value.confirmDeleteEntry)) return
  try { await api(`/entries/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function submitMonth() {
  if (!canSubmit.value || !confirm(labels.value.confirmSubmit.replace('{month}', monthLabel.value))) return
  submitting.value = true
  try {
    await api(`/me/months/${month.value.slice(0, 4)}/${Number(month.value.slice(5))}/submit`, { method: 'POST' })
    resetForm(); await load()
    announce(labels.value.submittedNotice)
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { submitting.value = false }
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
  const minutes = leaveAmount.value === 'half' ? Math.round(normalDay.value / 2)
    : leaveAmount.value === 'hours' ? Math.round(leaveHours.value * 60) : null
  const kind = catalog.value.leaveTypes.length ? { leaveTypeId: leaveKind.value } : { kind: leaveKind.value }
  try { await api('/leave', { method: 'POST', body: { date: leaveDate.value, ...kind, minutes } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function removeLeave(id: string) {
  if (!canEdit.value) return
  try { await api(`/leave/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function updateLeave(leave: Leave) {
  if (!canEdit.value) return
  try { await api(`/leave/${leave.id}`, { method: 'PUT', body: { date: leave.date, kind: leave.kind, notes: leave.notes,
    leaveTypeId: leave.leaveTypeId ?? null, minutes: leave.minutes ?? null } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
</script>

<template>
  <main class="workspace">
    <p v-if="!token" class="sign-in-card">{{ text.signIn }}</p>
    <template v-else>
      <header class="page-heading">
        <div class="month-title">
          <h1>{{ monthLabel }}</h1>
          <p class="month-totals">{{ monthTotals }}</p>
          <p class="work-hours-label">{{ workHoursLabel }}</p>
        </div>
        <div class="month-control" role="group" :aria-label="text.month">
          <button class="icon-button" type="button" :aria-label="labels.previousMonth" @click="moveMonth(-1)">‹</button>
          <label class="month-picker"><span class="visually-hidden">{{ text.month }}</span><input v-model="month" type="month" /></label>
          <button class="icon-button" type="button" :aria-label="labels.nextMonth" @click="moveMonth(1)">›</button>
        </div>
      </header>

      <ApprovalBar v-if="currentMonth && month <= currentMonth" :state="monthState" :can-submit="canSubmit" :busy="submitting" @submit="submitMonth" />
      <p v-if="!canEdit && monthState.status === 'open' && !monthState.locked" class="notice" role="status">{{ text.readOnly }}</p>
      <p v-if="error" role="alert" class="feedback error">{{ error }}</p>
      <div v-if="notice" role="status" class="feedback success"><p>{{ notice }}</p><template v-if="noticeDetails.length">
        <p class="notice-subhead">{{ labels.skipped }}</p><ul class="notice-details"><li v-for="line in noticeDetails" :key="line">{{ line }}</li></ul></template></div>

      <div class="view-switch" role="group" :aria-label="text.view">
        <button type="button" :aria-pressed="view === 'entries'" @click="view = 'entries'">{{ text.entriesView }}</button>
        <button type="button" :aria-pressed="view === 'board'" @click="view = 'board'">{{ text.board }}</button>
      </div>

      <TaskBoard v-if="view === 'board'" v-model:task-name="taskName" :tasks="tasks" :catalog="catalog" :can-log="canEdit"
        @add="addTask" @status="setStatus" @remove="removeTask" @log="logTask" />

      <template v-else>
        <div class="work-layout">
          <div class="main-column">
            <MonthCalendar :month="month" :selected="form.date" :today="today" :minutes-by-day="minutesByDay" :leave-by-day="leaveByDay"
              :holiday-by-day="holidayByDay" :working-days="catalog.workingDays" :standard-minutes="normalDay" @select="selectDay" @drop="dropTask" />
            <LeavePanel v-model:leave-date="leaveDate" v-model:leave-kind="leaveKind" v-model:leave-amount="leaveAmount" v-model:leave-hours="leaveHours"
              :leaves="leaves" :holidays="catalog.holidays" :leave-types="catalog.leaveTypes" :balances="balances" :month="month" :standard-minutes="normalDay"
              :can-edit="canEdit" :month-start="monthStart" :month-end="monthEnd" @add="addLeave" @update="updateLeave" @remove="removeLeave" />
          </div>

          <div class="side-column">
            <div class="day-sheet">
              <DayPanel :day="form.date" :heading="dayHeading" :entries="dayEntries" :catalog="catalog" :can-edit="canEdit"
                :editing-id="editingId" :note="dayNote" @edit="edit" @remove="removeEntry" />
              <CopyDays v-if="canEdit" :day="form.date" :month-start="monthStart" :month-end="monthEnd" :busy="copying" @copy="copyEntries" />
              <EntryComposer v-if="canEdit" v-model:several="several" v-model:bulk-dates="bulkDates" v-model:template-name="templateName"
                :form="form" :catalog="catalog" :month-start="monthStart" :month-end="monthEnd" :templates="templates" :bulk-days="bulkDays"
                :editing="Boolean(editingId)" @save="saveEntry" @cancel="cancelEntry" @drop="dropTask"
                @template="id => useTemplate(templates.find(template => template.id === id))" @save-template="saveTemplate" />
            </div>
            <TaskTray v-model:task-name="taskName" :tasks="tasks" :catalog="catalog" @add="addTask" @status="setStatus"
              @rename="renameTask" @remove="removeTask" @use="prefill" />
            <TemplatePanel v-if="canEdit" :templates="templates" @use="useTemplate" @rename="template => updateTemplate(template)"
              @replace="template => updateTemplate(template, true)" @remove="removeTemplate" />
          </div>
        </div>

        <EntriesTable :entries="entries" :catalog="catalog" :can-edit="canEdit" :editing-id="editingId" :heading="entriesHeading"
          @edit="edit" @remove="removeEntry" />
      </template>
    </template>
  </main>
</template>
