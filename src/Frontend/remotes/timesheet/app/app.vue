<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'

type Entry = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null; projectId: string | null; categoryId: string | null; personalTaskId: string | null }
type Task = { id: string; name: string; status: string; projectId: string | null; categoryId: string | null }
type Leave = { id: string; date: string; kind: string; notes: string | null }
type Catalog = { projects: { id: string; name: string }[]; categories: { id: string; name: string }[]; holidays: { id: string; date: string; name: string }[] }
type TrayItem = { name: string; projectId?: string | null; categoryId?: string | null; personalTaskId?: string | null }
const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<'th' | 'en'>('th')
const month = ref(new Date().toISOString().slice(0, 7))
const entries = ref<Entry[]>([])
const tasks = ref<Task[]>([])
const leaves = ref<Leave[]>([])
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [] })
const error = ref('')
const notice = ref('')
const editingId = ref<string | null>(null)
const form = reactive({ date: new Date().toISOString().slice(0, 10), startTime: '09:00', endTime: '17:00', taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
const taskName = ref('')
const leaveDate = ref(new Date().toISOString().slice(0, 10))
const leaveKind = ref('annual')
const currentMonth = ref('')
const locked = ref(false)
const canEdit = computed(() => month.value === currentMonth.value && !locked.value)
const days = computed(() => Array.from({ length: new Date(Number(month.value.slice(0, 4)), Number(month.value.slice(5)), 0).getDate() },
  (_, index) => `${month.value}-${String(index + 1).padStart(2, '0')}`))
const weekendDays = computed(() => new Set(days.value.filter(day => {
  const weekday = new Date(`${day}T12:00:00`).getDay()
  return weekday === 0 || weekday === 6
})))
const calendarCells = computed(() => [
  ...Array.from({ length: new Date(Number(month.value.slice(0, 4)), Number(month.value.slice(5)) - 1, 1).getDay() }, () => null),
  ...days.value
])
const weekdayLabels = computed(() => locale.value === 'th'
  ? ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส']
  : ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'])
const monthLabel = computed(() => new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en', {
  month: 'long', year: 'numeric'
}).format(new Date(`${month.value}-01T12:00:00`)))
const monthStart = computed(() => `${month.value}-01`)
const monthEnd = computed(() => `${month.value}-${new Date(Number(month.value.slice(0, 4)), Number(month.value.slice(5)), 0).getDate()}`)
const totalHours = computed(() => entries.value.reduce((total, entry) => total + entry.durationMinutes, 0) / 60)
const daysWithEntries = computed(() => new Set(entries.value.map(entry => entry.date)))
const activeDays = computed(() => daysWithEntries.value.size)
const daysWithLeave = computed(() => new Set(leaves.value.map(leave => leave.date)))
const daysWithHoliday = computed(() => new Set(catalog.value.holidays.filter(holiday => holiday.date.startsWith(month.value)).map(holiday => holiday.date)))
const durationPreview = computed(() => {
  const [startHour, startMinute] = form.startTime.split(':').map(Number)
  const [endHour, endMinute] = form.endTime.split(':').map(Number)
  const duration = endHour * 60 + endMinute - (startHour * 60 + startMinute)
  return duration > 0 ? `${(duration / 60).toFixed(2)} ${text.value.duration}` : ''
})
const text = computed(() => locale.value === 'th' ? {
  title: 'บันทึกเวลาทำงาน', month: 'เดือน', task: 'งาน', date: 'วันที่', start: 'เริ่ม', end: 'สิ้นสุด', duration: 'ชั่วโมง', detail: 'รายละเอียด', notes: 'หมายเหตุ', project: 'โครงการ', category: 'หมวดหมู่', save: 'บันทึก', cancel: 'ยกเลิก', edit: 'แก้ไข', remove: 'ลบ', add: 'เพิ่มรายการ', tasks: 'งานส่วนตัว', leaves: 'วันลา', kind: 'ประเภท', readOnly: 'เดือนที่ผ่านมาอ่านได้อย่างเดียว', noEntries: 'ยังไม่มีรายการ', status: 'สถานะ', drop: 'ลากงานมาที่แบบฟอร์มเพื่อเริ่มบันทึก', chooseDay: 'เลือกวัน', use: 'ใช้', todo: 'ต้องทำ', doing: 'กำลังทำ', done: 'เสร็จ', annual: 'พักร้อน', sick: 'ลาป่วย', other: 'อื่น ๆ', holidays: 'วันหยุด', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell'
} : {
  title: 'Time entries', month: 'Month', task: 'Task', date: 'Date', start: 'Start', end: 'End', duration: 'Hours', detail: 'Detail', notes: 'Notes', project: 'Project', category: 'Category', save: 'Save', cancel: 'Cancel', edit: 'Edit', remove: 'Delete', add: 'Add entry', tasks: 'Personal tasks', leaves: 'Leave', kind: 'Kind', readOnly: 'Past months are read only', noEntries: 'No entries yet', status: 'Status', drop: 'Drag a task here to start an entry', chooseDay: 'Choose a day', use: 'Use', todo: 'To do', doing: 'Doing', done: 'Done', annual: 'Annual', sick: 'Sick', other: 'Other', holidays: 'Holidays', signIn: 'Please sign in through Aiyara Shell'
})
const labels = computed(() => locale.value === 'th' ? {
  hoursLogged: 'ชั่วโมงที่บันทึก', daysTracked: 'วันที่มีรายการ', leaveDays: 'วันลา',
  previousMonth: 'เดือนก่อนหน้า', nextMonth: 'เดือนถัดไป', selectedDate: 'วันที่เลือก',
  estimate: 'รวมเวลารายการนี้', entryList: 'รายการเวลาทำงาน', allMonth: 'สรุปของเดือน',
  taskPlaceholder: 'เช่น ประชุมทีม ออกแบบหน้าจอ', detailPlaceholder: 'เพิ่มรายละเอียดที่ช่วยให้กลับมาทบทวนได้',
  notesPlaceholder: 'บันทึกข้อมูลเพิ่มเติม (ไม่บังคับ)', emptyTitle: 'ยังไม่มีเวลาที่บันทึก',
  emptyHint: 'เริ่มเพิ่มรายการแรกของเดือนนี้ได้จากแบบฟอร์มด้านบน'
} : {
  hoursLogged: 'Hours logged', daysTracked: 'Days with entries', leaveDays: 'Leave days',
  previousMonth: 'Previous month', nextMonth: 'Next month', selectedDate: 'Selected date',
  estimate: 'Entry total', entryList: 'Time entries', allMonth: 'Month summary',
  taskPlaceholder: 'e.g. Team meeting, design review', detailPlaceholder: 'Add a detail that will help later',
  notesPlaceholder: 'Extra notes (optional)', emptyTitle: 'No time logged yet',
  emptyHint: 'Add your first entry for this month using the form above'
})
function onSessionMessage(event: MessageEvent) {
  if (event.source !== window.parent || event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
  token.value = event.data.token || ''
  gateway.value = event.data.gatewayUrl || gateway.value
  locale.value = event.data.locale === 'en' ? 'en' : 'th'
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
    entries.value = e; tasks.value = t; leaves.value = l; catalog.value = c
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
function dragTask(event: DragEvent, task: TrayItem) { event.dataTransfer?.setData('application/x-aiyara-task', JSON.stringify(task)) }
function prefill(task: TrayItem, date?: string) {
  if (!canEdit.value) return
  if (date) form.date = date
  form.taskName = task.name; form.personalTaskId = task.personalTaskId || ''
  form.projectId = task.projectId || ''; form.categoryId = task.categoryId || ''
}
function dropTask(event: DragEvent, date?: string) {
  const value = event.dataTransfer?.getData('application/x-aiyara-task')
  if (!value) return
  prefill(JSON.parse(value) as TrayItem, date)
}
async function saveEntry() {
  if (!canEdit.value) return
  try {
    const savedDate = form.date
    await api(`/entries${editingId.value ? `/${editingId.value}` : ''}`, { method: editingId.value ? 'PUT' : 'POST',
      body: { ...form, startTime: `${form.startTime}:00`, endTime: `${form.endTime}:00`,
        projectId: form.projectId || null, categoryId: form.categoryId || null, personalTaskId: form.personalTaskId || null } })
    notice.value = locale.value === 'th' ? 'บันทึกแล้ว' : 'Saved'
    resetForm(savedDate); await load()
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
async function removeEntry(id: string) {
  if (!canEdit.value || !confirm(locale.value === 'th' ? 'ลบรายการนี้?' : 'Delete this entry?')) return
  try { await api(`/entries/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function addTask() {
  if (!taskName.value.trim()) return
  try { await api('/tasks', { method: 'POST', body: { name: taskName.value, status: 'todo' } }); taskName.value = ''; await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function setStatus(task: Task, status: string) {
  try { await api(`/tasks/${task.id}`, { method: 'PUT', body: { name: task.name, status, projectId: task.projectId, categoryId: task.categoryId } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
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
        <div><span class="eyebrow">AIYARA · WORKSPACE</span><h1>{{ text.title }}</h1><p>{{ locale === 'th' ? 'บันทึกและติดตามเวลาทำงานของคุณในที่เดียว' : 'Log and review your work time in one place.' }}</p></div>
        <div class="month-control" :aria-label="text.month">
          <button class="icon-button" type="button" :aria-label="labels.previousMonth" @click="moveMonth(-1)">‹</button>
          <label class="month-picker"><span>{{ text.month }}</span><input v-model="month" type="month" /></label>
          <button class="icon-button" type="button" :aria-label="labels.nextMonth" @click="moveMonth(1)">›</button>
        </div>
      </header>

      <p v-if="!canEdit" class="notice" role="status"><span aria-hidden="true">ⓘ</span>{{ text.readOnly }}</p>
      <p v-if="error" role="alert" class="feedback error">{{ error }}</p>
      <p v-if="notice" role="status" class="feedback success">{{ notice }}</p>

      <section class="summary-grid" :aria-label="labels.allMonth">
        <article class="summary-card summary-hours"><span class="summary-icon" aria-hidden="true">◷</span><div><p>{{ labels.hoursLogged }}</p><strong>{{ totalHours.toFixed(2) }} <small>{{ text.duration }}</small></strong></div></article>
        <article class="summary-card"><span class="summary-icon" aria-hidden="true">▤</span><div><p>{{ labels.entryList }}</p><strong>{{ entries.length }}</strong></div></article>
        <article class="summary-card"><span class="summary-icon" aria-hidden="true">▦</span><div><p>{{ labels.daysTracked }}</p><strong>{{ activeDays }}</strong></div></article>
        <article class="summary-card"><span class="summary-icon" aria-hidden="true">☼</span><div><p>{{ labels.leaveDays }}</p><strong>{{ leaves.length }}</strong></div></article>
      </section>

      <div class="work-layout">
        <div class="main-column">
          <section v-if="canEdit" class="panel calendar-panel">
            <div class="panel-heading"><div><span class="eyebrow">{{ labels.selectedDate }}</span><h2>{{ monthLabel }}</h2></div><div class="calendar-legend"><span><i class="legend-work"></i>{{ labels.entryList }}</span><span><i class="legend-leave"></i>{{ text.leaves }}</span></div></div>
            <div class="calendar-grid" :aria-label="text.chooseDay">
              <span v-for="(weekday, index) in weekdayLabels" :key="index" class="weekday">{{ weekday }}</span>
              <span v-for="(day, index) in calendarCells" :key="day || `blank-${index}`" class="calendar-cell">
                <button v-if="day" type="button" class="day-button" :class="{ selected: form.date === day, weekend: weekendDays.has(day), 'has-entry': daysWithEntries.has(day), 'has-leave': daysWithLeave.has(day), holiday: daysWithHoliday.has(day) }" :aria-pressed="form.date === day" :aria-label="`${text.date} ${day}`" @click="form.date = day" @dragover.prevent @drop.prevent="dropTask($event, day)"><span>{{ Number(day.slice(-2)) }}</span><i v-if="daysWithEntries.has(day) || daysWithLeave.has(day) || daysWithHoliday.has(day)" class="day-markers" aria-hidden="true"><b v-if="daysWithEntries.has(day)" class="entry-marker"></b><b v-if="daysWithLeave.has(day)" class="leave-marker"></b><b v-if="daysWithHoliday.has(day)" class="holiday-marker"></b></i></button>
              </span>
            </div>
          </section>

          <section v-if="canEdit && !editingId" class="panel entry-panel" @dragover.prevent @drop.prevent="dropTask($event)">
            <div class="panel-heading"><div><span class="eyebrow">{{ text.add }}</span><h2>{{ labels.selectedDate }} · {{ form.date }}</h2></div><span v-if="durationPreview" class="duration-pill">{{ labels.estimate }} <strong>{{ durationPreview }}</strong></span></div>
            <p class="subtle-hint">{{ text.drop }}</p>
            <form class="entry-grid" @submit.prevent="saveEntry">
              <label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" :placeholder="labels.taskPlaceholder" /></label>
              <div class="time-fields"><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><span aria-hidden="true">→</span><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label></div>
              <label>{{ text.date }}<input v-model="form.date" type="date" required :min="monthStart" :max="monthEnd" /></label>
              <label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label>
              <label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label>
              <label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" :placeholder="labels.detailPlaceholder" /></label>
              <label class="field-wide">{{ text.notes }}<input v-model="form.notes" maxlength="4000" :placeholder="labels.notesPlaceholder" /></label>
              <div class="form-actions"><button class="primary-button" type="submit"><span aria-hidden="true">＋</span>{{ text.save }}</button><button class="quiet-button" type="button" @click="resetForm()">{{ text.cancel }}</button></div>
            </form>
          </section>
        </div>

        <aside class="panel task-panel">
          <div class="panel-heading"><div><span class="eyebrow">{{ text.tasks }}</span><h2>{{ locale === 'th' ? 'เริ่มจากงานที่บันทึกไว้' : 'Start from a saved task' }}</h2></div><span class="count-badge">{{ tasks.length }}</span></div>
          <form class="add-task-form" @submit.prevent="addTask"><input v-model="taskName" :aria-label="text.task" maxlength="200" :placeholder="labels.taskPlaceholder" /><button class="icon-button add-task-button" type="submit" :aria-label="text.add">＋</button></form>
          <ul class="task-list"><li v-for="task in tasks" :key="task.id" draggable="true" @dragstart="dragTask($event, { name: task.name, projectId: task.projectId, categoryId: task.categoryId, personalTaskId: task.id })"><span class="task-grip" aria-hidden="true">⠿</span><input v-model="task.name" :aria-label="text.task" maxlength="200" /><button class="use-task-button" type="button" @click="prefill({ name: task.name, projectId: task.projectId, categoryId: task.categoryId, personalTaskId: task.id })">{{ text.use }}</button><details class="task-more"><summary :aria-label="text.status">···</summary><div class="task-menu"><label>{{ text.status }}<select :value="task.status" @change="setStatus(task, ($event.target as HTMLSelectElement).value)"><option value="todo">{{ text.todo }}</option><option value="doing">{{ text.doing }}</option><option value="done">{{ text.done }}</option></select></label><button type="button" @click="renameTask(task)">{{ text.save }}</button><button type="button" @click="removeTask(task.id)">{{ text.remove }}</button></div></details></li><li v-if="!tasks.length" class="empty-state">{{ locale === 'th' ? 'เพิ่มงานที่ทำบ่อยไว้ใช้ซ้ำได้' : 'Save tasks here to reuse them.' }}</li></ul>
          <div v-if="catalog.projects.length" class="quick-picks"><h3>{{ text.project }}</h3><button v-for="project in catalog.projects" :key="project.id" type="button" draggable="true" @dragstart="dragTask($event, { name: project.name, projectId: project.id })" @click="prefill({ name: project.name, projectId: project.id })">{{ project.name }}</button></div>
          <div v-if="catalog.categories.length" class="quick-picks"><h3>{{ text.category }}</h3><button v-for="category in catalog.categories" :key="category.id" type="button" draggable="true" @dragstart="dragTask($event, { name: category.name, categoryId: category.id })" @click="prefill({ name: category.name, categoryId: category.id })">{{ category.name }}</button></div>
        </aside>
      </div>

      <section class="panel entries-panel">
        <div class="panel-heading"><div><span class="eyebrow">{{ monthLabel }}</span><h2>{{ labels.entryList }}</h2></div><span class="count-badge">{{ entries.length }}</span></div>
        <div v-if="!entries.length" class="empty-panel"><span aria-hidden="true">◷</span><h3>{{ labels.emptyTitle }}</h3><p>{{ canEdit ? labels.emptyHint : text.readOnly }}</p></div>
        <div v-else class="table-wrap"><table><thead><tr><th>{{ text.date }}</th><th>{{ text.start }}–{{ text.end }}</th><th>{{ text.duration }}</th><th>{{ text.task }}</th><th class="optional-column">{{ text.project }}</th><th class="optional-column">{{ text.category }}</th><th><span class="visually-hidden">{{ text.edit }}</span></th></tr></thead>
          <tbody><template v-for="entry in entries" :key="entry.id"><tr><td><time :datetime="entry.date">{{ entry.date }}</time></td><td>{{ entry.startTime.slice(0, 5) }}–{{ entry.endTime.slice(0, 5) }}</td><td><strong>{{ (entry.durationMinutes / 60).toFixed(2) }}</strong></td><td class="task-name-cell">{{ entry.taskName }}</td><td class="optional-column">{{ catalog.projects.find(x => x.id === entry.projectId)?.name || '—' }}</td><td class="optional-column">{{ catalog.categories.find(x => x.id === entry.categoryId)?.name || '—' }}</td><td class="row-actions"><button v-if="canEdit" class="quiet-button" type="button" @click="edit(entry)">{{ text.edit }}</button><button v-if="canEdit" class="danger-button" type="button" @click="removeEntry(entry.id)">{{ text.remove }}</button></td></tr>
          <tr v-if="editingId === entry.id"><td colspan="7"><form class="entry-grid edit-grid" @submit.prevent="saveEntry"><label>{{ text.date }}<input v-model="form.date" type="date" required :min="monthStart" :max="monthEnd" /></label><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label><label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" /></label><label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label><label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label><label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" /></label><label>{{ text.notes }}<input v-model="form.notes" maxlength="4000" /></label><div class="form-actions"><button class="primary-button" type="submit">{{ text.save }}</button><button class="quiet-button" type="button" @click="resetForm()">{{ text.cancel }}</button></div></form></td></tr></template></tbody></table></div>
      </section>

      <div class="support-grid">
        <section class="panel support-panel"><div class="panel-heading"><div><span class="eyebrow">{{ text.leaves }}</span><h2>{{ locale === 'th' ? 'วางแผนวันหยุด' : 'Plan time off' }}</h2></div></div>
          <form v-if="canEdit" class="inline-form" @submit.prevent="addLeave"><label>{{ text.date }}<input v-model="leaveDate" type="date" :aria-label="text.date" :min="monthStart" :max="monthEnd" /></label><label>{{ text.kind }}<select v-model="leaveKind"><option value="annual">{{ text.annual }}</option><option value="sick">{{ text.sick }}</option><option value="other">{{ text.other }}</option></select></label><button class="primary-button" type="submit">{{ text.add }}</button></form>
          <ul class="simple-list"><li v-for="leave in leaves" :key="leave.id"><template v-if="canEdit"><input v-model="leave.date" type="date" :aria-label="text.date" :min="monthStart" :max="monthEnd" /><select v-model="leave.kind" :aria-label="text.kind"><option value="annual">{{ text.annual }}</option><option value="sick">{{ text.sick }}</option><option value="other">{{ text.other }}</option></select><button class="quiet-button" type="button" @click="updateLeave(leave)">{{ text.save }}</button><button class="danger-button" type="button" @click="removeLeave(leave.id)">{{ text.remove }}</button></template><template v-else><time :datetime="leave.date">{{ leave.date }}</time><span>{{ leave.kind }}</span></template></li><li v-if="!leaves.length" class="empty-state">{{ locale === 'th' ? 'ยังไม่มีวันลาในเดือนนี้' : 'No leave planned this month.' }}</li></ul>
          <div v-if="catalog.holidays.some(x => x.date.startsWith(month))" class="holiday-list"><h3>{{ text.holidays }}</h3><p v-for="holiday in catalog.holidays.filter(x => x.date.startsWith(month))" :key="holiday.id"><time :datetime="holiday.date">{{ holiday.date }}</time><span>{{ holiday.name }}</span></p></div>
        </section>
      </div>
    </template>
  </main>
</template>

<style>
:root {
  font-family: Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;
  color: var(--color-ink);
  background: var(--color-canvas);
}
* {
  box-sizing: border-box;
}
body {
  margin: 0;
}
.workspace {
  max-width: 82rem;
  margin: auto;
  padding: clamp(1rem,3vw,2rem);
  --accent: #4768dc;
  --accent-dark: #334fc1;
  --line: #e4e9f2;
  --soft: #f6f8fc;
  --muted: #68758a;
}
.page-heading,.panel-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
.page-heading {
  margin: .25rem 0 1.6rem;
}
.page-heading h1 {
  font-size: clamp(1.6rem,3vw,2.25rem);
  letter-spacing: -.04em;
  margin: .35rem 0;
}
.page-heading p,.subtle-hint {
  margin: .25rem 0 0;
  color: var(--muted);
}
.eyebrow {
  color: var(--accent);
  font-size: .7rem;
  font-weight: 750;
  letter-spacing: .12em;
  text-transform: uppercase;
}
.month-control {
  display: flex;
  align-items: center;
  gap: .5rem;
}
.month-picker {
  display: grid;
  gap: .2rem;
  color: var(--muted);
  font-size: .75rem;
}
.month-picker input {
  font-weight: 650;
  color: var(--color-ink);
}
.icon-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.6rem;
  height: 2.6rem;
  padding: 0;
  border: 1px solid var(--line);
  border-radius: .7rem;
  background: white;
  color: var(--color-ink);
  font-size: 1.4rem;
}
.icon-button:hover,.quiet-button:hover {
  background: var(--soft);
  border-color: #c8d2e2;
}
.summary-grid {
  display: grid;
  grid-template-columns: repeat(4,minmax(0,1fr));
  gap: .85rem;
  margin-bottom: 1rem;
}
.summary-card {
  display: flex;
  align-items: center;
  gap: .8rem;
  min-width: 0;
  padding: 1rem 1.1rem;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: white;
  box-shadow: 0 5px 20px #22395d08;
}
.summary-icon {
  display: grid;
  place-items: center;
  flex: 0 0 2.75rem;
  height: 2.75rem;
  border-radius: .85rem;
  background: #eef2ff;
  color: var(--accent);
  font-size: 1.25rem;
}
.summary-hours .summary-icon {
  background: #e8f7f3;
  color: #16856f;
}
.summary-card p {
  margin: 0 0 .2rem;
  color: var(--muted);
  font-size: .78rem;
}
.summary-card strong {
  font-size: 1.3rem;
  letter-spacing: -.03em;
}
.summary-card small {
  font-size: .75rem;
  font-weight: 500;
  color: var(--muted);
}
.work-layout {
  display: grid;
  grid-template-columns: minmax(0,1.65fr) minmax(18rem,.85fr);
  align-items: start;
  gap: 1rem;
}
.main-column {
  min-width: 0;
}
.panel {
  min-width: 0;
  margin: 0 0 1rem;
  padding: clamp(1rem,2vw,1.35rem);
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: white;
  box-shadow: 0 5px 20px #22395d08;
}
.panel-heading {
  margin-bottom: 1rem;
}
.panel-heading h2 {
  margin: .2rem 0 0;
  font-size: 1.12rem;
  letter-spacing: -.02em;
}
.calendar-legend {
  display: flex;
  flex-wrap: wrap;
  gap: .75rem;
  color: var(--muted);
  font-size: .72rem;
}
.calendar-legend span {
  display: flex;
  align-items: center;
  gap: .35rem;
}
.calendar-legend i {
  width: .48rem;
  height: .48rem;
  border-radius: 50%;
  background: #4c71df;
}
.calendar-legend .legend-leave {
  background: #e69a50;
}
.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7,minmax(0,1fr));
  gap: .3rem;
}
.weekday {
  padding: .35rem;
  text-align: center;
  color: var(--muted);
  font-size: .72rem;
  font-weight: 650;
}
.calendar-cell {
  display: grid;
  place-items: center;
  min-width: 0;
}
.day-button {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: .25rem;
  width: min(100%,3.2rem);
  min-height: 3.15rem;
  padding: .3rem;
  border: 1px solid transparent;
  border-radius: .75rem;
  background: transparent;
  color: var(--color-ink);
  font-weight: 600;
}
.day-button:hover {
  background: #f1f4fb;
}
.day-button.selected {
  background: var(--accent);
  color: white;
  box-shadow: 0 4px 10px #4768dc42;
}
.day-button.holiday:not(.selected) {
  border-color: #f0d7ad;
  background: #fffaf1;
}
.day-button.weekend:not(.selected) {
  background: #ffddcc;
}
.day-markers {
  display: flex;
  gap: .16rem;
  height: .28rem;
}
.day-markers b {
  width: .25rem;
  height: .25rem;
  border-radius: 50%;
}
.entry-marker {
  background: #5c78d8;
}
.leave-marker {
  background: #e69a50;
}
.holiday-marker {
  background: #58a58b;
}
.day-button.selected .day-markers b {
  background: white;
}
.duration-pill,.count-badge {
  display: inline-flex;
  align-items: center;
  gap: .35rem;
  padding: .42rem .65rem;
  border-radius: 999px;
  background: #eef2ff;
  color: #425dbb;
  font-size: .75rem;
}
.duration-pill strong {
  font-size: .8rem;
}
.count-badge {
  min-width: 2rem;
  justify-content: center;
}
.entry-grid {
  display: grid;
  grid-template-columns: repeat(2,minmax(0,1fr));
  gap: .9rem 1rem;
}
.entry-grid>label,.inline-form label {
  display: grid;
  gap: .35rem;
  color: #47556c;
  font-size: .8rem;
  font-weight: 650;
}
.entry-grid input,.entry-grid select,.inline-form input,.inline-form select,.add-task-form input,.task-list input,.simple-list input,.simple-list select,.task-menu select {
  width: 100%;
  min-width: 0;
  min-height: 2.75rem;
  padding: .65rem .75rem;
  border: 1px solid #d6deea;
  border-radius: .65rem;
  background: #fff;
  color: var(--color-ink);
  font: inherit;
}
.entry-grid input:focus,.entry-grid select:focus,.inline-form input:focus,.inline-form select:focus,.add-task-form input:focus,.task-list input:focus {
  outline: 2px solid #b9c8ff;
  outline-offset: 1px;
  border-color: var(--accent);
}
.entry-grid input::placeholder,.add-task-form input::placeholder {
  color: #98a3b4;
  font-weight: 400;
}
.time-fields {
  display: grid;
  grid-template-columns: minmax(0,1fr) auto minmax(0,1fr);
  align-items: end;
  gap: .5rem;
}
.time-fields label {
  display: grid;
  gap: .35rem;
  color: #47556c;
  font-size: .8rem;
  font-weight: 650;
}
.time-fields>span {
  padding-bottom: .75rem;
  color: #9aa5b5;
}
.field-wide {
  grid-column: 1/-1;
}
.form-actions {
  display: flex;
  align-items: center;
  gap: .55rem;
  grid-column: 1/-1;
  margin-top: .15rem;
}
.primary-button,.quiet-button,.danger-button,.use-task-button {
  min-height: 2.55rem;
  padding: .58rem .85rem;
  border: 1px solid transparent;
  border-radius: .65rem;
  font: inherit;
  font-size: .82rem;
  font-weight: 650;
  cursor: pointer;
}
.primary-button {
  background: var(--accent);
  color: white;
  box-shadow: 0 3px 8px #4768dc2e;
}
.primary-button:hover {
  background: var(--accent-dark);
}
.primary-button span {
  margin-right: .35rem;
  font-size: 1.1rem;
}
.quiet-button {
  border-color: var(--line);
  background: white;
  color: #536178;
}
.danger-button {
  background: transparent;
  color: #b54747;
}
.danger-button:hover {
  background: #fff1f0;
}
.notice,.feedback {
  display: flex;
  align-items: center;
  gap: .55rem;
  margin: 0 0 1rem;
  padding: .8rem 1rem;
  border-radius: .75rem;
}
.notice {
  border: 1px solid #f1dfb4;
  background: #fffaf0;
  color: #87601a;
}
.feedback {
  display: block;
}
.error {
  border: 1px solid #f0c9c7;
  background: #fff4f3;
  color: #a20e26;
}
.success {
  border: 1px solid #bee6d4;
  background: #f0fbf5;
  color: #14744f;
}
.subtle-hint {
  margin: -.45rem 0 1rem;
  font-size: .78rem;
}
.task-panel {
  position: sticky;
  top: 1rem;
}
.task-panel .panel-heading {
  align-items: flex-start;
}
.add-task-form {
  display: flex;
  gap: .45rem;
}
.add-task-button {
  flex: 0 0 2.75rem;
  background: #edf1ff;
  color: var(--accent);
  font-weight: 700;
}
.task-list,.simple-list {
  display: grid;
  gap: .55rem;
  margin: 1rem 0;
  padding: 0;
  list-style: none;
}
.task-list li {
  display: flex;
  align-items: center;
  gap: .35rem;
  min-width: 0;
  margin: 0;
  padding: .4rem;
  border: 1px solid var(--line);
  border-radius: .75rem;
  background: #fff;
}
.task-list input {
  min-height: 2.3rem;
  padding: .4rem .5rem;
  border: 0;
  background: transparent;
}
.task-grip {
  color: #a1adbd;
  cursor: grab;
}
.use-task-button {
  min-height: 2.3rem;
  padding: .4rem .55rem;
  background: #eff8f5;
  color: #16856f;
  white-space: nowrap;
}
.task-more {
  position: relative;
}
.task-more summary {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2.3rem;
  border-radius: .5rem;
  color: #66738a;
  cursor: pointer;
  font-weight: 700;
}
.task-more summary:hover {
  background: var(--soft);
}
.task-menu {
  position: absolute;
  right: 0;
  z-index: 5;
  display: grid;
  gap: .45rem;
  width: 11rem;
  padding: .65rem;
  border: 1px solid var(--line);
  border-radius: .75rem;
  background: white;
  box-shadow: 0 12px 30px #1a2b471a;
}
.task-menu label {
  display: grid;
  gap: .25rem;
  color: var(--muted);
  font-size: .75rem;
}
.task-menu select {
  min-height: 2.2rem;
}
.task-menu button {
  padding: .4rem;
  border: 0;
  border-radius: .4rem;
  background: var(--soft);
  color: #536178;
  text-align: left;
}
.quick-picks {
  margin-top: 1rem;
}
.quick-picks h3,.holiday-list h3 {
  margin: .7rem 0 .45rem;
  color: var(--muted);
  font-size: .76rem;
}
.quick-picks button {
  max-width: 100%;
  margin: .15rem;
  padding: .4rem .65rem;
  border: 1px solid #e2e8f2;
  border-radius: 999px;
  background: #f9faff;
  color: #52617c;
  font-size: .75rem;
}
.quick-picks button:hover {
  border-color: #b9c8ff;
  background: #f1f4ff;
}
.entries-panel {
  margin-top: 0;
}
.table-wrap {
  overflow-x: auto;
}
table {
  width: 100%;
  border-collapse: collapse;
}
th,td {
  text-align: left;
  border-bottom: 1px solid #edf0f5;
  padding: .8rem .65rem;
  font-size: .8rem;
}
th {
  color: #778398;
  font-size: .72rem;
  font-weight: 700;
}
td {
  color: #4c596d;
}
tbody tr:last-child:not(:has(form)) td {
  border-bottom: 0;
}
.task-name-cell {
  min-width: 10rem;
  color: var(--color-ink);
  font-weight: 600;
}
.row-actions {
  white-space: nowrap;
}
.row-actions button {
  min-height: 2rem;
  padding: .35rem .5rem;
  font-size: .74rem;
}
.row-actions .danger-button {
  margin-left: .15rem;
}
.edit-grid {
  padding: .8rem;
  background: var(--soft);
  border-radius: .75rem;
}
.support-grid {
  display: grid;
  grid-template-columns: minmax(0,1fr);
  gap: 1rem;
}
.support-panel {
  margin: 0;
}
.inline-form {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  align-items: end;
  gap: .6rem;
}
.inline-form .primary-button {
  white-space: nowrap;
}
.simple-list li {
  display: flex;
  align-items: center;
  gap: .5rem;
  margin: 0;
  padding: .6rem 0;
  border-bottom: 1px solid #edf0f5;
  font-size: .8rem;
}
.simple-list li:last-child {
  border-bottom: 0;
}
.simple-list li>time {
  color: #52617c;
}
.simple-list li>span {
  margin-right: auto;
}
.simple-list input,.simple-list select {
  min-height: 2.3rem;
  padding: .4rem;
  font-size: .75rem;
}
.simple-list button {
  min-height: 2.2rem;
  padding: .4rem .55rem;
  font-size: .72rem;
}
.holiday-list {
  margin-top: 1rem;
  padding-top: .5rem;
  border-top: 1px solid var(--line);
}
.holiday-list p {
  display: flex;
  gap: .75rem;
  margin: .45rem 0;
  color: #788498;
  font-size: .78rem;
}
.empty-state {
  color: #8793a6;
  font-size: .78rem;
  text-align: center;
}
.empty-panel {
  padding: 2.5rem 1rem;
  text-align: center;
}
.empty-panel>span {
  display: grid;
  place-items: center;
  width: 3rem;
  height: 3rem;
  margin: 0 auto .7rem;
  border-radius: 1rem;
  background: #eef2ff;
  color: var(--accent);
  font-size: 1.4rem;
}
.empty-panel h3 {
  margin: .2rem 0;
  font-size: 1rem;
}
.empty-panel p {
  max-width: 26rem;
  margin: .3rem auto;
  color: var(--muted);
  font-size: .82rem;
}
.sign-in-card {
  padding: 1.25rem;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: white;
  color: var(--muted);
}
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0,0,0,0);
  white-space: nowrap;
  border: 0;
}
@media(max-width:62rem) {
  .summary-grid {
    grid-template-columns: repeat(2,minmax(0,1fr));
  }
  .work-layout {
    grid-template-columns: minmax(0,1.2fr) minmax(16rem,.8fr);
  }
  .task-panel {
    position: static;
  }
}
@media(max-width:46rem) {
  .workspace {
    padding: 1rem;
  }
  .page-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .month-control {
    align-self: stretch;
    justify-content: space-between;
  }
  .month-picker {
    flex: 1;
  }
  .month-picker input {
    width: 100%;
  }
  .work-layout {
    grid-template-columns: 1fr;
  }
  .calendar-panel {
    order: 0;
  }
  .entry-panel {
    order: 1;
  }
  .task-panel {
    order: 2;
  }
  .summary-grid {
    gap: .55rem;
  }
  .summary-card {
    gap: .55rem;
    padding: .75rem;
  }
  .summary-icon {
    flex-basis: 2.3rem;
    height: 2.3rem;
  }
  .summary-card p {
    font-size: .7rem;
  }
  .summary-card strong {
    font-size: 1.15rem;
  }
  .panel-heading {
    align-items: flex-start;
  }
  .calendar-legend {
    font-size: 0;
  }
  .calendar-legend i {
    width: .55rem;
    height: .55rem;
  }
  .day-button {
    min-height: 2.65rem;
  }
  .support-grid {
    grid-template-columns: 1fr;
  }
  .optional-column {
    display: none;
  }
  th,td {
    padding: .65rem .4rem;
  }
  .task-name-cell {
    min-width: 7rem;
  }
  .row-actions {
    display: grid;
    gap: .2rem;
  }
  .row-actions .danger-button {
    margin: 0;
  }
}
@media(max-width:26rem) {
  .workspace {
    padding: .75rem;
  }
  .summary-card {
    align-items: flex-start;
    flex-direction: column;
    gap: .35rem;
    padding: .65rem;
  }
  .summary-icon {
    flex-basis: 2rem;
    width: 2rem;
    height: 2rem;
  }
  .entry-grid {
    grid-template-columns: 1fr;
  }
  .field-wide,.form-actions {
    grid-column: auto;
  }
  .time-fields {
    grid-template-columns: minmax(0,1fr) auto minmax(0,1fr);
  }
  .panel {
    padding: .85rem;
    border-radius: .85rem;
  }
  .calendar-grid {
    gap: .1rem;
  }
  .day-button {
    min-height: 2.25rem;
    border-radius: .55rem;
  }
  .inline-form {
    grid-template-columns: 1fr 1fr;
  }
  .inline-form .primary-button {
    grid-column: 1/-1;
  }
  .duration-pill {
    flex-wrap: wrap;
    font-size: .68rem;
  }
  .simple-list li {
    flex-wrap: wrap;
  }
  .simple-list input {
    min-width: 7rem;
  }
  .simple-list select {
    min-width: 5rem;
  }
}
</style>
