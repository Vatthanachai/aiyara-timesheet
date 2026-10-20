<script setup lang="ts">
import { safeError } from '../../../../shared/api-response'
import { useLocaleText, type Locale } from '../../../../shared/i18n'
import { zonedToday } from '../utils/reportPeriods'

type Member = { id: string; firstName: string; lastName: string; email: string; role: string; status: string; groupId?: string | null; groupName?: string | null }
type MemberSummary = { ownerId: string; entryCount: number; totalMinutes: number; leaveDays: number }
type Summary = { year: number; month: number; locked: boolean; members: MemberSummary[] }
type Entry = {
  id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string
  detail: string | null; notes: string | null; projectName: string | null; categoryName: string | null
}
type Detail = {
  ownerId: string; locked: boolean; entries: Entry[]
  leaves: { id: string; date: string; kind: string; notes: string | null }[]
  holidays: { date: string; name: string }[]
}

const props = defineProps<{
  members: Member[]
  locale: Locale
  timeZoneId: string
  /** GET a Timesheet API path through Gateway with the remote's session. */
  load: <T>(path: string, query: Record<string, unknown>) => Promise<T>
}>()

const copy = useLocaleText(toRef(props, 'locale'), { th: {
  title: 'Timesheet ของสมาชิก', hint: 'ดูเวลาทำงานและวันลาของสมาชิกแต่ละคนรายเดือน รวมเดือนปัจจุบันที่ยังไม่ปิดรอบ',
  month: 'เดือน', member: 'สมาชิก', entries: 'รายการ', hours: 'ชั่วโมง', leaveDays: 'วันลา', view: 'ดู timesheet',
  noData: 'ยังไม่มีรายการ', formerMember: 'สมาชิกเดิม', loading: 'กำลังโหลด…', back: 'สมาชิกทั้งหมด',
  monthClosed: 'ปิดรอบแล้ว', monthOpen: 'ยังไม่ปิดรอบ (ข้อมูลอาจเปลี่ยน)', totalHours: 'ชั่วโมงรวม', daysWorked: 'วันที่ทำงาน',
  date: 'วันที่', time: 'เวลา', task: 'งาน', project: 'โครงการ', category: 'หมวดหมู่', duration: 'ชั่วโมง',
  byProject: 'ชั่วโมงตามโครงการ', noProject: 'ไม่ระบุโครงการ', leave: 'วันลา', holidays: 'วันหยุดขององค์กร',
  noEntries: 'ไม่มีรายการเวลาทำงานในเดือนนี้', noLeave: 'ไม่มีวันลา', noHolidays: 'ไม่มีวันหยุด', dayTotal: 'รวมวันนี้',
  group: 'กลุ่ม', allGroups: 'ทุกกลุ่ม', noGroup: 'ไม่มีกลุ่ม', byGroup: 'สรุปตามกลุ่ม', members: 'สมาชิก', noMembersInGroup: 'ไม่มีสมาชิกในกลุ่มนี้'
}, en: {
  title: 'Member timesheets', hint: "Review each member's hours and leave by month, including the current open month.",
  month: 'Month', member: 'Member', entries: 'Entries', hours: 'Hours', leaveDays: 'Leave days', view: 'View timesheet',
  noData: 'No entries yet', formerMember: 'Former member', loading: 'Loading…', back: 'All members',
  monthClosed: 'Month closed', monthOpen: 'Month open (may still change)', totalHours: 'Total hours', daysWorked: 'Days worked',
  date: 'Date', time: 'Time', task: 'Task', project: 'Project', category: 'Category', duration: 'Hours',
  byProject: 'Hours by project', noProject: 'No project', leave: 'Leave', holidays: 'Organization holidays',
  noEntries: 'No time entries this month.', noLeave: 'No leave.', noHolidays: 'No holidays.', dayTotal: 'Day total',
  group: 'Group', allGroups: 'All groups', noGroup: 'No group', byGroup: 'Totals by group', members: 'Members', noMembersInGroup: 'No members in this group.'
} })

const month = ref(zonedToday(props.timeZoneId).slice(0, 7))
const summary = ref<Summary | null>(null)
const selectedId = ref('')
/** '' is every member; NO_GROUP is directory members in no group. */
const NO_GROUP = '-'
const groupFilter = ref('')
const detail = ref<Detail | null>(null)
const busy = ref(false)
const error = ref('')
const dateLocale = computed(() => props.locale === 'th' ? 'th-TH' : 'en-GB')
const period = computed(() => ({ year: Number(month.value.slice(0, 4)), month: Number(month.value.slice(5, 7)) }))

const memberName = (member: Member | undefined) =>
  member ? [member.firstName, member.lastName].filter(part => part?.trim()).join(' ') || member.email : copy.value.formerMember
const memberById = (id: string) => props.members.find(x => x.id === id)
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
  const totals = new Map<string, { key: string; name: string; members: number; totalMinutes: number; entryCount: number; leaveDays: number }>()
  for (const row of rows.value) {
    const key = groupKey(row.member)
    if (!key) continue
    const total = totals.get(key) ?? { key, name: names.get(key) ?? copy.value.noGroup, members: 0, totalMinutes: 0, entryCount: 0, leaveDays: 0 }
    total.members++
    total.totalMinutes += row.totals.totalMinutes
    total.entryCount += row.totals.entryCount
    total.leaveDays += row.totals.leaveDays
    totals.set(key, total)
  }
  return [...totals.values()].sort((a, b) => Number(a.key === NO_GROUP) - Number(b.key === NO_GROUP) ||
    b.totalMinutes - a.totalMinutes || a.name.localeCompare(b.name))
})
const hours = (minutes: number) => `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
const formatDate = (value: string) => new Intl.DateTimeFormat(dateLocale.value,
  { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`))
const monthLabel = computed(() => new Intl.DateTimeFormat(dateLocale.value,
  { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${month.value}-01T00:00:00Z`)))
const time = (value: string) => value.slice(0, 5)
const totalMinutes = computed(() => detail.value?.entries.reduce((sum, x) => sum + x.durationMinutes, 0) ?? 0)
const daysWorked = computed(() => new Set(detail.value?.entries.map(x => x.date)).size)
const dayTotals = computed(() => {
  const totals = new Map<string, number>()
  for (const entry of detail.value?.entries ?? []) totals.set(entry.date, (totals.get(entry.date) ?? 0) + entry.durationMinutes)
  return totals
})
/** The last entry of each day carries that day's total. */
const isLastOfDay = (index: number) => detail.value!.entries[index + 1]?.date !== detail.value!.entries[index]!.date
const byProject = computed(() => {
  const totals = new Map<string, number>()
  for (const entry of detail.value?.entries ?? []) {
    const name = entry.projectName ?? copy.value.noProject
    totals.set(name, (totals.get(name) ?? 0) + entry.durationMinutes)
  }
  return [...totals].sort((a, b) => b[1] - a[1])
})

async function run(action: () => Promise<void>) {
  busy.value = true
  error.value = ''
  try { await action() } catch (cause) { error.value = safeError(cause, props.locale) }
  finally { busy.value = false }
}
const loadSummary = () => run(async () => {
  summary.value = await props.load<Summary>('/members/summary', period.value)
  if (selectedId.value) detail.value = await props.load<Detail>(`/members/${selectedId.value}/timesheet`, period.value)
})
const open = (id: string) => run(async () => {
  detail.value = await props.load<Detail>(`/members/${id}/timesheet`, period.value)
  selectedId.value = id
})
function close() {
  selectedId.value = ''
  detail.value = null
}

watch(month, () => { if (/^\d{4}-\d{2}$/.test(month.value)) loadSummary() })
onMounted(loadSummary)
</script>

<template>
  <section class="card member-timesheets" aria-labelledby="member-timesheets-heading">
    <div class="card-heading">
      <div>
        <h2 id="member-timesheets-heading">{{ copy.title }}</h2>
        <p class="muted">{{ copy.hint }}</p>
      </div>
      <div class="member-filters">
        <label v-if="groups.length">{{ copy.group }}<select v-model="groupFilter" :aria-label="copy.group">
          <option value="">{{ copy.allGroups }}</option>
          <option v-for="group in groups" :key="group.id" :value="group.id">{{ group.name }}</option>
          <option :value="NO_GROUP">{{ copy.noGroup }}</option>
        </select></label>
        <label>{{ copy.month }}<input v-model="month" type="month" required /></label>
      </div>
    </div>
    <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
    <p v-if="busy && !summary" class="muted" role="status">{{ copy.loading }}</p>

    <template v-if="selectedId && detail">
      <div class="detail-heading">
        <button type="button" class="secondary" @click="close">← {{ copy.back }}</button>
        <h3>{{ memberName(memberById(selectedId)) }} · {{ monthLabel }}</h3>
        <span class="status" :data-status="detail.locked ? 'Succeeded' : 'Blocked'">{{ detail.locked ? copy.monthClosed : copy.monthOpen }}</span>
      </div>
      <dl class="summary member-totals">
        <div><dt>{{ copy.totalHours }}</dt><dd>{{ hours(totalMinutes) }}</dd></div>
        <div><dt>{{ copy.daysWorked }}</dt><dd>{{ daysWorked }}</dd></div>
        <div><dt>{{ copy.entries }}</dt><dd>{{ detail.entries.length }}</dd></div>
        <div><dt>{{ copy.leaveDays }}</dt><dd>{{ detail.leaves.length }}</dd></div>
      </dl>
      <p v-if="!detail.entries.length" class="empty">{{ copy.noEntries }}</p>
      <div v-else class="table-wrap">
        <table :aria-label="`${copy.entries}: ${memberName(memberById(selectedId))}`">
          <thead><tr>
            <th scope="col">{{ copy.date }}</th><th scope="col">{{ copy.time }}</th><th scope="col">{{ copy.task }}</th>
            <th scope="col">{{ copy.project }}</th><th scope="col">{{ copy.category }}</th><th scope="col" class="number">{{ copy.duration }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="(entry, index) in detail.entries" :key="entry.id" :class="{ 'day-end': isLastOfDay(index) }">
              <td>{{ index === 0 || detail.entries[index - 1]!.date !== entry.date ? formatDate(entry.date) : '' }}</td>
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
          <ul v-if="detail.leaves.length" class="plain-list"><li v-for="leave in detail.leaves" :key="leave.id"><span>{{ formatDate(leave.date) }}</span><strong>{{ leave.kind }}</strong></li></ul>
          <p v-else class="muted">{{ copy.noLeave }}</p>
        </div>
        <div>
          <h3>{{ copy.holidays }}</h3>
          <ul v-if="detail.holidays.length" class="plain-list"><li v-for="holiday in detail.holidays" :key="holiday.date"><span>{{ formatDate(holiday.date) }}</span><strong>{{ holiday.name }}</strong></li></ul>
          <p v-else class="muted">{{ copy.noHolidays }}</p>
        </div>
      </div>
    </template>

    <template v-else-if="summary">
      <div v-if="groups.length && !groupFilter" class="table-wrap group-totals">
        <table :aria-label="copy.byGroup">
          <caption>{{ copy.byGroup }}</caption>
          <thead><tr>
            <th scope="col">{{ copy.group }}</th><th scope="col" class="number">{{ copy.members }}</th>
            <th scope="col" class="number">{{ copy.hours }}</th><th scope="col" class="number">{{ copy.entries }}</th>
            <th scope="col" class="number">{{ copy.leaveDays }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="group in groupTotals" :key="group.key">
              <td><button type="button" class="link-button" @click="groupFilter = group.key">{{ group.name }}</button></td>
              <td class="number">{{ group.members }}</td><td class="number">{{ hours(group.totalMinutes) }}</td>
              <td class="number">{{ group.entryCount }}</td><td class="number">{{ group.leaveDays }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="muted month-state"><span class="status" :data-status="summary.locked ? 'Succeeded' : 'Blocked'">{{ summary.locked ? copy.monthClosed : copy.monthOpen }}</span></p>
      <div class="table-wrap">
        <table class="member-table" :aria-label="copy.title">
          <thead><tr>
            <th scope="col">{{ copy.member }}</th><th scope="col" class="number">{{ copy.hours }}</th>
            <th scope="col" class="number">{{ copy.entries }}</th><th scope="col" class="number">{{ copy.leaveDays }}</th>
            <th scope="col"><span class="sr-only">{{ copy.view }}</span></th>
          </tr></thead>
          <tbody>
            <tr v-for="row in visibleRows" :key="row.id">
              <td><strong>{{ memberName(row.member) }}</strong><small v-if="row.member && memberName(row.member) !== row.member.email">{{ row.member.email }}</small><small v-if="groups.length && !groupFilter && row.member">{{ row.member.groupName ?? copy.noGroup }}</small></td>
              <td class="number">{{ row.totals.entryCount || row.totals.leaveDays ? hours(row.totals.totalMinutes) : '—' }}</td>
              <td class="number">{{ row.totals.entryCount }}</td>
              <td class="number">{{ row.totals.leaveDays }}</td>
              <td class="actions-cell">
                <button type="button" :disabled="busy" :aria-label="`${copy.view}: ${memberName(row.member)}`" @click="open(row.id)">{{ copy.view }}</button>
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
.member-table{min-width:36rem}
.member-filters{display:flex;flex-wrap:wrap;gap:.6rem}
.group-totals{margin-bottom:1rem}
.group-totals table{min-width:30rem}
.group-totals caption{text-align:left;font-weight:600;padding-bottom:.4rem}
.link-button{background:none;border:0;padding:0;color:inherit;font:inherit;font-weight:600;text-decoration:underline;cursor:pointer}
.number{text-align:right;font-variant-numeric:tabular-nums}
.number small{white-space:nowrap}
.actions-cell{text-align:right}
.month-state{margin:0 0 .5rem}
.detail-heading{display:flex;flex-wrap:wrap;align-items:center;gap:.6rem 1rem;margin:0 0 1rem}
.detail-heading h3{margin:0;font-size:1rem;flex:1 1 14rem}
.member-totals{margin-bottom:1rem;grid-template-columns:repeat(auto-fit,minmax(min(100%,7rem),1fr))}
tr.day-end td{border-bottom-color:#c9d3e1}
.detail-lists{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,14rem),1fr));gap:0 1.5rem}
.plain-list{list-style:none;margin:0;padding:0;display:grid;gap:.35rem}
.plain-list li{display:flex;justify-content:space-between;gap:1rem;font-size:.9rem;border-bottom:1px dashed var(--color-border);padding:.3rem 0}
</style>
