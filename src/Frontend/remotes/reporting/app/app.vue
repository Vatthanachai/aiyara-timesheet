<script setup lang="ts">
type Definition = { id: string; kind: string; format: string; name: string }
type Run = { id: string; subjectUserId: string | null; status: string; periodStartUtc: string; periodEndUtc: string; attemptCount: number; createdAtUtc: string; kind: string; format: string; name: string }
type Schedule = { id: string; reportDefinitionId: string; timeZoneId: string; localTime: string; nextFireAtUtc: string; kind: string; format: string; name: string }
type Dashboard = { total: number; ready: number; queued: number; failed: number }
const config = useRuntimeConfig()
const token = ref('')
const gateway = ref('http://localhost:8081')
const locale = ref<'th' | 'en'>('th')
const isAdmin = ref(false)
const timeZoneId = ref('Asia/Bangkok')
const definitions = ref<Definition[]>([])
const runs = ref<Run[]>([])
const schedules = ref<Schedule[]>([])
const dashboard = ref<Dashboard>({ total: 0, ready: 0, queued: 0, failed: 0 })
const retentionYears = ref(7)
const selectedDefinition = ref('')
const startDate = ref('')
const endDate = ref('')
const definitionForm = reactive({ kind: 'Monthly', format: 'Pdf', name: '' })
const scheduleForm = reactive({ reportDefinitionId: '', timeZoneId: 'Asia/Bangkok', localTime: '00:15' })
const error = ref('')
const notice = ref('')
const copy = computed(() => locale.value === 'th' ? {
  title: 'รายงานและเอกสาร', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
  dashboard: 'ภาพรวม', total: 'รายงานทั้งหมด', ready: 'พร้อมดาวน์โหลด', queued: 'กำลังดำเนินการ', failed: 'ผิดพลาด',
  request: 'ขอรายงาน', definition: 'รูปแบบรายงาน', kind: 'ประเภทรายงาน', format: 'ไฟล์',
  start: 'ตั้งแต่วันที่', end: 'ถึงวันที่', create: 'สร้างรายงาน', history: 'ประวัติรายงาน',
  name: 'ชื่อ', status: 'สถานะ', period: 'ช่วงเวลา', created: 'วันที่ขอ', download: 'ดาวน์โหลด',
  signed: 'อัปโหลด PDF ที่เซ็นแล้ว', schedules: 'กำหนดการ', timezone: 'เขตเวลา',
  localTime: 'เวลาท้องถิ่น', nextFire: 'รอบถัดไป', add: 'เพิ่มกำหนดการ', stop: 'ปิด',
  settings: 'ตั้งค่ารายงาน', retention: 'เก็บไฟล์ (ปี)', save: 'บันทึก',
  monthly: 'รายเดือน', weekly: 'รายสัปดาห์', annual: 'รายปี', performance: 'ประสิทธิภาพ',
  pdf: 'PDF', xlsx: 'Excel', createDefinition: 'เพิ่มรูปแบบ',
  queued: 'รอสร้าง', running: 'กำลังสร้าง', succeeded: 'เสร็จแล้ว', failed: 'ผิดพลาด',
  generated: 'ส่งคำขอสร้างรายงานแล้ว', saved: 'บันทึกแล้ว', upload: 'อัปโหลด',
  loadingError: 'โหลดข้อมูลไม่สำเร็จ', actionError: 'ทำรายการไม่สำเร็จ', noRuns: 'ยังไม่มีรายงาน',
  requiresLocked: 'สร้างรายงานได้เมื่อเดือนต้นทางถูกล็อกแล้ว'
} : {
  title: 'Reports and documents', signIn: 'Please sign in through Aiyara Shell',
  dashboard: 'Summary', total: 'Total reports', ready: 'Ready to download', queued: 'In progress', failed: 'Failed',
  request: 'Request a report', definition: 'Report format', kind: 'Report type', format: 'File',
  start: 'From', end: 'To', create: 'Generate report', history: 'Report history',
  name: 'Name', status: 'Status', period: 'Period', created: 'Requested', download: 'Download',
  signed: 'Upload signed PDF', schedules: 'Schedules', timezone: 'Time zone',
  localTime: 'Local time', nextFire: 'Next run', add: 'Add schedule', stop: 'Disable',
  settings: 'Report settings', retention: 'Retain files (years)', save: 'Save',
  monthly: 'Monthly', weekly: 'Weekly', annual: 'Annual', performance: 'Performance',
  pdf: 'PDF', xlsx: 'Excel', createDefinition: 'Add format',
  queued: 'Queued', running: 'Generating', succeeded: 'Succeeded', failed: 'Failed',
  generated: 'Report generation queued', saved: 'Saved', upload: 'Upload',
  loadingError: 'Could not load reports', actionError: 'Action failed', noRuns: 'No reports yet',
  requiresLocked: 'Source months must be locked before report generation'
})

onMounted(() => window.addEventListener('message', (event: MessageEvent) => {
  if (event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
  token.value = event.data.token || ''
  gateway.value = event.data.gatewayUrl || gateway.value
  locale.value = event.data.locale === 'en' ? 'en' : 'th'
  if (token.value) load()
}))

async function api<T>(path: string, options: { method?: string; body?: unknown; headers?: Record<string, string> } = {}) {
  return await $fetch<T>(`${gateway.value}/api/v1/reports${path}`, {
    ...options, headers: { Authorization: `Bearer ${token.value}`, ...options.headers }
  })
}

async function load() {
  try {
    const context = await api<{ isAdmin: boolean; timeZoneId: string }>('/context')
    isAdmin.value = context.isAdmin
    timeZoneId.value = context.timeZoneId || 'Asia/Bangkok'
    const [items, history, summary] = await Promise.all([
      api<Definition[]>('/definitions'), api<Run[]>('/runs'), api<Dashboard>('/dashboard')
    ])
    definitions.value = items
    runs.value = history
    dashboard.value = summary
    selectedDefinition.value ||= items[0]?.id || ''
    if (isAdmin.value) {
      const [scheduleList, policy] = await Promise.all([
        api<Schedule[]>('/schedules'), api<{ years: number }>('/retention')
      ])
      schedules.value = scheduleList
      retentionYears.value = policy.years
    }
    error.value = ''
  } catch { error.value = copy.value.loadingError }
}

async function createRun() {
  try {
    const start = tenantLocalMidnight(startDate.value, timeZoneId.value)
    const [year, month, day] = endDate.value.split('-').map(Number)
    const nextEnd = new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10)
    const end = tenantLocalMidnight(nextEnd, timeZoneId.value)
    await api('/runs', { method: 'POST', body: {
      definitionId: selectedDefinition.value,
      periodStartUtc: start.toISOString(), periodEndUtc: end.toISOString(), subjectUserId: null
    } })
    notice.value = copy.value.generated
    await load()
  } catch { error.value = `${copy.value.actionError} · ${copy.value.requiresLocked}` }
}

function tenantLocalMidnight(dateText: string, zone: string) {
  const [year, month, day] = dateText.split('-').map(Number)
  const target = Date.UTC(year, month - 1, day)
  let guess = target
  const formatter = new Intl.DateTimeFormat('en-US', {
    timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  })
  for (let attempt = 0; attempt < 2; attempt++) {
    const parts = Object.fromEntries(formatter.formatToParts(new Date(guess))
      .filter(part => part.type !== 'literal').map(part => [part.type, Number(part.value)]))
    const displayed = Date.UTC(parts.year, parts.month - 1, parts.day,
      parts.hour, parts.minute, parts.second)
    guess += target - displayed
  }
  return new Date(guess)
}

async function addDefinition() {
  try {
    await api('/definitions', { method: 'POST', body: definitionForm })
    definitionForm.name = ''
    notice.value = copy.value.saved
    await load()
  } catch { error.value = copy.value.actionError }
}

async function addSchedule() {
  try {
    await api('/schedules', { method: 'POST', body: { ...scheduleForm, localTime: `${scheduleForm.localTime}:00` } })
    notice.value = copy.value.saved
    await load()
  } catch { error.value = copy.value.actionError }
}

async function disableSchedule(id: string) {
  try { await api(`/schedules/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = copy.value.actionError }
}

async function saveRetention() {
  try { await api('/retention', { method: 'PUT', body: { years: retentionYears.value } }); notice.value = copy.value.saved }
  catch { error.value = copy.value.actionError }
}

async function download(run: Run) {
  try {
    const response = await fetch(`${gateway.value}/api/v1/reports/runs/${run.id}/download`, {
      headers: { Authorization: `Bearer ${token.value}` }
    })
    if (!response.ok) throw new Error()
    const objectUrl = URL.createObjectURL(await response.blob())
    const anchor = document.createElement('a')
    anchor.href = objectUrl; anchor.download = `report-${run.id}.${run.format.toLowerCase()}`
    anchor.click(); URL.revokeObjectURL(objectUrl)
  } catch { error.value = copy.value.actionError }
}

async function uploadSigned(run: Run, event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  try {
    const form = new FormData(); form.append('file', file)
    const response = await fetch(`${gateway.value}/api/v1/reports/runs/${run.id}/signed-document`, {
      method: 'POST', headers: { Authorization: `Bearer ${token.value}` }, body: form
    })
    if (!response.ok) throw new Error()
    notice.value = copy.value.saved
    await load()
  } catch { error.value = copy.value.actionError }
  ;(event.target as HTMLInputElement).value = ''
}

function statusLabel(status: string) {
  return ({ Queued: copy.value.queued, Running: copy.value.running,
    Succeeded: copy.value.succeeded, Failed: copy.value.failed } as Record<string, string>)[status] || status
}
</script>

<template>
  <main>
    <h2>{{ copy.title }}</h2>
    <p v-if="!token">{{ copy.signIn }}</p>
    <template v-else>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <section><h3>{{ copy.dashboard }}</h3><dl class="dashboard">
        <div><dt>{{ copy.total }}</dt><dd>{{ dashboard.total }}</dd></div>
        <div><dt>{{ copy.ready }}</dt><dd>{{ dashboard.ready }}</dd></div>
        <div><dt>{{ copy.queued }}</dt><dd>{{ dashboard.queued }}</dd></div>
        <div><dt>{{ copy.failed }}</dt><dd>{{ dashboard.failed }}</dd></div>
      </dl></section>
      <section>
        <h3>{{ copy.request }}</h3>
        <form @submit.prevent="createRun">
          <label>{{ copy.definition }}<select v-model="selectedDefinition" required><option v-for="item in definitions" :key="item.id" :value="item.id">{{ item.name }} · {{ item.format }}</option></select></label>
          <label>{{ copy.start }}<input v-model="startDate" type="date" required /></label>
          <label>{{ copy.end }}<input v-model="endDate" type="date" required :min="startDate" /></label>
          <button :disabled="!selectedDefinition">{{ copy.create }}</button>
        </form>
      </section>
      <section>
        <h3>{{ copy.history }}</h3>
        <p v-if="runs.length === 0">{{ copy.noRuns }}</p>
        <div class="table-wrap"><table v-if="runs.length">
          <thead><tr><th>{{ copy.name }}</th><th>{{ copy.period }}</th><th>{{ copy.status }}</th><th>{{ copy.created }}</th><th /></tr></thead>
          <tbody><tr v-for="run in runs" :key="run.id">
            <td>{{ run.name }} · {{ run.subjectUserId?.slice(0, 8) || '—' }}</td>
            <td>{{ new Date(run.periodStartUtc).toLocaleDateString() }} – {{ new Date(run.periodEndUtc).toLocaleDateString() }}</td>
            <td><span class="status" :data-status="run.status">{{ statusLabel(run.status) }}</span></td>
            <td>{{ new Date(run.createdAtUtc).toLocaleString() }}</td>
            <td class="actions"><button v-if="run.status === 'Succeeded'" @click="download(run)">{{ copy.download }}</button>
              <label v-if="isAdmin && run.status === 'Succeeded' && run.kind === 'Monthly'" class="upload">{{ copy.signed }}<input type="file" accept="application/pdf,.pdf" @change="uploadSigned(run, $event)" /></label></td>
          </tr></tbody>
        </table></div>
      </section>
      <template v-if="isAdmin">
        <section><h3>{{ copy.settings }}</h3>
          <form class="settings" @submit.prevent="addDefinition">
            <label>{{ copy.kind }}<select v-model="definitionForm.kind"><option>Weekly</option><option>Monthly</option><option>Annual</option><option>Performance</option></select></label>
            <label>{{ copy.format }}<select v-model="definitionForm.format"><option>Pdf</option><option>Xlsx</option></select></label>
            <label>{{ copy.name }}<input v-model="definitionForm.name" required maxlength="160" /></label>
            <button>{{ copy.createDefinition }}</button>
          </form>
          <form class="settings retention" @submit.prevent="saveRetention"><label>{{ copy.retention }}<input v-model.number="retentionYears" type="number" min="1" max="20" /></label><button>{{ copy.save }}</button></form>
        </section>
        <section><h3>{{ copy.schedules }}</h3>
          <form class="settings" @submit.prevent="addSchedule">
            <label>{{ copy.definition }}<select v-model="scheduleForm.reportDefinitionId" required><option v-for="item in definitions.filter(x => x.kind !== 'Performance')" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
            <label>{{ copy.timezone }}<input v-model="scheduleForm.timeZoneId" required maxlength="100" /></label>
            <label>{{ copy.localTime }}<input v-model="scheduleForm.localTime" type="time" required /></label>
            <button :disabled="!scheduleForm.reportDefinitionId">{{ copy.add }}</button>
          </form>
          <ul class="schedule-list"><li v-for="schedule in schedules" :key="schedule.id"><span>{{ schedule.name }} · {{ schedule.timeZoneId }} · {{ new Date(schedule.nextFireAtUtc).toLocaleString() }}</span><button @click="disableSchedule(schedule.id)">{{ copy.stop }}</button></li></ul>
        </section>
      </template>
    </template>
  </main>
</template>

<style>
:root{font-family:system-ui,sans-serif;color:var(--color-ink);background:var(--color-canvas)}
main{max-width:80rem;margin:auto;padding:1rem;color:var(--color-ink)}
section{background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-card);padding:1rem;margin:1rem 0}
form{display:flex;flex-wrap:wrap;align-items:end;gap:.75rem}label{display:grid;gap:.35rem;min-width:10rem;flex:1}
input,select,button{font:inherit;padding:.55rem;border:1px solid var(--color-border);border-radius:.4rem;background:var(--color-surface);color:var(--color-ink)}
button{cursor:pointer;background:var(--color-brand);color:white}button:disabled{opacity:.55;cursor:not-allowed}
.table-wrap{overflow:auto}table{border-collapse:collapse;width:100%;min-width:48rem}th,td{text-align:left;padding:.6rem;border-bottom:1px solid var(--color-border)}
.status{display:inline-block;border-radius:1rem;background:#e7ecf5;padding:.2rem .6rem}.status[data-status="Succeeded"]{background:#dcfce7;color:#166534}.status[data-status="Failed"]{background:#fee2e2;color:#991b1b}
.actions{display:flex;gap:.5rem;align-items:center}.upload{font-size:.8rem;cursor:pointer;color:var(--color-brand)}.upload input{max-width:11rem;font-size:.75rem}
.settings{margin:.5rem 0 1rem}.retention label{max-width:12rem}.schedule-list{padding:0;list-style:none}.schedule-list li{display:flex;justify-content:space-between;gap:.75rem;align-items:center;padding:.65rem 0;border-bottom:1px solid var(--color-border)}
.dashboard{display:grid;grid-template-columns:repeat(auto-fit,minmax(9rem,1fr));gap:.75rem;margin:0}.dashboard div{border:1px solid var(--color-border);border-radius:.5rem;padding:.7rem}.dashboard dt{font-size:.85rem}.dashboard dd{font-size:1.5rem;font-weight:700;margin:.25rem 0 0}
</style>
