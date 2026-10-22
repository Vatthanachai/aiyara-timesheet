<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, useLocaleText, type Locale } from '../../../shared/i18n'
import { applyTheme, readSessionTheme } from '../../../shared/theme'
import { formatDateTime as formatInstant, formatMonth, readFormats } from '../../../shared/date-format'
import {
  addDays, formatPeriod, monthPeriod, previousPeriod, rangePeriod, weekPeriod, yearPeriod, zonedDate,
  zonedMidnight, zonedToday, type CalendarPeriod, type ReportKind
} from './utils/reportPeriods'

type RunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'
type Definition = { id: string; kind: ReportKind; format: 'Pdf' | 'Xlsx'; name: string }
type Run = {
  id: string; definitionId: string; subjectUserId: string | null; subjectGroupName?: string | null; status: RunStatus
  statusReason: 'SOURCE_MONTH_NOT_LOCKED' | 'GENERATION_FAILED' | null; blockedMonth: string | null
  periodStartUtc: string; periodEndUtc: string; createdAtUtc: string; completedAtUtc: string | null
  signedVersion: number | null; kind: ReportKind; format: 'Pdf' | 'Xlsx'; name: string
}
type RunPage = { totalCount: number; page: number; pageSize: number; items: Run[] }
type Schedule = {
  id: string; reportDefinitionId: string; timeZoneId: string; localTime: string
  nextFireAtUtc: string | null; lastFireAtUtc: string | null; kind: ReportKind; name: string
  subjectGroupId?: string | null; subjectGroupName?: string | null
}
type Dashboard = { total: number; ready: number; queued: number; blocked: number; failed: number }
type Member = { id: string; firstName: string; lastName: string; email: string; role: string; status: string; groupId?: string | null; groupName?: string | null }

const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const token = ref('')
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
const isAdmin = ref(false)
const myName = ref('')
const timeZoneId = ref('Asia/Bangkok')
const loaded = ref(false)
const definitions = ref<Definition[]>([])
const members = ref<Member[]>([])
const history = ref<RunPage>({ totalCount: 0, page: 1, pageSize: 10, items: [] })
const schedules = ref<Schedule[]>([])
const dashboard = ref<Dashboard>({ total: 0, ready: 0, queued: 0, blocked: 0, failed: 0 })
const retentionYears = ref(7)
const error = ref('')
const notice = ref('')
const busy = ref(false)
let pollTimer: ReturnType<typeof setTimeout> | undefined

const copy = useLocaleText(locale, { th: {
  title: 'รายงานและเอกสาร', subtitle: 'ขอรายงานเวลาทำงาน ติดตามสถานะ และดาวน์โหลดไฟล์',
  signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell', loading: 'กำลังโหลด…',
  ready: 'พร้อมดาวน์โหลด', inProgress: 'กำลังสร้าง', attention: 'ต้องดำเนินการ', total: 'รายงานทั้งหมด',
  request: 'ขอรายงาน', report: 'รายงาน', period: 'ช่วงเวลา', week: 'สัปดาห์ที่มีวันที่', month: 'เดือน', year: 'ปี',
  from: 'ตั้งแต่วันที่', to: 'ถึงวันที่', subject: 'รายงานของ', wholeOrganization: 'ทั้งองค์กร', groups: 'กลุ่ม', people: 'สมาชิก',
  periodPreview: 'ช่วงเวลา: {period} ({zone})', generate: 'สร้างรายงาน', generating: 'กำลังส่งคำขอ…',
  generated: 'ส่งคำขอสร้างรายงานแล้ว ระบบจะอัปเดตสถานะให้อัตโนมัติ',
  noDefinitions: 'ยังไม่มีรูปแบบรายงาน', noDefinitionsAdmin: 'เพิ่มรูปแบบรายงานในส่วนตั้งค่าด้านล่างก่อน',
  noDefinitionsEmployee: 'กรุณาติดต่อผู้ดูแลองค์กรให้เพิ่มรูปแบบรายงาน',
  history: 'ประวัติรายงาน', status: 'สถานะ', allStatuses: 'ทุกสถานะ', allReports: 'ทุกรายงาน', person: 'บุคคล',
  memberGroup: 'กลุ่ม', allGroups: 'ทุกกลุ่ม', noGroup: 'ไม่มีกลุ่ม',
  requested: 'วันที่ขอ', completed: 'เสร็จเมื่อ', actions: 'การดำเนินการ', noRuns: 'ยังไม่มีรายงานตามเงื่อนไขนี้',
  Queued: 'รอสร้าง', Running: 'กำลังสร้าง', Succeeded: 'พร้อมดาวน์โหลด', Failed: 'สร้างไม่สำเร็จ',
  blockedReason: 'รอให้ล็อกเดือน {month} ก่อน', failedReason: 'ลองขอใหม่อีกครั้ง หากยังไม่สำเร็จให้ติดต่อผู้ดูแล',
  formerMember: 'สมาชิกเดิม', download: 'ดาวน์โหลด', regenerate: 'สร้างใหม่', regenerated: 'กำลังสร้างรายงานใหม่ จะแสดงเป็นรายการใหม่ในประวัติ', runInProgress: 'รายงานนี้ยังสร้างไม่เสร็จ', signedBadge: 'เซ็นแล้ว v{n}', uploadSigned: 'อัปโหลด PDF ที่เซ็นแล้ว',
  uploaded: 'อัปโหลดไฟล์ที่เซ็นแล้ว', previous: 'ก่อนหน้า', next: 'ถัดไป', pagination: 'แบ่งหน้า',
  autoRefresh: 'อัปเดตสถานะอัตโนมัติขณะมีรายงานที่กำลังสร้าง',
  settings: 'ตั้งค่ารายงาน', formats: 'รูปแบบรายงาน', name: 'ชื่อ', kind: 'ประเภท', format: 'ไฟล์',
  addFormat: 'เพิ่มรูปแบบ', save: 'บันทึก', disable: 'ปิดใช้งาน', saved: 'บันทึกแล้ว', disabled: 'ปิดใช้งานแล้ว',
  Weekly: 'รายสัปดาห์', Monthly: 'รายเดือน', Annual: 'รายปี', Performance: 'ประสิทธิภาพ', Pdf: 'PDF', Xlsx: 'Excel',
  schedules: 'สร้างรายงานอัตโนมัติ', timezone: 'เขตเวลา', localTime: 'เวลา', addSchedule: 'เพิ่มกำหนดการ',
  monthlyTime: 'รายงานรายเดือนสร้างเวลา 00:15 ของวันที่ 1 ทุกเดือน', nextRun: 'รอบถัดไป', lastRun: 'รอบล่าสุด',
  never: 'ยังไม่เคย', noSchedules: 'ยังไม่มีการสร้างรายงานอัตโนมัติ',
  scheduleExists: 'มีกำหนดการของรายงานนี้สำหรับกลุ่มหรือทั้งองค์กรนี้อยู่แล้ว', groupNotFound: 'ไม่พบกลุ่มนี้ อาจถูกลบไปแล้ว กรุณาโหลดหน้าใหม่',
  groupScheduleHint: 'รายงานกลุ่มอัตโนมัติจะใช้สมาชิกของกลุ่ม ณ เวลาที่สร้างรายงาน',
  retention: 'ระยะเวลาเก็บไฟล์', retentionYears: 'จำนวนปี', retentionHint: 'ไฟล์ที่เก่ากว่านี้จะถูกลบอัตโนมัติ',
  documents: 'เอกสาร', countReady: 'พร้อมดาวน์โหลด {n} รายการ', countQueued: 'กำลังสร้าง {n} รายการ', countAttention: 'ต้องดำเนินการ {n} รายการ',
  noReportsYet: 'ยังไม่มีรายงาน', countSeparator: ' ', countEnd: ''
}, en: {
  title: 'Reports and documents', subtitle: 'Request timesheet reports, follow their progress, and download the files.',
  signIn: 'Please sign in through Aiyara Shell', loading: 'Loading…',
  ready: 'Ready to download', inProgress: 'In progress', attention: 'Needs attention', total: 'All reports',
  request: 'Request a report', report: 'Report', period: 'Period', week: 'Week containing', month: 'Month', year: 'Year',
  from: 'From', to: 'To', subject: 'Report on', wholeOrganization: 'Whole organization', groups: 'Groups', people: 'Members',
  periodPreview: 'Period: {period} ({zone})', generate: 'Generate report', generating: 'Sending request…',
  generated: 'Report requested. Its status updates automatically.',
  noDefinitions: 'No report formats yet', noDefinitionsAdmin: 'Add a report format in the settings below first.',
  noDefinitionsEmployee: 'Ask your organization administrator to add a report format.',
  history: 'Report history', status: 'Status', allStatuses: 'All statuses', allReports: 'All reports', person: 'Person',
  memberGroup: 'Group', allGroups: 'All groups', noGroup: 'No group',
  requested: 'Requested', completed: 'Completed', actions: 'Actions', noRuns: 'No reports match these filters.',
  Queued: 'Queued', Running: 'Generating', Succeeded: 'Ready', Failed: 'Failed',
  blockedReason: 'Waiting for {month} to be locked', failedReason: 'Request it again; contact an administrator if it keeps failing.',
  formerMember: 'Former member', download: 'Download', regenerate: 'Regenerate', regenerated: 'Generating the report again; it appears as a new row in the history.', runInProgress: 'This report is still being generated.', signedBadge: 'Signed v{n}', uploadSigned: 'Upload signed PDF',
  uploaded: 'Signed PDF uploaded', previous: 'Previous', next: 'Next', pagination: 'Pagination',
  autoRefresh: 'Statuses refresh automatically while reports are being generated.',
  settings: 'Report settings', formats: 'Report formats', name: 'Name', kind: 'Type', format: 'File',
  addFormat: 'Add format', save: 'Save', disable: 'Disable', saved: 'Saved', disabled: 'Disabled',
  Weekly: 'Weekly', Monthly: 'Monthly', Annual: 'Annual', Performance: 'Performance', Pdf: 'PDF', Xlsx: 'Excel',
  schedules: 'Automatic reports', timezone: 'Time zone', localTime: 'Time', addSchedule: 'Add schedule',
  monthlyTime: 'Monthly reports run at 00:15 on the 1st of each month.', nextRun: 'Next run', lastRun: 'Last run',
  never: 'Never', noSchedules: 'No automatic reports yet.',
  scheduleExists: 'This report is already scheduled for this group or the whole organization.', groupNotFound: 'This group no longer exists. Reload the page.',
  groupScheduleHint: 'Automatic group reports cover whoever is in the group when each report is made.',
  retention: 'File retention', retentionYears: 'Years', retentionHint: 'Files older than this are deleted automatically.',
  documents: 'Documents', countReady: '{n} ready to download', countQueued: '{n} being generated', countAttention: '{n} need attention',
  noReportsYet: 'No reports yet.', countSeparator: ', ', countEnd: '.'
} })
const text = (key: string) => (copy.value as Record<string, string>)[key] ?? key
const dateLocale = computed(() => locale.value === 'th' ? 'th-TH' : 'en')
const formats = ref(readFormats(null))
// File names keep one fixed, readable date style whatever the organization chose.
const FILE_NAME_FORMATS = readFormats({ dateFormat: 'd MMM yyyy' })

// ---- Request -------------------------------------------------------------------------------
const selectedDefinitionId = ref('')
const subjectUserId = ref('')
const weekDay = ref('')
const monthYear = ref(0)
const monthNumber = ref(1)
const yearValue = ref(0)
const rangeFrom = ref('')
const rangeTo = ref('')
const selectedDefinition = computed(() => definitions.value.find(x => x.id === selectedDefinitionId.value))
const yearOptions = computed(() => {
  const current = Number(zonedToday(timeZoneId.value).slice(0, 4))
  return Array.from({ length: 6 }, (_, index) => current - index)
})
const monthOptions = computed(() => Array.from({ length: 12 }, (_, index) => ({
  value: index + 1,
  label: new Intl.DateTimeFormat(dateLocale.value, { month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(2026, index, 1)))
})))
const activeMembers = computed(() => members.value.filter(x => x.status === 'Active'))
const requestPeriod = computed<CalendarPeriod | null>(() => {
  switch (selectedDefinition.value?.kind) {
    case 'Weekly': return weekDay.value ? weekPeriod(weekDay.value) : null
    case 'Monthly': return monthYear.value ? monthPeriod(monthYear.value, monthNumber.value) : null
    case 'Annual': return yearValue.value ? yearPeriod(yearValue.value) : null
    case 'Performance': return rangeFrom.value && rangeTo.value && rangeFrom.value <= rangeTo.value
      ? rangePeriod(rangeFrom.value, rangeTo.value) : null
    default: return null
  }
})

/** Starts each period control on the most recent complete period. */
function resetPeriod() {
  const today = zonedToday(timeZoneId.value)
  const lastMonth = previousPeriod('Monthly', today)
  const [year, month] = lastMonth.start.split('-').map(Number)
  weekDay.value = previousPeriod('Weekly', today).start
  monthYear.value = year
  monthNumber.value = month
  yearValue.value = Number(previousPeriod('Annual', today).start.slice(0, 4))
  rangeFrom.value = lastMonth.start
  rangeTo.value = addDays(lastMonth.end, -1)
}
watch(selectedDefinitionId, () => resetPeriod())

/** A member, a member group (sent as its name and current members), or the whole organization. */
const GROUP_PREFIX = 'group:'
function requestSubject() {
  if (!isAdmin.value || !subjectUserId.value) return { subjectUserId: null }
  if (!subjectUserId.value.startsWith(GROUP_PREFIX)) return { subjectUserId: subjectUserId.value }
  const groupId = subjectUserId.value.slice(GROUP_PREFIX.length)
  return { subjectUserId: null, groupName: memberGroups.value.find(x => x.id === groupId)?.name,
    groupMemberIds: members.value.filter(x => x.groupId === groupId).map(x => x.id) }
}
async function requestReport() {
  if (!selectedDefinition.value || !requestPeriod.value) return
  busy.value = true
  error.value = ''
  try {
    await api('/runs', { method: 'POST', body: {
      definitionId: selectedDefinition.value.id,
      periodStartUtc: zonedMidnight(requestPeriod.value.start, timeZoneId.value).toISOString(),
      periodEndUtc: zonedMidnight(requestPeriod.value.end, timeZoneId.value).toISOString(),
      ...requestSubject()
    } })
    notice.value = copy.value.generated
    filters.page = 1
    await Promise.all([loadHistory(), loadDashboard()])
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { busy.value = false }
}

// ---- History --------------------------------------------------------------------------------
/** group: '' is every report, NO_GROUP is reports about members in no group, else a group ID. */
const NO_GROUP = '-'
const filters = reactive({ status: '', definitionId: '', group: '', page: 1 })
/** The groups directory members are in, by name. */
const memberGroups = computed(() => [...new Map(members.value.filter(x => x.groupId)
  .map(x => [x.groupId!, x.groupName ?? ''])).entries()]
  .map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name)))
const pageCount = computed(() => Math.max(1, Math.ceil(history.value.totalCount / history.value.pageSize)))
const memberNames = computed(() => new Map(members.value.map(member => [member.id,
  [member.firstName, member.lastName].filter(Boolean).join(' ') || member.email])))

function personLabel(run: Run) {
  if (run.subjectGroupName) return `${copy.value.memberGroup}: ${run.subjectGroupName}`
  if (!run.subjectUserId) return copy.value.wholeOrganization
  return memberNames.value.get(run.subjectUserId) ?? copy.value.formerMember
}
function runPeriod(run: Run): CalendarPeriod {
  return { start: zonedDate(run.periodStartUtc, timeZoneId.value), end: zonedDate(run.periodEndUtc, timeZoneId.value) }
}
function statusDetail(run: Run) {
  if (run.statusReason === 'SOURCE_MONTH_NOT_LOCKED' && run.blockedMonth)
    return copy.value.blockedReason.replace('{month}', formatMonth(run.blockedMonth, formats.value, locale.value))
  if (run.statusReason === 'GENERATION_FAILED') return copy.value.failedReason
  return ''
}
/** "1 ready to download, 1 being generated, 2 need attention." with the attention count marked. */
const statusCounts = computed(() => {
  const attention = dashboard.value.blocked + dashboard.value.failed
  return [
    { key: 'ready', count: dashboard.value.ready, text: copy.value.countReady },
    { key: 'queued', count: dashboard.value.queued, text: copy.value.countQueued },
    { key: 'attention', count: attention, text: copy.value.countAttention }
  ].filter(item => item.count > 0).map(item => ({ ...item, text: item.text.replace('{n}', String(item.count)) }))
})
const formatDateTime = (value: string | null) => value
  ? formatInstant(value, timeZoneId.value, formats.value, locale.value) : '—'

async function loadHistory() {
  // A group filter becomes the group's current members; whole-organization reports have no member.
  const subjectUserId = filters.group
    ? members.value.filter(x => (x.groupId ?? NO_GROUP) === filters.group).map(x => x.id) : undefined
  history.value = subjectUserId?.length === 0
    ? { totalCount: 0, page: 1, pageSize: 10, items: [] }
    : await api<RunPage>('/runs', { query: {
      status: filters.status || undefined, definitionId: filters.definitionId || undefined,
      subjectUserId, page: filters.page, pageSize: 10
    } })
  schedulePoll()
}
async function loadDashboard() { dashboard.value = await api<Dashboard>('/dashboard') }

/** Re-checks progress every 10 s while any visible report is still queued or generating. */
function schedulePoll() {
  if (pollTimer) clearTimeout(pollTimer)
  pollTimer = undefined
  if (!history.value.items.some(run => run.status === 'Queued' || run.status === 'Running')) return
  pollTimer = setTimeout(async () => {
    try { await Promise.all([loadHistory(), loadDashboard()]) }
    catch { schedulePoll() }
  }, 10_000)
}
onBeforeUnmount(() => { if (pollTimer) clearTimeout(pollTimer) })

async function applyFilters(page = 1) {
  filters.page = page
  try { await loadHistory() } catch (cause) { error.value = safeError(cause, locale.value) }
}

async function download(run: Run) {
  try {
    const response = await reportFetch(`/runs/${run.id}/download`)
    const objectUrl = URL.createObjectURL(await response.blob())
    const anchor = document.createElement('a')
    const label = `${run.name} ${formatPeriod(runPeriod(run), FILE_NAME_FORMATS, 'en')}`.replace(/[\\/:*?"<>|]+/g, '-')
    anchor.href = objectUrl
    anchor.download = `${label}.${run.format === 'Pdf' ? 'pdf' : 'xlsx'}`
    anchor.click()
    URL.revokeObjectURL(objectUrl)
  } catch (cause) { error.value = safeError(cause, locale.value) }
}

/** Queues a finished report again as a new run with the same report, period, and subject. */
const regeneratingId = ref('')
async function regenerate(run: Run) {
  regeneratingId.value = run.id
  error.value = ''
  notice.value = ''
  try {
    await api(`/runs/${run.id}/regenerate`, { method: 'POST' })
    notice.value = copy.value.regenerated
    filters.page = 1
    await Promise.all([loadHistory(), loadDashboard()])
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'RUN_IN_PROGRESS' ? copy.value.runInProgress : safeError(cause, locale.value)
  } finally { regeneratingId.value = '' }
}

async function uploadSigned(run: Run, event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  try {
    const form = new FormData()
    form.append('file', file)
    const response = await reportFetch(`/runs/${run.id}/signed-document`, { method: 'POST', body: form })
    unwrapResponse(await response.json() as ApiEnvelope<unknown>)
    notice.value = copy.value.uploaded
    await loadHistory()
  } catch (cause) { error.value = safeError(cause, locale.value) }
  input.value = ''
}

// ---- Settings (Tenant Admin) ---------------------------------------------------------------
const definitionForm = reactive<{ kind: ReportKind; format: 'Pdf' | 'Xlsx'; name: string }>({ kind: 'Monthly', format: 'Pdf', name: '' })
const definitionNames = reactive<Record<string, string>>({})
const scheduleForm = reactive({ reportDefinitionId: '', timeZoneId: '', localTime: '00:15', groupId: '' })
const schedulableDefinitions = computed(() => definitions.value.filter(x => x.kind !== 'Performance'))
const scheduleKind = computed(() => definitions.value.find(x => x.id === scheduleForm.reportDefinitionId)?.kind)
const timeZones = computed(() => [...new Set([timeZoneId.value, 'Asia/Bangkok', 'Asia/Ho_Chi_Minh', 'Asia/Jakarta',
  'Asia/Singapore', 'Asia/Kuala_Lumpur', 'Asia/Manila', 'Asia/Tokyo', 'Asia/Kolkata', 'Europe/London', 'UTC',
  'America/New_York', 'America/Los_Angeles'])])

async function settingsAction(action: () => Promise<unknown>, message: string) {
  error.value = ''
  try { await action(); notice.value = message; await loadDefinitionsAndSettings() }
  catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'SCHEDULE_EXISTS' ? copy.value.scheduleExists
      : code === 'GROUP_NOT_FOUND' ? copy.value.groupNotFound : safeError(cause, locale.value)
  }
}
const addDefinition = () => settingsAction(async () => {
  await api('/definitions', { method: 'POST', body: { ...definitionForm, name: definitionForm.name.trim() } })
  definitionForm.name = ''
}, copy.value.saved)
const renameDefinition = (definition: Definition) => settingsAction(() => api(`/definitions/${definition.id}`, {
  method: 'PUT', body: { kind: definition.kind, format: definition.format, name: (definitionNames[definition.id] ?? definition.name).trim() }
}), copy.value.saved)
const disableDefinition = (definition: Definition) =>
  settingsAction(() => api(`/definitions/${definition.id}`, { method: 'DELETE' }), copy.value.disabled)
const addSchedule = () => settingsAction(() => api('/schedules', { method: 'POST', body: {
  reportDefinitionId: scheduleForm.reportDefinitionId, timeZoneId: scheduleForm.timeZoneId || timeZoneId.value,
  localTime: `${scheduleKind.value === 'Monthly' ? '00:15' : scheduleForm.localTime}:00`,
  groupId: scheduleForm.groupId || null
} }), copy.value.saved)
const disableSchedule = (schedule: Schedule) =>
  settingsAction(() => api(`/schedules/${schedule.id}`, { method: 'DELETE' }), copy.value.disabled)
const saveRetention = () => settingsAction(() =>
  api('/retention', { method: 'PUT', body: { years: retentionYears.value } }), copy.value.saved)

// ---- Loading --------------------------------------------------------------------------------
async function api<T>(path: string, options: { method?: string; body?: unknown; query?: Record<string, unknown> } = {}) {
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1/reports${path}`, {
      ...options, headers: { Authorization: `Bearer ${accessToken}` }
    }), token, config.public.shellOrigin))
}

async function reportFetch(path: string, options: RequestInit = {}) {
  return withRemoteRefresh(async accessToken => {
    const response = await fetch(`${gateway.value}/api/v1/reports${path}`, {
      ...options, headers: { ...options.headers, Authorization: `Bearer ${accessToken}` }
    })
    if (!response.ok) throw { status: response.status, data: await response.json().catch(() => undefined) }
    return response
  }, token, config.public.shellOrigin)
}

/** Report renders a member's month, as the page shows it, into a timesheet PDF. */
async function exportTimesheetPdf(body: unknown) {
  const response = await reportFetch('/exports/timesheet-pdf', { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  return response.blob()
}

async function timesheetGet<T>(path: string, query: Record<string, unknown>) {
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1/timesheets${path}`, {
      query, headers: { Authorization: `Bearer ${accessToken}` }
    }), token, config.public.shellOrigin))
}

/** A member's own display name for the My timesheet heading and export; empty falls back to "Me". */
async function loadMyName() {
  try {
    const profile = unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<{ firstName: string | null; lastName: string | null; email: string }>>(`${gateway.value}/api/v1/identity/profile/me`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      }), token, config.public.shellOrigin))
    myName.value = [profile.firstName, profile.lastName].filter(part => part?.trim()).join(' ') || profile.email
  } catch { myName.value = '' }
}
async function loadMembers() {
  try {
    members.value = unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<Member[]>>(`${gateway.value}/api/v1/identity/members`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      }), token, config.public.shellOrigin))
  } catch { members.value = [] } // Names are a convenience; reports still work without them.
}

async function loadDefinitionsAndSettings() {
  definitions.value = await api<Definition[]>('/definitions')
  for (const definition of definitions.value) definitionNames[definition.id] = definition.name
  if (!definitions.value.some(x => x.id === selectedDefinitionId.value))
    selectedDefinitionId.value = definitions.value[0]?.id ?? ''
  if (!isAdmin.value) return
  const [scheduleList, policy] = await Promise.all([
    api<Schedule[]>('/schedules'), api<{ years: number }>('/retention')
  ])
  schedules.value = scheduleList
  retentionYears.value = policy.years
  if (!schedulableDefinitions.value.some(x => x.id === scheduleForm.reportDefinitionId))
    scheduleForm.reportDefinitionId = schedulableDefinitions.value[0]?.id ?? ''
}

async function load() {
  try {
    const context = await api<{ isAdmin: boolean; timeZoneId: string }>('/context')
    isAdmin.value = context.isAdmin
    timeZoneId.value = context.timeZoneId || 'Asia/Bangkok'
    scheduleForm.timeZoneId ||= timeZoneId.value
    await Promise.all([loadDefinitionsAndSettings(), loadHistory(), loadDashboard(),
      isAdmin.value ? loadMembers() : loadMyName()])
    resetPeriod()
    error.value = ''
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { loaded.value = true }
}

onMounted(() => {
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.source !== window.parent || event.origin !== config.public.shellOrigin ||
      event.data?.type !== 'aiyara.session') return
    const firstSession = !token.value
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = readSessionLocale(event.data)
    applyTheme(readSessionTheme(event.data))
    formats.value = readFormats(event.data)
    if (token.value && firstSession) load()
  })
  // The Shell's first session message can arrive before hydration; ask for it once listening.
  if (window.parent !== window) window.parent.postMessage({ type: 'aiyara.ready' }, config.public.shellOrigin)
})
</script>

<template>
  <main class="reports">
    <header class="page-heading">
      <h1>{{ copy.title }}</h1>
      <p>{{ copy.subtitle }}</p>
    </header>
    <p v-if="!token" class="empty">{{ copy.signIn }}</p>
    <p v-else-if="!loaded" role="status">{{ copy.loading }}</p>
    <template v-else>
      <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
      <p v-if="notice" class="feedback feedback-ok" role="status">{{ notice }}</p>

      <MemberTimesheets v-if="isAdmin" :members="members" :locale="locale" :formats="formats" :time-zone-id="timeZoneId" :load="timesheetGet" :export-pdf="exportTimesheetPdf" />
      <MemberTimesheets v-else-if="loaded" :members="[]" :locale="locale" :formats="formats" :time-zone-id="timeZoneId" :load="timesheetGet" :export-pdf="exportTimesheetPdf" :self="{ name: myName }" />

      <section class="card documents" aria-labelledby="documents-heading">
        <div class="card-heading">
          <h2 id="documents-heading">{{ copy.documents }}</h2>
          <p class="doc-status">
            <template v-if="statusCounts.length">
              <span v-for="(item, index) in statusCounts" :key="item.key" :class="`count-${item.key}`">{{ item.text }}{{ index < statusCounts.length - 1 ? copy.countSeparator : copy.countEnd }}</span>
            </template>
            <span v-else>{{ copy.noReportsYet }}</span>
          </p>
        </div>

        <section class="request" aria-labelledby="request-heading">
          <h3 id="request-heading">{{ copy.request }}</h3>
          <div v-if="!definitions.length" class="empty">
            <strong>{{ copy.noDefinitions }}</strong>
            <p>{{ isAdmin ? copy.noDefinitionsAdmin : copy.noDefinitionsEmployee }}</p>
          </div>
          <form v-else class="request-form" @submit.prevent="requestReport">
            <label class="request-report">{{ copy.report }}
              <select v-model="selectedDefinitionId" required>
                <option v-for="item in definitions" :key="item.id" :value="item.id">{{ item.name }} ({{ text(item.format) }})</option>
              </select>
            </label>
            <label v-if="selectedDefinition?.kind === 'Weekly'">{{ copy.week }}
              <input v-model="weekDay" type="date" required />
            </label>
            <template v-else-if="selectedDefinition?.kind === 'Monthly'">
              <label>{{ copy.month }}
                <select v-model.number="monthNumber"><option v-for="item in monthOptions" :key="item.value" :value="item.value">{{ item.label }}</option></select>
              </label>
              <label>{{ copy.year }}
                <select v-model.number="monthYear"><option v-for="item in yearOptions" :key="item" :value="item">{{ item }}</option></select>
              </label>
            </template>
            <label v-else-if="selectedDefinition?.kind === 'Annual'">{{ copy.year }}
              <select v-model.number="yearValue"><option v-for="item in yearOptions" :key="item" :value="item">{{ item }}</option></select>
            </label>
            <template v-else>
              <label>{{ copy.from }}<input v-model="rangeFrom" type="date" required /></label>
              <label>{{ copy.to }}<input v-model="rangeTo" type="date" required :min="rangeFrom" /></label>
            </template>
            <label v-if="isAdmin">{{ copy.subject }}
              <select v-model="subjectUserId" :aria-label="copy.subject">
                <option value="">{{ copy.wholeOrganization }}</option>
                <optgroup v-if="memberGroups.length" :label="copy.groups">
                  <option v-for="group in memberGroups" :key="group.id" :value="`${GROUP_PREFIX}${group.id}`">{{ group.name }}</option>
                </optgroup>
                <optgroup v-if="memberGroups.length" :label="copy.people">
                  <option v-for="member in activeMembers" :key="member.id" :value="member.id">{{ memberNames.get(member.id) }}</option>
                </optgroup>
                <template v-else>
                  <option v-for="member in activeMembers" :key="member.id" :value="member.id">{{ memberNames.get(member.id) }}</option>
                </template>
              </select>
            </label>
            <div class="request-submit">
              <p v-if="requestPeriod" class="preview">
                {{ copy.periodPreview.replace('{period}', formatPeriod(requestPeriod, formats, locale)).replace('{zone}', timeZoneId) }}
              </p>
              <button class="primary" type="submit" :disabled="busy || !requestPeriod">{{ busy ? copy.generating : copy.generate }}</button>
            </div>
          </form>
        </section>

        <section class="history" aria-labelledby="history-heading">
          <div class="history-heading">
            <h3 id="history-heading">{{ copy.history }}</h3>
            <form class="filters" @submit.prevent>
              <label>{{ copy.status }}
                <select v-model="filters.status" @change="applyFilters()">
                  <option value="">{{ copy.allStatuses }}</option>
                  <option v-for="status in ['Queued', 'Running', 'Succeeded', 'Failed']" :key="status" :value="status">{{ text(status) }}</option>
                </select>
              </label>
              <label>{{ copy.report }}
                <select v-model="filters.definitionId" @change="applyFilters()">
                  <option value="">{{ copy.allReports }}</option>
                  <option v-for="item in definitions" :key="item.id" :value="item.id">{{ item.name }}</option>
                </select>
              </label>
              <label v-if="isAdmin && memberGroups.length">{{ copy.memberGroup }}
                <select v-model="filters.group" :aria-label="copy.memberGroup" @change="applyFilters()">
                  <option value="">{{ copy.allGroups }}</option>
                  <option v-for="group in memberGroups" :key="group.id" :value="group.id">{{ group.name }}</option>
                  <option :value="NO_GROUP">{{ copy.noGroup }}</option>
                </select>
              </label>
            </form>
          </div>
          <p v-if="history.items.some(run => run.status === 'Queued' || run.status === 'Running')" class="hint">{{ copy.autoRefresh }}</p>
          <p v-if="!history.items.length" class="empty">{{ copy.noRuns }}</p>
          <div v-else class="table-wrap">
            <!-- Fixed column widths keep every row in proportion: the document leads, the actions get room for two buttons side by side. -->
            <table class="history-table" :class="{ 'with-person': isAdmin }">
              <colgroup><col class="col-document" /><col v-if="isAdmin" class="col-person" /><col class="col-status" /><col class="col-actions" /></colgroup>
              <thead><tr>
                <th scope="col">{{ copy.report }}</th>
                <th v-if="isAdmin" scope="col">{{ copy.person }}</th>
                <th scope="col">{{ copy.status }}</th>
                <th scope="col"><span class="sr-only">{{ copy.actions }}</span></th>
              </tr></thead>
              <tbody>
                <tr v-for="run in history.items" :key="run.id">
                  <td class="document-cell">
                    <div class="document">
                      <span class="file-tag" :data-format="run.format">{{ run.format === 'Pdf' ? 'PDF' : 'XLSX' }}</span>
                      <span class="document-name"><strong>{{ run.name }}</strong><span class="document-period">{{ formatPeriod(runPeriod(run), formats, locale) }}</span><small>{{ text(run.kind) }}</small></span>
                    </div>
                  </td>
                  <td v-if="isAdmin" class="person-cell">{{ personLabel(run) }}</td>
                  <td class="status-cell">
                    <span class="status" :data-status="run.statusReason === 'SOURCE_MONTH_NOT_LOCKED' ? 'Blocked' : run.status">{{ text(run.status) }}</span>
                    <span v-if="run.signedVersion" class="badge">{{ copy.signedBadge.replace('{n}', String(run.signedVersion)) }}</span>
                    <small v-if="statusDetail(run)" class="status-detail">{{ statusDetail(run) }}</small>
                    <small class="run-times">{{ copy.requested }} {{ formatDateTime(run.createdAtUtc) }}</small>
                    <small v-if="run.completedAtUtc" class="run-times">{{ copy.completed }} {{ formatDateTime(run.completedAtUtc) }}</small>
                  </td>
                  <td>
                    <div class="actions">
                      <button v-if="run.status === 'Succeeded'" type="button" class="primary-quiet" @click="download(run)">{{ copy.download }}</button>
                      <button v-if="run.status === 'Succeeded' || run.status === 'Failed'" type="button"
                        :disabled="regeneratingId === run.id" :aria-label="`${copy.regenerate}: ${run.name} ${formatPeriod(runPeriod(run), formats, locale)}`"
                        @click="regenerate(run)">{{ copy.regenerate }}</button>
                      <label v-if="isAdmin && run.status === 'Succeeded' && run.kind === 'Monthly' && run.format === 'Pdf'" class="upload">
                        {{ copy.uploadSigned }}<input type="file" accept="application/pdf,.pdf" @change="uploadSigned(run, $event)" />
                      </label>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <nav v-if="pageCount > 1" class="pagination" :aria-label="copy.pagination">
            <button type="button" :disabled="filters.page <= 1" @click="applyFilters(filters.page - 1)">{{ copy.previous }}</button>
            <span>{{ filters.page }} / {{ pageCount }}</span>
            <button type="button" :disabled="filters.page >= pageCount" @click="applyFilters(filters.page + 1)">{{ copy.next }}</button>
          </nav>
        </section>
      </section>

      <section v-if="isAdmin" class="settings" aria-labelledby="settings-heading">
        <h2 id="settings-heading">{{ copy.settings }}</h2>
        <div class="settings-grid">
          <section class="card setting-panel" aria-labelledby="formats-heading">
            <h3 id="formats-heading">{{ copy.formats }}</h3>
            <ul class="setting-list">
              <li v-for="definition in definitions" :key="definition.id">
                <label class="grow">{{ copy.name }}<input v-model="definitionNames[definition.id]" maxlength="160" required /></label>
                <span class="muted">{{ text(definition.kind) }}, {{ text(definition.format) }}</span>
                <span class="setting-actions">
                  <button type="button" @click="renameDefinition(definition)">{{ copy.save }}</button>
                  <button type="button" class="secondary" @click="disableDefinition(definition)">{{ copy.disable }}</button>
                </span>
              </li>
            </ul>
            <form class="inline-form" @submit.prevent="addDefinition">
              <label>{{ copy.kind }}<select v-model="definitionForm.kind"><option v-for="kind in ['Weekly', 'Monthly', 'Annual', 'Performance']" :key="kind" :value="kind">{{ text(kind) }}</option></select></label>
              <label>{{ copy.format }}<select v-model="definitionForm.format"><option value="Pdf">PDF</option><option value="Xlsx">Excel</option></select></label>
              <label class="grow">{{ copy.name }}<input v-model="definitionForm.name" required maxlength="160" /></label>
              <button type="submit">{{ copy.addFormat }}</button>
            </form>
          </section>

          <section class="card setting-panel" aria-labelledby="schedules-heading">
            <h3 id="schedules-heading">{{ copy.schedules }}</h3>
            <p v-if="!schedules.length" class="muted">{{ copy.noSchedules }}</p>
            <ul v-else class="setting-list">
              <li v-for="schedule in schedules" :key="schedule.id" class="schedule-item">
                <span class="grow schedule-name"><strong>{{ schedule.name }}</strong>
                  <span>{{ schedule.subjectGroupName ? `${copy.memberGroup}: ${schedule.subjectGroupName}` : copy.wholeOrganization }}</span>
                  <small>{{ text(schedule.kind) }}, {{ schedule.timeZoneId }}</small></span>
                <span class="muted">{{ copy.nextRun }}: {{ formatDateTime(schedule.nextFireAtUtc) }}</span>
                <span class="muted">{{ copy.lastRun }}: {{ schedule.lastFireAtUtc ? formatDateTime(schedule.lastFireAtUtc) : copy.never }}</span>
                <button type="button" class="secondary" @click="disableSchedule(schedule)">{{ copy.disable }}</button>
              </li>
            </ul>
            <form v-if="schedulableDefinitions.length" class="inline-form" @submit.prevent="addSchedule">
              <label class="grow">{{ copy.report }}<select v-model="scheduleForm.reportDefinitionId" required><option v-for="item in schedulableDefinitions" :key="item.id" :value="item.id">{{ item.name }} ({{ text(item.kind) }})</option></select></label>
              <label v-if="memberGroups.length" class="grow">{{ copy.subject }}<select v-model="scheduleForm.groupId" :aria-label="`${copy.schedules}: ${copy.subject}`">
                <option value="">{{ copy.wholeOrganization }}</option>
                <option v-for="group in memberGroups" :key="group.id" :value="group.id">{{ copy.memberGroup }}: {{ group.name }}</option>
              </select></label>
              <label class="grow">{{ copy.timezone }}<select v-model="scheduleForm.timeZoneId"><option v-for="zone in timeZones" :key="zone" :value="zone">{{ zone }}</option></select></label>
              <label v-if="scheduleKind !== 'Monthly'">{{ copy.localTime }}<input v-model="scheduleForm.localTime" type="time" required /></label>
              <p v-else class="muted full">{{ copy.monthlyTime }}</p>
              <p v-if="scheduleForm.groupId" class="muted full">{{ copy.groupScheduleHint }}</p>
              <button type="submit">{{ copy.addSchedule }}</button>
            </form>
          </section>

          <section class="card setting-panel" aria-labelledby="retention-heading">
            <h3 id="retention-heading">{{ copy.retention }}</h3>
            <form class="inline-form" @submit.prevent="saveRetention">
              <label>{{ copy.retentionYears }}<input v-model.number="retentionYears" type="number" min="1" max="20" required /></label>
              <button type="submit">{{ copy.save }}</button>
              <p class="muted full">{{ copy.retentionHint }}</p>
            </form>
          </section>
        </div>
      </section>
    </template>
  </main>
</template>

<style>
:root{font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;color:var(--color-ink);background:var(--color-canvas)}
*{box-sizing:border-box}body{margin:0}
:focus-visible{outline:2px solid var(--color-brand);outline-offset:2px}
.reports{max-width:76rem;margin:0 auto;padding:clamp(1rem,3vw,2rem)}
.page-heading h1{font-family:var(--font-display);font-size:clamp(1.7rem,3.5vw,2.4rem);font-weight:600;line-height:1.15;margin:0 0 .35rem}
.page-heading p{color:var(--color-muted);margin:0 0 1.5rem;max-width:40rem}
.card{background:var(--color-surface);border:1px solid var(--color-border);border-radius:.75rem;padding:clamp(1rem,2vw,1.35rem);margin:0 0 1.25rem;min-width:0}
h2,h3{font-family:var(--font-display);font-weight:600}
.card h2,.settings h2{font-size:1.25rem;margin:0}
.card h3{font-size:1.05rem;margin:0 0 .75rem}
.card-heading{display:flex;flex-wrap:wrap;gap:.5rem 1.5rem;align-items:flex-end;justify-content:space-between;margin-bottom:1rem}
form{margin:0}label{display:grid;gap:.3rem;font-size:.85rem;color:var(--color-muted)}
input,select,button{font:inherit;color:var(--color-ink);border:1px solid var(--color-border-strong);border-radius:.5rem;padding:.5rem .65rem;background:var(--color-surface);min-width:0}
button{cursor:pointer;background:var(--color-surface);font-weight:600;font-size:.88rem}button:hover:not(:disabled){background:var(--color-surface-muted)}button:disabled{opacity:.55;cursor:not-allowed}
button.primary{background:var(--color-brand);border-color:var(--color-brand);color:var(--color-on-brand)}button.primary:hover:not(:disabled){background:var(--color-brand-strong)}
button.primary-quiet{background:var(--color-work-track);border-color:var(--color-work-track);color:var(--color-brand-ink)}button.primary-quiet:hover:not(:disabled){background:var(--color-brand-line)}
button.secondary{background:transparent;border-color:transparent;color:var(--color-muted)}
/* Documents: one sentence of counts, the request form, then the history. */
.doc-status{margin:0;color:var(--color-ink)}
.doc-status .count-attention{color:var(--color-danger);font-weight:600}
.request{border-bottom:1px solid var(--color-border);padding-bottom:1.25rem;margin-bottom:1.25rem}
.request-form{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,11rem),1fr));gap:.85rem;align-items:end}
@media (min-width:48rem){.request-report{grid-column:span 2}}
.request-submit{grid-column:1/-1;display:flex;flex-wrap:wrap;gap:.75rem 1.25rem;align-items:center}
.request-submit .preview{margin:0;color:var(--color-ink);font-weight:600}
.history-heading{display:flex;flex-wrap:wrap;gap:.75rem 1.5rem;align-items:flex-end;justify-content:space-between;margin-bottom:.75rem}.history-heading h3{margin:0}
.filters{display:flex;flex-wrap:wrap;gap:.6rem}
.muted,.hint{color:var(--color-muted);font-size:.85rem;margin:0}.hint{margin:0 0 .75rem}
.empty{color:var(--color-muted);padding:1rem;border:1px dashed var(--color-border);border-radius:.6rem;margin:0}.empty p{margin:.35rem 0 0}
.table-wrap{overflow-x:auto;position:relative}table{border-collapse:collapse;width:100%}
th,td{text-align:left;vertical-align:top;padding:.7rem .6rem;border-bottom:1px solid var(--color-border);font-size:.9rem}th{color:var(--color-muted);font-weight:600;font-size:.8rem}
td small{display:block;color:var(--color-muted);font-size:.78rem;margin-top:.15rem}
/* History: each report reads as a document, its file type tagged. */
/* History columns: fixed widths so rows keep the same proportions whatever their text. */
.history-table{table-layout:fixed;min-width:44rem}
.history-table .col-document{width:38%}.history-table.with-person .col-document{width:31%}
.history-table .col-person{width:15%}.history-table .col-actions{width:15.5rem}
.document-name strong{overflow-wrap:anywhere}.document{display:flex;gap:.75rem;align-items:flex-start}
.file-tag{flex:0 0 auto;width:2.6rem;padding:.55rem 0 .45rem;border-radius:.3rem;font-family:var(--font-display);font-size:.7rem;font-weight:600;text-align:center;background:var(--color-danger-soft);color:var(--color-danger)}
.file-tag[data-format="Xlsx"]{background:var(--color-success-soft);color:var(--color-success)}
.document-name{display:grid;gap:.1rem}
.document-period{font-family:var(--font-display);font-variant-numeric:tabular-nums;white-space:nowrap}
.person-cell{overflow-wrap:anywhere}
.status-cell .badge{margin-left:.5rem}
.run-times{font-variant-numeric:tabular-nums}
.status{display:inline-flex;align-items:center;gap:.4rem;font-size:.85rem;font-weight:600;color:var(--color-ink-soft)}
.status::before{content:"";width:.55rem;height:.55rem;border-radius:50%;background:var(--color-faint)}
.status[data-status="Succeeded"]{color:var(--color-success)}.status[data-status="Succeeded"]::before{background:var(--color-success-strong)}
.status[data-status="Running"]::before,.status[data-status="Queued"]::before{background:var(--color-brand)}
.status[data-status="Failed"]{color:var(--color-danger)}.status[data-status="Failed"]::before{background:var(--color-danger)}
.status[data-status="Blocked"]{color:var(--color-warn)}.status[data-status="Blocked"]::before{background:var(--color-warn-strong)}
.actions{display:flex;flex-wrap:wrap;gap:.4rem;align-items:center;justify-content:flex-end}
.actions button,.actions .upload{font-size:.82rem;padding:.4rem .6rem}
.badge{border-radius:.35rem;background:var(--color-success-soft);color:var(--color-success);font-size:.75rem;font-weight:700;padding:.2rem .45rem}
/* The file input is invisible over its label, so the label reads as a button and still opens the picker. */
.upload{position:relative;display:inline-flex;align-items:center;border:1px solid var(--color-border-strong);border-radius:.5rem;padding:.5rem .65rem;color:var(--color-ink);font-size:.88rem;font-weight:600;cursor:pointer}.upload:hover{background:var(--color-surface-muted)}.upload:focus-within{outline:2px solid var(--color-brand);outline-offset:2px}.upload input{position:absolute;inset:0;opacity:0;cursor:pointer;width:100%;padding:0}
.pagination{display:flex;gap:.75rem;align-items:center;justify-content:center;margin-top:1rem}
/* Settings: three separate panels. */
.settings h2{margin:2rem 0 .85rem}
.settings-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,20rem),1fr));gap:1.25rem;align-items:start}
.setting-panel{margin:0}
.inline-form{display:flex;flex-wrap:wrap;gap:.75rem;align-items:end}.grow{flex:1 1 12rem}.full{flex-basis:100%}
.setting-list{list-style:none;margin:0 0 1rem;padding:0;display:grid}
.setting-list li{display:flex;flex-wrap:wrap;gap:.5rem .75rem;align-items:end;padding:.75rem 0;border-bottom:1px solid var(--color-border)}
.setting-list li:first-child{padding-top:0}
.setting-actions{display:flex;gap:.4rem}
.schedule-name{display:grid;gap:.1rem;color:var(--color-ink)}
.schedule-name small{color:var(--color-muted)}
.feedback{border-radius:.6rem;padding:.7rem .9rem;margin:0 0 1rem}.feedback-error{background:var(--color-danger-soft);color:var(--color-danger)}.feedback-ok{background:var(--color-success-soft);color:var(--color-success)}
.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap}
/* On a phone each report stacks into a block instead of scrolling sideways. */
@media (max-width:48rem){
  .history-table{min-width:0}
  .history-table colgroup{display:none}
  .history-table thead{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0)}
  .history-table,.history-table tbody,.history-table tr,.history-table td{display:block;width:100%}
  .history-table tr{border-bottom:1px solid var(--color-border);padding:.75rem 0}
  .history-table td{border:0;padding:.2rem 0}
  .history-table td:not(.document-cell){padding-left:3.35rem}
  .actions{justify-content:flex-start;margin-top:.35rem}
  .status-cell{max-width:none}
}
</style>
