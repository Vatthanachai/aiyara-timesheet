<script setup lang="ts">
type Entry = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null; projectId: string | null; categoryId: string | null; personalTaskId: string | null }
type Task = { id: string; name: string; status: string; projectId: string | null; categoryId: string | null }
type Leave = { id: string; date: string; kind: string; notes: string | null }
type Catalog = { projects: { id: string; name: string }[]; categories: { id: string; name: string }[]; holidays: { id: string; date: string; name: string }[] }
const token = ref('')
const config = useRuntimeConfig()
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
const text = computed(() => locale.value === 'th' ? {
  title: 'บันทึกเวลาทำงาน', month: 'เดือน', task: 'งาน', date: 'วันที่', start: 'เริ่ม', end: 'สิ้นสุด', duration: 'ชั่วโมง', detail: 'รายละเอียด', notes: 'หมายเหตุ', project: 'โครงการ', category: 'หมวดหมู่', save: 'บันทึก', cancel: 'ยกเลิก', edit: 'แก้ไข', remove: 'ลบ', add: 'เพิ่มรายการ', tasks: 'งานส่วนตัว', leaves: 'วันลา', kind: 'ประเภท', readOnly: 'เดือนที่ผ่านมาอ่านได้อย่างเดียว', noEntries: 'ยังไม่มีรายการ', status: 'สถานะ', drop: 'ลากงานมาที่แบบฟอร์มเพื่อเริ่มบันทึก'
} : {
  title: 'Time entries', month: 'Month', task: 'Task', date: 'Date', start: 'Start', end: 'End', duration: 'Hours', detail: 'Detail', notes: 'Notes', project: 'Project', category: 'Category', save: 'Save', cancel: 'Cancel', edit: 'Edit', remove: 'Delete', add: 'Add entry', tasks: 'Personal tasks', leaves: 'Leave', kind: 'Kind', readOnly: 'Past months are read only', noEntries: 'No entries yet', status: 'Status', drop: 'Drag a task here to start an entry'
})
onMounted(() => {
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = event.data.locale === 'en' ? 'en' : 'th'
    if (token.value) load()
  })
})
async function api<T>(path: string, options: { method?: string; body?: object } = {}) {
  return await $fetch<T>(`${gateway.value}/api/v1/timesheets${path}`, {
    ...options, headers: { Authorization: `Bearer ${token.value}` }
  })
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
  } catch { error.value = locale.value === 'th' ? 'โหลดข้อมูลไม่สำเร็จ' : 'Could not load data' }
}
watch(month, load)
function resetForm() {
  editingId.value = null
  Object.assign(form, { date: `${month.value}-01`, startTime: '09:00', endTime: '17:00', taskName: '', detail: '', notes: '', projectId: '', categoryId: '', personalTaskId: '' })
}
function edit(entry: Entry) {
  editingId.value = entry.id
  Object.assign(form, { date: entry.date, startTime: entry.startTime.slice(0, 5), endTime: entry.endTime.slice(0, 5),
    taskName: entry.taskName, detail: entry.detail || '', notes: entry.notes || '',
    projectId: entry.projectId || '', categoryId: entry.categoryId || '', personalTaskId: entry.personalTaskId || '' })
}
function dragTask(event: DragEvent, task: Task) { event.dataTransfer?.setData('application/x-aiyara-task', JSON.stringify(task)) }
function dropTask(event: DragEvent) {
  const value = event.dataTransfer?.getData('application/x-aiyara-task')
  if (!value) return
  const task = JSON.parse(value) as Task
  form.taskName = task.name; form.personalTaskId = task.id
  form.projectId = task.projectId || ''; form.categoryId = task.categoryId || ''
}
async function saveEntry() {
  if (!canEdit.value) return
  try {
    await api(`/entries${editingId.value ? `/${editingId.value}` : ''}`, { method: editingId.value ? 'PUT' : 'POST',
      body: { ...form, startTime: `${form.startTime}:00`, endTime: `${form.endTime}:00`,
        projectId: form.projectId || null, categoryId: form.categoryId || null, personalTaskId: form.personalTaskId || null } })
    notice.value = locale.value === 'th' ? 'บันทึกแล้ว' : 'Saved'
    resetForm(); await load()
  } catch { error.value = locale.value === 'th' ? 'บันทึกไม่สำเร็จ' : 'Save failed' }
}
async function removeEntry(id: string) {
  if (!canEdit.value || !confirm(locale.value === 'th' ? 'ลบรายการนี้?' : 'Delete this entry?')) return
  try { await api(`/entries/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = locale.value === 'th' ? 'ลบไม่สำเร็จ' : 'Delete failed' }
}
async function addTask() {
  if (!taskName.value.trim()) return
  try { await api('/tasks', { method: 'POST', body: { name: taskName.value, status: 'todo' } }); taskName.value = ''; await load() }
  catch { error.value = locale.value === 'th' ? 'เพิ่มงานไม่สำเร็จ' : 'Could not add task' }
}
async function setStatus(task: Task, status: string) {
  try { await api(`/tasks/${task.id}`, { method: 'PUT', body: { name: task.name, status, projectId: task.projectId, categoryId: task.categoryId } }); await load() }
  catch { error.value = locale.value === 'th' ? 'เปลี่ยนสถานะไม่สำเร็จ' : 'Could not update task' }
}
async function renameTask(task: Task) {
  try { await api(`/tasks/${task.id}`, { method: 'PUT', body: { name: task.name, status: task.status, projectId: task.projectId, categoryId: task.categoryId } }); await load() }
  catch { error.value = locale.value === 'th' ? 'แก้ไขงานไม่สำเร็จ' : 'Could not update task' }
}
async function removeTask(id: string) {
  try { await api(`/tasks/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = locale.value === 'th' ? 'ลบงานไม่สำเร็จ' : 'Could not delete task' }
}
async function addLeave() {
  if (!canEdit.value) return
  try { await api('/leave', { method: 'POST', body: { date: leaveDate.value, kind: leaveKind.value } }); await load() }
  catch { error.value = locale.value === 'th' ? 'เพิ่มวันลาไม่สำเร็จ' : 'Could not add leave' }
}
async function removeLeave(id: string) {
  if (!canEdit.value) return
  try { await api(`/leave/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = locale.value === 'th' ? 'ลบวันลาไม่สำเร็จ' : 'Could not delete leave' }
}
async function updateLeave(leave: Leave) {
  if (!canEdit.value) return
  try { await api(`/leave/${leave.id}`, { method: 'PUT', body: { date: leave.date, kind: leave.kind, notes: leave.notes } }); await load() }
  catch { error.value = locale.value === 'th' ? 'แก้ไขวันลาไม่สำเร็จ' : 'Could not update leave' }
}
</script>

<template>
  <main class="workspace">
    <p v-if="!token">{{ locale === 'th' ? 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell' : 'Please sign in through Aiyara Shell' }}</p>
    <template v-else>
      <header><h2>{{ text.title }}</h2><label>{{ text.month }} <input v-model="month" type="month" /></label></header>
      <p v-if="!canEdit" class="notice">{{ text.readOnly }}</p>
      <p v-if="error" role="alert" class="error">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <section class="panel">
        <h3>{{ text.title }}</h3>
        <div class="table-wrap"><table><thead><tr><th>{{ text.date }}</th><th>{{ text.start }}</th><th>{{ text.end }}</th><th>{{ text.duration }}</th><th>{{ text.task }}</th><th>{{ text.project }}</th><th>{{ text.category }}</th><th></th></tr></thead>
          <tbody><template v-for="entry in entries" :key="entry.id"><tr><td>{{ entry.date }}</td><td>{{ entry.startTime }}</td><td>{{ entry.endTime }}</td><td>{{ (entry.durationMinutes / 60).toFixed(2) }}</td><td>{{ entry.taskName }}</td><td>{{ catalog.projects.find(x => x.id === entry.projectId)?.name }}</td><td>{{ catalog.categories.find(x => x.id === entry.categoryId)?.name }}</td><td><button v-if="canEdit" @click="edit(entry)">{{ text.edit }}</button> <button v-if="canEdit" @click="removeEntry(entry.id)">{{ text.remove }}</button></td></tr>
          <tr v-if="editingId === entry.id"><td colspan="8"><form class="entry-grid" @submit.prevent="saveEntry"><label>{{ text.date }}<input v-model="form.date" type="date" required /></label><label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label><label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label><label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" /></label><label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" /></label><label>{{ text.notes }}<input v-model="form.notes" maxlength="4000" /></label><div class="actions"><button type="submit">{{ text.save }}</button><button type="button" @click="resetForm">{{ text.cancel }}</button></div></form></td></tr></template>
          <tr v-if="!entries.length"><td colspan="8">{{ text.noEntries }}</td></tr></tbody></table></div>
      </section>
      <section v-if="canEdit && !editingId" class="panel" @dragover.prevent @drop.prevent="dropTask"><h3>{{ text.add }}</h3><p>{{ text.drop }}</p>
        <form class="entry-grid" @submit.prevent="saveEntry">
          <label>{{ text.date }}<input v-model="form.date" type="date" required :min="`${month}-01`" :max="`${month}-${new Date(Number(month.slice(0, 4)), Number(month.slice(5)), 0).getDate()}`" /></label>
          <label>{{ text.start }}<input v-model="form.startTime" type="time" required /></label>
          <label>{{ text.end }}<input v-model="form.endTime" type="time" required /></label>
          <label>{{ text.task }}<input v-model="form.taskName" required maxlength="200" /></label>
          <label>{{ text.project }}<select v-model="form.projectId"><option value="">—</option><option v-for="project in catalog.projects" :key="project.id" :value="project.id">{{ project.name }}</option></select></label>
          <label>{{ text.category }}<select v-model="form.categoryId"><option value="">—</option><option v-for="category in catalog.categories" :key="category.id" :value="category.id">{{ category.name }}</option></select></label>
          <label>{{ text.detail }}<input v-model="form.detail" maxlength="4000" /></label>
          <label>{{ text.notes }}<input v-model="form.notes" maxlength="4000" /></label>
          <div class="actions"><button type="submit">{{ text.save }}</button><button type="button" @click="resetForm">{{ text.cancel }}</button></div>
        </form>
      </section>
      <div class="columns"><section class="panel"><h3>{{ text.tasks }}</h3><form @submit.prevent="addTask"><input v-model="taskName" :aria-label="text.task" maxlength="200" /><button type="submit">{{ text.add }}</button></form>
          <ul><li v-for="task in tasks" :key="task.id" draggable="true" @dragstart="dragTask($event, task)"><input v-model="task.name" :aria-label="text.task" maxlength="200" /><button @click="renameTask(task)">{{ text.save }}</button><select :value="task.status" :aria-label="text.status" @change="setStatus(task, ($event.target as HTMLSelectElement).value)"><option value="todo">To do</option><option value="doing">Doing</option><option value="done">Done</option></select><button @click="removeTask(task.id)">{{ text.remove }}</button></li></ul>
        </section><section class="panel"><h3>{{ text.leaves }}</h3><form v-if="canEdit" @submit.prevent="addLeave"><input v-model="leaveDate" type="date" :min="`${month}-01`" :max="`${month}-${new Date(Number(month.slice(0, 4)), Number(month.slice(5)), 0).getDate()}`" /><select v-model="leaveKind" :aria-label="text.kind"><option value="annual">Annual</option><option value="sick">Sick</option><option value="other">Other</option></select><button type="submit">{{ text.add }}</button></form>
          <ul><li v-for="leave in leaves" :key="leave.id"><template v-if="canEdit"><input v-model="leave.date" type="date" :aria-label="text.date" /><select v-model="leave.kind" :aria-label="text.kind"><option value="annual">Annual</option><option value="sick">Sick</option><option value="other">Other</option></select><button @click="updateLeave(leave)">{{ text.save }}</button><button @click="removeLeave(leave.id)">{{ text.remove }}</button></template><template v-else>{{ leave.date }} · {{ leave.kind }}</template></li></ul>
          <h4>{{ locale === 'th' ? 'วันหยุด' : 'Holidays' }}</h4><ul><li v-for="holiday in catalog.holidays.filter(x => x.date.startsWith(month))" :key="holiday.id">{{ holiday.date }} · {{ holiday.name }}</li></ul>
        </section></div>
    </template>
  </main>
</template>

<style>
:root{font-family:system-ui,sans-serif;color:#172033;background:#f8fafc}*{box-sizing:border-box}body{margin:0}.workspace{max-width:80rem;margin:auto;padding:1rem}header,.columns{display:flex;gap:1rem;justify-content:space-between;flex-wrap:wrap}.panel{background:white;border:1px solid #d8dee9;border-radius:.75rem;padding:1rem;margin:1rem 0;flex:1;min-width:18rem}.table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;min-width:45rem}th,td{text-align:left;border-bottom:1px solid #e4e8ee;padding:.65rem}label{display:grid;gap:.25rem}input,select,button{font:inherit;padding:.55rem;border:1px solid #abb5c5;border-radius:.4rem}button{cursor:pointer;background:#eaf1ff;color:#1241a1}.entry-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(10rem,1fr));gap:.7rem}.actions{display:flex;align-items:end;gap:.5rem}.notice{background:#fff1cf;padding:.7rem}.error{color:#a20e26}ul{padding-left:1.3rem}li{margin:.5rem 0}li[draggable]{cursor:grab;display:flex;justify-content:space-between;gap:1rem}
</style>
