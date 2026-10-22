<script setup lang="ts">
import { safeError } from '../../../../shared/api-response'
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import { useLocaleText, type Locale } from '../../../../shared/i18n'
import { zonedToday } from '../utils/reportPeriods'
import { csvBlob, saveBlob, xlsxBlob, type Cell } from '../utils/spreadsheet'
import { expectedMinutes, standardMinutes, type WorkHours, type WorkSchedule } from '../utils/expectedHours'
import { formatDate as formatCalendarDate, formatMonth, formatTime, type Formats } from '../../../../shared/date-format'

type Member = { id: string; firstName: string; lastName: string; email: string; role: string; status: string; groupId?: string | null; groupName?: string | null }
/** A member's month in the approval flow; older API responses omit it (open). */
type ApprovalStatus = 'open' | 'submitted' | 'approved' | 'returned'
type MemberSummary = { ownerId: string; entryCount: number; totalMinutes: number; leaveDays: number; status?: ApprovalStatus }
type Summary = { year: number; month: number; locked: boolean; members: MemberSummary[] }
type Entry = {
  id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string
  detail: string | null; notes: string | null; projectName: string | null; categoryName: string | null
}
type Detail = {
  ownerId: string; locked: boolean; entries: Entry[]
  status?: ApprovalStatus; returnReason?: string | null
  /** `minutes` is a part-day leave; null or absent is the whole day. */
  leaves: { id: string; date: string; kind: string; notes: string | null; minutes?: number | null }[]
  holidays: { date: string; name: string }[]
  /** Date.getDay() numbers the organization works; older API responses omit it (Monday to Friday). */
  workingDays?: number[]
  workHours?: WorkHours
}
type Catalog = { holidays: { date: string }[]; workingDays?: number[]; workHours?: WorkHours }
/** One leave type's calendar year; remaining is null without a limit. */
type LeaveBalance = { leaveTypeId: string; name: string; annualDays: number | null; usedDays: number; remainingDays: number | null }

const props = defineProps<{
  members: Member[]
  locale: Locale
  timeZoneId: string
  /** GET a Timesheet API path through Gateway with the remote's session. */
  load: <T>(path: string, query: Record<string, unknown>) => Promise<T>
  /** The organization's date and time formats. */
  formats: Formats
  /** Set for a member who is not an admin: shows only their own month, read live. */
  self?: { name: string }
  /** Renders a month as a timesheet PDF through Report, returning the file. */
  exportPdf: (body: unknown) => Promise<Blob>
  /** POST a Timesheet API path through Gateway; set for admins, who approve and return months. */
  post?: (path: string, body?: unknown) => Promise<unknown>
}>()

const copy = useLocaleText(toRef(props, 'locale'), { th: {
  title: 'Timesheet ของสมาชิก', hint: 'ดูเวลาทำงานและวันลาของสมาชิกแต่ละคนรายเดือน รวมเดือนปัจจุบันที่ยังไม่ปิดรอบ',
  month: 'เดือน', member: 'สมาชิก', entries: 'รายการ', hours: 'ชั่วโมง', leaveDays: 'วันลา', view: 'ดู timesheet', viewShort: 'ดู',
  noData: 'ยังไม่มีรายการ', formerMember: 'สมาชิกเดิม', loading: 'กำลังโหลด…', back: 'สมาชิกทั้งหมด',
  monthClosed: 'ปิดรอบแล้ว', monthOpen: 'ยังไม่ปิดรอบ (ข้อมูลอาจเปลี่ยน)', totalHours: 'ชั่วโมงรวม', daysWorked: 'วันที่ทำงาน',
  date: 'วันที่', time: 'เวลา', task: 'งาน', project: 'โครงการ', category: 'หมวดหมู่', duration: 'ชั่วโมง',
  byProject: 'ชั่วโมงตามโครงการ', noProject: 'ไม่ระบุโครงการ', leave: 'วันลา', holidays: 'วันหยุด', weeklyDayOff: 'วันหยุดประจำสัปดาห์', daysOff: 'วันหยุด',
  noEntries: 'ไม่มีรายการเวลาทำงานในเดือนนี้', noLeave: 'ไม่มีวันลา', noHolidays: 'ไม่มีวันหยุดในเดือนนี้', dayTotal: 'รวมวันนี้',
  group: 'กลุ่ม', allGroups: 'ทุกกลุ่ม', noGroup: 'ไม่มีกลุ่ม', byGroup: 'สรุปตามกลุ่ม', members: 'สมาชิก', noMembersInGroup: 'ไม่มีสมาชิกในกลุ่มนี้',
  exportExcel: 'ส่งออก Excel', moreFormats: 'ส่งออกเป็นไฟล์แบบอื่น', asCsv: 'CSV (.csv)', asPdf: 'PDF (.pdf)', preparingPdf: 'กำลังสร้าง PDF…', exportLabel: 'ส่งออก timesheet ของ {name}', start: 'เริ่ม', end: 'สิ้นสุด',
  detail: 'รายละเอียด', notes: 'บันทึก', remark: 'หมายเหตุ', status: 'สถานะ',
  expectedOpen: 'ถึงวันนี้ควรมี {toDate} จาก {total} ชั่วโมงต่อคน', expectedClosed: '{month} ต้องการ {total} ชั่วโมงต่อคน',
  ofHours: 'จาก {expected}', hoursLabel: '{name}: บันทึก {logged} จาก {expected} ชั่วโมง', strip: 'ชั่วโมงรายวันของ {name}',
  myTitle: 'Timesheet ของฉัน', myHint: 'เวลาทำงาน วันลา และวันหยุดของคุณรายเดือน รวมเดือนปัจจุบันที่ยังไม่ปิดรอบ อัปเดตทันทีที่บันทึกเวลา', me: 'ฉัน',
  statusSubmitted: 'รออนุมัติ', statusApproved: 'อนุมัติแล้ว', statusReturned: 'ส่งกลับแล้ว',
  submittedHint: '{name} ส่ง{month}ให้อนุมัติแล้ว', approve: 'อนุมัติ', returnMonth: 'ส่งกลับให้แก้ไข',
  returnReason: 'เหตุผลที่ส่งกลับ', returnHint: 'สมาชิกจะเห็นเหตุผลนี้และได้รับอีเมล', sendReturn: 'ส่งกลับ', cancel: 'ยกเลิก',
  confirmApprove: 'อนุมัติ{month}ของ {name}? เมื่ออนุมัติแล้วจะแก้ไขไม่ได้อีก', approvedNotice: 'อนุมัติแล้ว', returnedNotice: 'ส่งกลับให้สมาชิกแก้ไขแล้ว',
  returnedBecause: 'ส่งกลับเพราะ: {reason}',
  leaveBalance: 'วันลาปี {year}', balanceLeft: 'ใช้ {used} จาก {total} วัน เหลือ {left}', balanceTaken: 'ใช้ {used} วัน', partDay: '{hours} ชม.'
}, en: {
  title: 'Member timesheets', hint: "Review each member's hours and leave by month, including the current open month.",
  month: 'Month', member: 'Member', entries: 'Entries', hours: 'Hours', leaveDays: 'Leave days', view: 'View timesheet', viewShort: 'View',
  noData: 'No entries yet', formerMember: 'Former member', loading: 'Loading…', back: 'All members',
  monthClosed: 'Month closed', monthOpen: 'Month open (may still change)', totalHours: 'Total hours', daysWorked: 'Days worked',
  date: 'Date', time: 'Time', task: 'Task', project: 'Project', category: 'Category', duration: 'Hours',
  byProject: 'Hours by project', noProject: 'No project', leave: 'Leave', holidays: 'Days off', weeklyDayOff: 'Weekly day off', daysOff: 'Days off',
  noEntries: 'No time entries this month.', noLeave: 'No leave.', noHolidays: 'No days off this month.', dayTotal: 'Day total',
  group: 'Group', allGroups: 'All groups', noGroup: 'No group', byGroup: 'Totals by group', members: 'Members', noMembersInGroup: 'No members in this group.',
  exportExcel: 'Export Excel', moreFormats: 'Export as another file type', asCsv: 'CSV (.csv)', asPdf: 'PDF (.pdf)', preparingPdf: 'Preparing the PDF…', exportLabel: 'Export the timesheet of {name}', start: 'Start', end: 'End',
  detail: 'Detail', notes: 'Notes', remark: 'Remark', status: 'Status',
  expectedOpen: 'Expected so far: {toDate} of {total} hours per member.', expectedClosed: '{month} expects {total} hours per member.',
  ofHours: 'of {expected}', hoursLabel: '{name}: {logged} of {expected} hours logged', strip: 'Hours per day for {name}',
  myTitle: 'My timesheet', myHint: 'Your hours, leave, and days off by month, including the current open month. It updates as soon as you record time.', me: 'Me',
  statusSubmitted: 'Awaiting approval', statusApproved: 'Approved', statusReturned: 'Returned',
  submittedHint: '{name} submitted {month} for approval.', approve: 'Approve', returnMonth: 'Return for changes',
  returnReason: 'Why are you returning it?', returnHint: 'The member sees this reason and gets an email.', sendReturn: 'Return', cancel: 'Cancel',
  confirmApprove: "Approve {name}'s {month}? It can no longer be changed once approved.", approvedNotice: 'Approved', returnedNotice: 'Returned to the member for changes',
  returnedBecause: 'Returned: {reason}',
  leaveBalance: 'Leave in {year}', balanceLeft: '{used} of {total} days taken, {left} left', balanceTaken: '{used} days taken', partDay: '{hours} h'
} })

const month = ref(zonedToday(props.timeZoneId).slice(0, 7))
const summary = ref<Summary | null>(null)
const selectedId = ref('')
/** '' is every member; NO_GROUP is directory members in no group. */
const NO_GROUP = '-'
const groupFilter = ref('')
const groupChoices = computed(() => [{ value: '', label: copy.value.allGroups },
  ...groups.value.map(group => ({ value: group.id, label: group.name })), { value: NO_GROUP, label: copy.value.noGroup }])
const detail = ref<Detail | null>(null)
const busy = ref(false)
const error = ref('')
const notice = ref('')
const balances = ref<LeaveBalance[]>([])
const days = (value: number) => String(Math.round(value * 100) / 100)
const balanceLine = (balance: LeaveBalance) => balance.annualDays == null
  ? fill(copy.value.balanceTaken, { used: days(balance.usedDays) })
  : fill(copy.value.balanceLeft, { used: days(balance.usedDays), total: days(balance.annualDays), left: days(balance.remainingDays ?? 0) })
/** The open month's leave balances for its year; a failure leaves them out rather than the month. */
async function loadBalances(id: string | null) {
  try {
    balances.value = (await props.load<{ balances: LeaveBalance[] }>(id ? `/members/${id}/leave-balance` : '/me/leave-balance',
      { year: period.value.year })).balances
  } catch { balances.value = [] }
}
const leaveKind = (leave: Detail['leaves'][number]) => leave.minutes
  ? `${leave.kind} (${fill(copy.value.partDay, { hours: String(Math.round(leave.minutes / 60 * 100) / 100) })})` : leave.kind
/** The approval status as a dot colour: approved reads as done, submitted as waiting, returned as blocked. */
const approvalDot = { open: '', submitted: 'Queued', approved: 'Succeeded', returned: 'Blocked' } as const
const approvalLabel = (status: ApprovalStatus | undefined) => status === 'submitted' ? copy.value.statusSubmitted
  : status === 'approved' ? copy.value.statusApproved : status === 'returned' ? copy.value.statusReturned : ''
const period = computed(() => ({ year: Number(month.value.slice(0, 4)), month: Number(month.value.slice(5, 7)) }))
/** The organization's work week, hours, and holidays; without them the page shows figures only. */
const catalog = ref<Catalog | null>(null)
const toSchedule = (source: { workingDays?: number[]; holidays: { date: string }[]; workHours?: WorkHours }): WorkSchedule => ({
  workingDays: source.workingDays ?? [1, 2, 3, 4, 5],
  holidays: new Set(source.holidays.map(holiday => holiday.date)),
  standardMinutes: standardMinutes(source.workHours ?? catalog.value?.workHours)
})
const schedule = computed(() => catalog.value ? toSchedule(catalog.value) : null)
const today = computed(() => zonedToday(props.timeZoneId))
const monthExpected = computed(() => schedule.value ? expectedMinutes(month.value, schedule.value) : 0)
/** Expected by today in the current month; null for any other month. */
const expectedToDate = computed(() => schedule.value && today.value.startsWith(month.value)
  ? expectedMinutes(month.value, schedule.value, today.value) : null)
/** A member's expectation leaves out their leave days. */
const memberExpected = (leaveDays: number) => Math.max(monthExpected.value - leaveDays * (schedule.value?.standardMinutes ?? 0), 0)
const memberToDate = (leaveDays: number) => expectedToDate.value == null ? null
  : Math.max(expectedToDate.value - leaveDays * (schedule.value?.standardMinutes ?? 0), 0)
const fill = (template: string, values: Record<string, string>) => template.replace(/\{(\w+)\}/g, (_, key: string) => values[key] ?? '')
/** The open month's schedule: its own holidays and working days as the API sent them. */
const detailSchedule = computed(() => detail.value ? toSchedule(detail.value) : null)
const leaveDates = computed(() => new Set(detail.value?.leaves.map(leave => leave.date)))

const memberName = (member: Member | undefined) =>
  member ? [member.firstName, member.lastName].filter(part => part?.trim()).join(' ') || member.email : copy.value.formerMember
const memberById = (id: string) => props.members.find(x => x.id === id)
/** The name on the open month: the member's own in self mode. */
const selectedName = computed(() => props.self ? props.self.name || copy.value.me : memberName(memberById(selectedId.value)))
/** Every directory member, plus anyone with data who has since left, busiest first. */
const rows = computed(() => {
  const totals = new Map((summary.value?.members ?? []).map(x => [x.ownerId, x]))
  const ids = [...new Set([...props.members.map(x => x.id), ...totals.keys()])]
  return ids.map(id => ({ id, member: memberById(id),
    totals: totals.get(id) ?? { ownerId: id, entryCount: 0, totalMinutes: 0, leaveDays: 0 } }))
    .sort((a, b) => b.totals.totalMinutes - a.totals.totalMinutes || memberName(a.member).localeCompare(memberName(b.member)))
})
/** The groups members are in, by name; former members have no group. */
const groups = computed(() => [...new Map(props.members.filter(x => x.groupId)
  .map(x => [x.groupId!, x.groupName ?? ''])).entries()]
  .map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name)))
const groupKey = (member: Member | undefined) => member?.groupId ?? (member ? NO_GROUP : '')
const visibleRows = computed(() => groupFilter.value
  ? rows.value.filter(row => groupKey(row.member) === groupFilter.value) : rows.value)
/** Hours, entries, and leave per group, busiest first, with members in no group last. */
const groupTotals = computed(() => {
  const names = new Map(groups.value.map(x => [x.id, x.name]))
  const totals = new Map<string, { key: string; name: string; members: number; totalMinutes: number; entryCount: number; leaveDays: number; expected: number; toDate: number | null }>()
  for (const row of rows.value) {
    const key = groupKey(row.member)
    if (!key) continue
    const total = totals.get(key) ?? { key, name: names.get(key) ?? copy.value.noGroup, members: 0, totalMinutes: 0, entryCount: 0, leaveDays: 0,
      expected: 0, toDate: expectedToDate.value == null ? null : 0 }
    total.members++
    total.totalMinutes += row.totals.totalMinutes
    total.entryCount += row.totals.entryCount
    total.leaveDays += row.totals.leaveDays
    total.expected += memberExpected(row.totals.leaveDays)
    if (total.toDate != null) total.toDate += memberToDate(row.totals.leaveDays) ?? 0
    totals.set(key, total)
  }
  return [...totals.values()].sort((a, b) => Number(a.key === NO_GROUP) - Number(b.key === NO_GROUP) ||
    b.totalMinutes - a.totalMinutes || a.name.localeCompare(b.name))
})
const hours = (minutes: number) => `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
const formatDate = (value: string) => formatCalendarDate(value, props.formats, props.locale, { weekday: true })
const monthLabel = computed(() => formatMonth(month.value, props.formats, props.locale))
const time = (value: string) => formatTime(value, props.formats)
const expectedSentence = computed(() => !schedule.value || !summary.value ? '' : expectedToDate.value != null
  ? fill(copy.value.expectedOpen, { month: monthLabel.value, toDate: hours(expectedToDate.value), total: hours(monthExpected.value) })
  : fill(copy.value.expectedClosed, { month: monthLabel.value, total: hours(monthExpected.value) }))
const describeDay = (date: string, minutes: number) => `${formatDate(date)}: ${hours(minutes)}`
const hoursLabel = (name: string, logged: number, expected: number) =>
  fill(copy.value.hoursLabel, { name, logged: hours(logged), expected: hours(expected) })
const totalMinutes = computed(() => detail.value?.entries.reduce((sum, x) => sum + x.durationMinutes, 0) ?? 0)
/** Leave in days: part days against the normal working day. */
const leaveDayCount = computed(() => {
  const standard = detailSchedule.value?.standardMinutes ?? 0
  return Math.round((detail.value?.leaves ?? []).reduce((sum, leave) =>
    sum + (leave.minutes && standard ? leave.minutes / standard : 1), 0) * 100) / 100
})
const daysWorked = computed(() => new Set(detail.value?.entries.map(x => x.date)).size)
const dayTotals = computed(() => {
  const totals = new Map<string, number>()
  for (const entry of detail.value?.entries ?? []) totals.set(entry.date, (totals.get(entry.date) ?? 0) + entry.durationMinutes)
  return totals
})
/** The last entry of each day carries that day's total. */
const isLastOfDay = (index: number) => detail.value!.entries[index + 1]?.date !== detail.value!.entries[index]!.date
/** Every day of the month the organization does not work: its holidays by name, then the weekdays
 * outside its work week. */
const daysOff = computed(() => {
  if (!detail.value) return new Map<string, string>()
  const holidays = new Map(detail.value.holidays.map(x => [x.date, x.name]))
  const workingDays = detail.value.workingDays ?? [1, 2, 3, 4, 5]
  const days = new Map<string, string>()
  const count = new Date(Date.UTC(period.value.year, period.value.month, 0)).getUTCDate()
  for (let day = 1; day <= count; day++) {
    const date = `${month.value}-${String(day).padStart(2, '0')}`
    const name = holidays.get(date)
    if (name !== undefined) days.set(date, name)
    else if (!workingDays.includes(new Date(`${date}T00:00:00Z`).getUTCDay())) days.set(date, copy.value.weeklyDayOff)
  }
  return days
})
const byProject = computed(() => {
  const totals = new Map<string, number>()
  for (const entry of detail.value?.entries ?? []) {
    const name = entry.projectName ?? copy.value.noProject
    totals.set(name, (totals.get(name) ?? 0) + entry.durationMinutes)
  }
  return [...totals].sort((a, b) => b[1] - a[1])
})

/** The open member's month as a sheet: a heading and totals, then entries, leave, and days off by
 * date, with each day off named in the Remark column. `forReading` (XLSX) uses the organization's
 * formats and writes each date only on its day's first row; CSV keeps ISO dates and 24-hour times
 * on every row for importing. */
function exportRows(forReading: boolean): Cell[][] {
  const current = detail.value!
  const c = copy.value
  const day = (value: string) => forReading ? formatCalendarDate(value, props.formats, props.locale) : value
  const clock = (value: string) => forReading ? time(value) : value.slice(0, 5)
  const lines: { date: string; order: number; start: string; cells: Cell[] }[] = []
  for (const [date, name] of daysOff.value)
    lines.push({ date, order: 0, start: '', cells: [day(date), '', '', '', name, '', '', '', '', c.daysOff] })
  for (const leave of current.leaves)
    lines.push({ date: leave.date, order: 1, start: '', cells: [day(leave.date), '', '', leave.minutes ? Math.round(leave.minutes / 60 * 100) / 100 : '',
      leave.kind, '', leave.notes ?? '', '', '', c.leave] })
  for (const entry of current.entries)
    lines.push({ date: entry.date, order: 2, start: entry.startTime, cells: [day(entry.date), clock(entry.startTime), clock(entry.endTime),
      Math.round(entry.durationMinutes / 60 * 100) / 100, entry.taskName, entry.detail ?? '', entry.notes ?? '',
      entry.projectName ?? '', entry.categoryName ?? '', daysOff.value.get(entry.date) ?? ''] })
  lines.sort((a, b) => a.date.localeCompare(b.date) || a.order - b.order || a.start.localeCompare(b.start))
  if (forReading) lines.forEach((line, index) => { if (lines[index - 1]?.date === line.date) line.cells[0] = '' })
  return [
    [props.self ? c.myTitle : c.title, `${selectedName.value} · ${monthLabel.value}`],
    [c.status, current.locked ? c.monthClosed : c.monthOpen],
    [c.totalHours, Math.round(totalMinutes.value / 60 * 100) / 100, c.daysWorked, daysWorked.value,
      c.leaveDays, leaveDayCount.value, c.daysOff, daysOff.value.size],
    [],
    [c.date, c.start, c.end, c.duration, c.task, c.detail, c.notes, c.project, c.category, c.remark],
    ...lines.map(x => x.cells)
  ]
}
/** Rows above the entry table: heading, status, totals, and a gap. */
const EXPORT_TABLE_ROW = 4
/** "timesheet-<member>-<YYYY-MM>", keeping the member's name minus characters file systems refuse. */
const fileStem = computed(() => `timesheet-${selectedName.value.replace(/[\\/:*?"<>|\s]+/g, '-')}-${month.value}`)
function exportTimesheet(format: 'xlsx' | 'csv') {
  const sheet = { name: month.value, rows: exportRows(format === 'xlsx'), boldRows: [0],
    table: { headerRow: EXPORT_TABLE_ROW, name: 'Timesheet' }, widths: [14, 9, 9, 9, 40, 28, 24, 18, 18, 22] }
  saveBlob(format === 'xlsx' ? xlsxBlob(sheet) : csvBlob(sheet), `${fileStem.value}.${format}`)
}

/** The open month for Report's PDF, in the same rows as the spreadsheet: days off, leave, then work. */
function pdfRequest() {
  const current = detail.value!
  const holidays = new Set(current.holidays.map(x => x.date))
  return {
    memberName: selectedName.value, period: monthLabel.value, locale: props.locale,
    department: props.self ? null : memberById(selectedId.value)?.groupName ?? null,
    standardDayMinutes: detailSchedule.value?.standardMinutes ?? null,
    dateFormat: props.formats.dateFormat, timeFormat: props.formats.timeFormat,
    lines: [
      ...[...daysOff.value].map(([date, name]) => ({ date, start: null, end: null, durationMinutes: 0, task: name,
        notes: holidays.has(date) ? copy.value.holidays : copy.value.weeklyDayOff, kind: holidays.has(date) ? 'holiday' : 'dayoff' })),
      ...current.leaves.map(leave => ({ date: leave.date, start: null, end: null, durationMinutes: leave.minutes ?? 0, task: leave.kind,
        notes: leave.notes, kind: 'leave' })),
      ...current.entries.map(entry => ({ date: entry.date, start: entry.startTime.slice(0, 5), end: entry.endTime.slice(0, 5),
        durationMinutes: entry.durationMinutes, task: entry.taskName, detail: entry.detail, notes: entry.notes,
        project: entry.projectName, category: entry.categoryName, kind: 'work' }))
    ]
  }
}
const preparingPdf = ref(false)
async function exportAs(format: string) {
  if (format === 'csv') return exportTimesheet('csv')
  if (format !== 'pdf') return
  preparingPdf.value = true
  error.value = ''
  try { saveBlob(await props.exportPdf(pdfRequest()), `${fileStem.value}.pdf`) }
  catch (cause) { error.value = safeError(cause, props.locale) }
  finally { preparingPdf.value = false }
}

async function run(action: () => Promise<void>) {
  busy.value = true
  error.value = ''
  try { await action() } catch (cause) { error.value = safeError(cause, props.locale) }
  finally { busy.value = false }
}
const loadSummary = () => run(async () => {
  if (props.self) {
    detail.value = await props.load<Detail>('/me/timesheet', period.value)
    selectedId.value = detail.value.ownerId
    await loadBalances(null)
    return
  }
  summary.value = await props.load<Summary>('/members/summary', period.value)
  if (selectedId.value) {
    detail.value = await props.load<Detail>(`/members/${selectedId.value}/timesheet`, period.value)
    await loadBalances(selectedId.value)
  }
})
const open = (id: string) => run(async () => {
  detail.value = await props.load<Detail>(`/members/${id}/timesheet`, period.value)
  selectedId.value = id
  await loadBalances(id)
})
function close() {
  selectedId.value = ''
  detail.value = null
  returning.value = false
}

const returning = ref(false)
const returnReason = ref('')
const monthPath = () => `/members/${selectedId.value}/months/${period.value.year}/${period.value.month}`
/** Approve or return the open member's submitted month, then reload the month and the list. */
const decide = (action: 'approve' | 'return', done: string) => run(async () => {
  notice.value = ''
  await props.post!(`${monthPath()}/${action}`, action === 'return' ? { reason: returnReason.value } : undefined)
  returning.value = false
  returnReason.value = ''
  summary.value = await props.load<Summary>('/members/summary', period.value)
  detail.value = await props.load<Detail>(`/members/${selectedId.value}/timesheet`, period.value)
  notice.value = done
})
function approve() {
  if (confirm(fill(copy.value.confirmApprove, { name: selectedName.value, month: monthLabel.value })))
    void decide('approve', copy.value.approvedNotice)
}

watch(month, () => { notice.value = ''; returning.value = false; if (/^\d{4}-\d{2}$/.test(month.value)) loadSummary() })
onMounted(async () => {
  try { catalog.value = await props.load<Catalog>('/catalog', {}) } catch { catalog.value = null }
  await loadSummary()
})
</script>

<template>
  <section class="card member-timesheets" aria-labelledby="member-timesheets-heading">
    <div class="card-heading">
      <div>
        <h2 id="member-timesheets-heading">{{ self ? copy.myTitle : copy.title }}</h2>
        <p class="muted">{{ self ? copy.myHint : copy.hint }}</p>
      </div>
      <div class="member-filters">
        <label v-if="groups.length && !self">{{ copy.group }}<AutoComplete v-model="groupFilter" :label="copy.group" :options="groupChoices" /></label>
        <label>{{ copy.month }}<input v-model="month" type="month" required /></label>
      </div>
    </div>
    <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
    <p v-if="notice" class="feedback feedback-ok" role="status">{{ notice }}</p>
    <p v-if="busy && !summary && !detail" class="muted" role="status">{{ copy.loading }}</p>

    <template v-if="selectedId && detail">
      <div class="detail-heading">
        <button v-if="!self" type="button" class="secondary back-button" @click="close">{{ copy.back }}</button>
        <h3>{{ selectedName }} <span class="detail-month">{{ monthLabel }}</span></h3>
        <span class="status" :data-status="detail.locked ? 'Succeeded' : 'Blocked'">{{ detail.locked ? copy.monthClosed : copy.monthOpen }}</span>
        <span v-if="approvalLabel(detail.status)" class="status" :data-status="approvalDot[detail.status!]">{{ approvalLabel(detail.status) }}</span>
        <div class="export-actions" role="group" :aria-label="copy.exportLabel.replace('{name}', selectedName)">
          <ExportMenu :label="copy.exportExcel" :more-label="copy.moreFormats" :busy="preparingPdf"
            :items="[{ key: 'csv', label: copy.asCsv }, { key: 'pdf', label: copy.asPdf }]" @run="exportTimesheet('xlsx')" @select="exportAs" />
          <span v-if="preparingPdf" class="muted" role="status">{{ copy.preparingPdf }}</span>
        </div>
      </div>
      <div v-if="post && !self && detail.status === 'submitted' && !detail.locked" class="approval-panel">
        <p>{{ fill(copy.submittedHint, { name: selectedName, month: monthLabel }) }}</p>
        <div v-if="!returning" class="approval-actions">
          <button type="button" class="primary" :disabled="busy" @click="approve">{{ copy.approve }}</button>
          <button type="button" :disabled="busy" @click="returning = true">{{ copy.returnMonth }}</button>
        </div>
        <form v-else class="return-form" @submit.prevent="decide('return', copy.returnedNotice)">
          <label>{{ copy.returnReason }}<textarea v-model="returnReason" rows="3" maxlength="1000" required /></label>
          <p class="muted">{{ copy.returnHint }}</p>
          <div class="approval-actions">
            <button type="submit" class="primary" :disabled="busy || !returnReason.trim()">{{ copy.sendReturn }}</button>
            <button type="button" class="secondary" :disabled="busy" @click="returning = false">{{ copy.cancel }}</button>
          </div>
        </form>
      </div>
      <p v-else-if="detail.status === 'returned' && detail.returnReason && !detail.locked" class="approval-panel return-note">
        {{ fill(copy.returnedBecause, { reason: detail.returnReason }) }}</p>
      <MonthStrip v-if="detailSchedule" :month="month" :minutes-by-day="dayTotals" :leave-days="leaveDates" :schedule="detailSchedule"
        :label="copy.strip.replace('{name}', selectedName)" :describe="describeDay" />
      <dl class="member-totals">
        <div><dt>{{ copy.totalHours }}</dt><dd>{{ hours(totalMinutes) }}</dd></div>
        <div><dt>{{ copy.daysWorked }}</dt><dd>{{ daysWorked }}</dd></div>
        <div><dt>{{ copy.entries }}</dt><dd>{{ detail.entries.length }}</dd></div>
        <div><dt>{{ copy.leaveDays }}</dt><dd>{{ leaveDayCount }}</dd></div>
        <div><dt>{{ copy.daysOff }}</dt><dd>{{ daysOff.size }}</dd></div>
      </dl>
      <p v-if="!detail.entries.length" class="empty">{{ copy.noEntries }}</p>
      <div v-else class="table-wrap">
        <table class="entries-table" :aria-label="`${copy.entries}: ${selectedName}`">
          <colgroup><col class="col-date" /><col class="col-time" /><col /><col class="col-tag" /><col class="col-tag" /><col class="col-hours" /></colgroup>
          <thead><tr>
            <th scope="col">{{ copy.date }}</th><th scope="col">{{ copy.time }}</th><th scope="col">{{ copy.task }}</th>
            <th scope="col">{{ copy.project }}</th><th scope="col">{{ copy.category }}</th><th scope="col" class="number">{{ copy.duration }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="(entry, index) in detail.entries" :key="entry.id" :class="{ 'day-end': isLastOfDay(index) }">
              <td><template v-if="index === 0 || detail.entries[index - 1]!.date !== entry.date">{{ formatDate(entry.date) }}<small v-if="daysOff.has(entry.date)" class="day-off">{{ daysOff.get(entry.date) }}</small></template></td>
              <td>{{ time(entry.startTime) }}–{{ time(entry.endTime) }}</td>
              <td><strong>{{ entry.taskName }}</strong><small v-if="entry.detail">{{ entry.detail }}</small><small v-if="entry.notes">{{ entry.notes }}</small></td>
              <td>{{ entry.projectName ?? '—' }}</td>
              <td>{{ entry.categoryName ?? '—' }}</td>
              <td class="number">{{ hours(entry.durationMinutes) }}<small v-if="isLastOfDay(index)">{{ copy.dayTotal }} {{ hours(dayTotals.get(entry.date) ?? 0) }}</small></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="detail-lists">
        <div>
          <h3>{{ copy.byProject }}</h3>
          <ul v-if="byProject.length" class="plain-list"><li v-for="[name, minutes] in byProject" :key="name"><span>{{ name }}</span><strong>{{ hours(minutes) }}</strong></li></ul>
          <p v-else class="muted">{{ copy.noEntries }}</p>
        </div>
        <div>
          <h3>{{ copy.leave }}</h3>
          <ul v-if="detail.leaves.length" class="plain-list"><li v-for="leave in detail.leaves" :key="leave.id"><span>{{ formatDate(leave.date) }}</span><strong>{{ leaveKind(leave) }}</strong></li></ul>
          <p v-else class="muted">{{ copy.noLeave }}</p>
          <template v-if="balances.length">
            <h4 class="balance-heading">{{ fill(copy.leaveBalance, { year: String(period.year) }) }}</h4>
            <ul class="plain-list leave-balances"><li v-for="balance in balances" :key="balance.leaveTypeId" :class="{ overdrawn: (balance.remainingDays ?? 0) < 0 }"><span>{{ balance.name }}</span><strong>{{ balanceLine(balance) }}</strong></li></ul>
          </template>
        </div>
        <div>
          <h3>{{ copy.holidays }}</h3>
          <ul v-if="daysOff.size" class="plain-list"><li v-for="[date, name] in daysOff" :key="date"><span>{{ formatDate(date) }}</span><strong>{{ name }}</strong></li></ul>
          <p v-else class="muted">{{ copy.noHolidays }}</p>
        </div>
      </div>
    </template>

    <template v-else-if="summary">
      <div v-if="groups.length && !groupFilter" class="table-wrap group-totals">
        <table class="ledger" :aria-label="copy.byGroup">
          <caption>{{ copy.byGroup }}</caption>
          <colgroup><col class="col-name" /><col v-if="schedule" /><col class="col-hours" /><col class="col-count" /><col class="col-leave" /><col class="col-last" /></colgroup>
          <thead><tr>
            <th scope="col">{{ copy.group }}</th>
            <th v-if="schedule" scope="col" class="bar-cell"><span class="sr-only">{{ copy.hours }}</span></th>
            <th scope="col" class="number">{{ copy.hours }}</th><th scope="col" class="number optional">{{ copy.entries }}</th>
            <th scope="col" class="number optional">{{ copy.leaveDays }}</th><th scope="col" class="number">{{ copy.members }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="group in groupTotals" :key="group.key">
              <td><button type="button" class="link-button" @click="groupFilter = group.key">{{ group.name }}</button>
                <HoursBar v-if="schedule" class="inline-bar" :logged="group.totalMinutes" :expected="group.expected" :to-date="group.toDate"
                  :label="hoursLabel(group.name, group.totalMinutes, group.expected)" /></td>
              <td v-if="schedule" class="bar-cell"><HoursBar :logged="group.totalMinutes" :expected="group.expected" :to-date="group.toDate"
                :label="hoursLabel(group.name, group.totalMinutes, group.expected)" /></td>
              <td class="number">{{ hours(group.totalMinutes) }}</td>
              <td class="number optional">{{ group.entryCount }}</td><td class="number optional">{{ group.leaveDays }}</td>
              <td class="number">{{ group.members }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="month-state"><span class="status" :data-status="summary.locked ? 'Succeeded' : 'Blocked'">{{ summary.locked ? copy.monthClosed : copy.monthOpen }}</span>
        <span v-if="expectedSentence" class="expected-sentence">{{ expectedSentence }}</span></p>
      <div class="table-wrap">
        <table class="member-table ledger" :aria-label="copy.title">
          <colgroup><col class="col-name" /><col v-if="schedule" /><col class="col-hours" /><col class="col-count" /><col class="col-leave" /><col class="col-last" /></colgroup>
          <thead><tr>
            <th scope="col">{{ copy.member }}</th>
            <th v-if="schedule" scope="col" class="bar-cell"><span class="sr-only">{{ copy.hours }}</span></th>
            <th scope="col" class="number">{{ copy.hours }}</th>
            <th scope="col" class="number optional">{{ copy.entries }}</th><th scope="col" class="number optional">{{ copy.leaveDays }}</th>
            <th scope="col"><span class="sr-only">{{ copy.view }}</span></th>
          </tr></thead>
          <tbody>
            <tr v-for="row in visibleRows" :key="row.id">
              <td><strong>{{ memberName(row.member) }}</strong><span v-if="approvalLabel(row.totals.status)" class="status approval-status" :data-status="approvalDot[row.totals.status!]">{{ approvalLabel(row.totals.status) }}</span><small v-if="row.member && memberName(row.member) !== row.member.email">{{ row.member.email }}</small><small v-if="groups.length && !groupFilter && row.member">{{ row.member.groupName ?? copy.noGroup }}</small>
                <!-- On a phone the bar sits under the name instead of in its own column. -->
                <HoursBar v-if="schedule && row.member" class="inline-bar" :logged="row.totals.totalMinutes" :expected="memberExpected(row.totals.leaveDays)"
                  :to-date="memberToDate(row.totals.leaveDays)" :label="hoursLabel(memberName(row.member), row.totals.totalMinutes, memberExpected(row.totals.leaveDays))" /></td>
              <td v-if="schedule" class="bar-cell">
                <!-- Former members have no expectation; their hours are listed without a bar. -->
                <HoursBar v-if="row.member" :logged="row.totals.totalMinutes" :expected="memberExpected(row.totals.leaveDays)"
                  :to-date="memberToDate(row.totals.leaveDays)" :label="hoursLabel(memberName(row.member), row.totals.totalMinutes, memberExpected(row.totals.leaveDays))" />
              </td>
              <td class="number">{{ row.totals.entryCount || row.totals.leaveDays ? hours(row.totals.totalMinutes) : '—' }}<small v-if="schedule && row.member">{{ fill(copy.ofHours, { expected: hours(memberExpected(row.totals.leaveDays)) }) }}</small></td>
              <td class="number optional">{{ row.totals.entryCount }}</td>
              <td class="number optional">{{ row.totals.leaveDays }}</td>
              <td class="actions-cell">
                <button type="button" :disabled="busy" :aria-label="`${copy.view}: ${memberName(row.member)}`" @click="open(row.id)"><span class="view-long">{{ copy.view }}</span><span class="view-short">{{ copy.viewShort }}</span></button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="!visibleRows.length" class="empty">{{ copy.noMembersInGroup }}</p>
    </template>
  </section>
</template>

<style scoped>
.member-timesheets .card-heading p{margin:.3rem 0 0;max-width:36rem}
.member-filters{display:flex;flex-wrap:wrap;gap:.6rem}
.group-totals{margin-bottom:1.25rem}
.group-totals caption{text-align:left;font-family:var(--font-display);font-weight:600;padding-bottom:.4rem}
/* The ledger: each row's hours as a bar against what the month expects. */
/* Both ledgers share one column grid, so bars and figures line up from table to table. */
.ledger{table-layout:fixed;min-width:40rem}
.ledger .col-name{width:26%}.ledger .col-hours{width:7rem}.ledger .col-count{width:5.5rem}
.ledger .col-leave{width:6.5rem}.ledger .col-last{width:9.5rem}
.ledger td:first-child{overflow-wrap:anywhere}
.bar-cell{vertical-align:middle}
.member-timesheets .inline-bar{display:none}
.link-button{background:none;border:0;padding:0;color:inherit;font:inherit;font-weight:600;text-align:left;text-decoration:underline;cursor:pointer}
.number{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
.number small{white-space:nowrap}
.actions-cell{text-align:right}
.view-short{display:none}
.month-state{display:flex;flex-wrap:wrap;align-items:center;gap:.4rem .75rem;margin:0 0 .75rem}
.expected-sentence{color:var(--color-ink);font-size:.92rem}
.detail-heading{display:flex;flex-wrap:wrap;align-items:center;gap:.6rem 1rem;margin:0 0 1.25rem}
.detail-heading h3{margin:0;font-family:var(--font-display);font-size:1.35rem;font-weight:600;flex:1 1 14rem}
.detail-month{color:var(--color-muted);font-weight:500;margin-left:.35rem}
.export-actions{display:flex;flex-wrap:wrap;gap:.5rem}
/* A submitted month waits on the admin: the decision sits above the figures. */
.approval-panel{margin:0 0 1.25rem;padding:.85rem 1rem;border:1px solid var(--color-border);border-left:4px solid var(--color-brand);border-radius:.5rem;background:var(--color-surface-muted)}
.approval-panel p{margin:0 0 .75rem}
.approval-panel.return-note{border-left-color:var(--color-warn-strong);white-space:pre-line;overflow-wrap:anywhere}
.approval-actions{display:flex;flex-wrap:wrap;gap:.5rem}
.return-form{display:grid;gap:.6rem;max-width:36rem}
.return-form p{margin:0}
.return-form textarea{font:inherit;color:var(--color-ink);border:1px solid var(--color-border-strong);border-radius:.5rem;padding:.5rem .65rem;background:var(--color-surface);resize:vertical}
.approval-status{margin-left:.6rem;font-size:.78rem;white-space:nowrap}
/* A member's month in figures: one row, divided, no cards. */
.member-totals{display:flex;flex-wrap:wrap;margin:0 0 1.25rem;border-top:1px solid var(--color-border);border-bottom:1px solid var(--color-border)}
.member-totals div{flex:1 1 7rem;padding:.7rem 1rem .7rem 0;margin-right:1rem}
.member-totals div + div{border-left:1px solid var(--color-border);padding-left:1rem}
.member-totals dt{color:var(--color-muted);font-size:.82rem}
.member-totals dd{margin:.15rem 0 0;font-family:var(--font-display);font-size:1.5rem;font-weight:600;font-variant-numeric:tabular-nums}
tr.day-end td{border-bottom-color:var(--color-border-strong)}
.day-off{display:block;width:max-content;margin-top:.2rem;padding:.05rem .45rem;border-radius:999px;background:var(--color-day-off);color:var(--color-ink-soft);font-size:.72rem}
/* A member's entries: the task takes the room, the rest keep steady widths. */
.entries-table{table-layout:fixed;min-width:44rem}
.entries-table .col-date{width:9.5rem}.entries-table .col-time{width:8.5rem}.entries-table .col-tag{width:8.5rem}.entries-table .col-hours{width:6.5rem}
.entries-table td{overflow-wrap:anywhere}
.detail-lists{margin-top:1.5rem;display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,14rem),1fr));gap:0 1.5rem}
.detail-lists h3{font-family:var(--font-display);font-weight:600}
.plain-list{list-style:none;margin:0;padding:0;display:grid;gap:.35rem}
.balance-heading{margin:1rem 0 .5rem;font-size:.9rem;font-weight:600;color:var(--color-muted)}
.leave-balances strong{font-weight:500;font-variant-numeric:tabular-nums;text-align:right}
.leave-balances li.overdrawn strong{color:var(--color-danger);font-weight:600}
.plain-list li{display:flex;justify-content:space-between;gap:1rem;font-size:.9rem;border-bottom:1px solid var(--color-border);padding:.35rem 0}
@media (max-width:40rem){
  .ledger{table-layout:auto;min-width:0}
  .ledger col[class]{width:auto}
  /* On a phone the counts give way to the bar; a member's own view still lists them. */
  .bar-cell,.optional,.view-long{display:none}
  .view-short{display:inline}
  .member-timesheets .inline-bar{display:block;margin-top:.45rem}
  .member-totals div{flex-basis:40%}
  .member-totals div + div{border-left:0;padding-left:0}
}
</style>
