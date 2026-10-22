<script setup lang="ts">
import { safeError } from '../../../../shared/api-response'
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import DateField from '../../../../shared/components/DateField.vue'
import { useLocaleText, type Locale } from '../../../../shared/i18n'
import { formatMinutes, formatHours, formatMoney } from '../../../../shared/money'
import { formatDate, type Formats } from '../../../../shared/date-format'
import { addDays, monthPeriod, previousPeriod, zonedToday } from '../utils/reportPeriods'

type Amount = { currencyCode: string; amount: number }
type ProjectRow = {
  projectId: string; name: string; client: string | null; isBillable: boolean; hourlyRate: number | null; currencyCode: string | null
  budgetHours: number | null; isDeleted: boolean; periodMinutes: number; billableMinutes: number; billableAmount: number | null
  usedMinutes: number; usedHours: number; remainingHours: number | null; overBudget: boolean
}
type Totals = { periodMinutes: number; billableMinutes: number; amounts: Amount[] }
type ProjectUsage = { from: string | null; to: string | null; unassignedMinutes: number; projects: ProjectRow[]; totals: Totals }
type ClientRow = { client: string | null; projectCount: number; periodMinutes: number; billableMinutes: number; nonBillableMinutes: number; amounts: Amount[] }
type ClientUsage = { from: string | null; to: string | null; unassignedMinutes: number; clients: ClientRow[]; totals: Totals }
type MemberUsage = { project: ProjectRow; members: { ownerId: string; minutes: number }[] }
type Member = { id: string; firstName: string; lastName: string; email: string }
type PeriodMode = 'thisMonth' | 'lastMonth' | 'month' | 'range' | 'all'

const props = defineProps<{
  members: Member[]
  locale: Locale
  formats: Formats
  timeZoneId: string
  /** GET a Timesheet API path through Gateway with the remote's session. */
  load: <T>(path: string, query: Record<string, unknown>) => Promise<T>
}>()

const copy = useLocaleText(toRef(props, 'locale'), { th: {
  title: 'โครงการและลูกค้า', hint: 'ชั่วโมงที่ใช้ งบประมาณ และยอดเรียกเก็บเงินของแต่ละโครงการและลูกค้า เฉพาะผู้ดูแลองค์กร',
  period: 'ช่วงเวลา', thisMonth: 'เดือนนี้', lastMonth: 'เดือนที่แล้ว', pickMonth: 'เลือกเดือน', customRange: 'กำหนดช่วงวันที่', allTime: 'ตลอดเวลา',
  month: 'เดือน', year: 'ปี', from: 'ตั้งแต่วันที่', to: 'ถึงวันที่', showing: 'แสดงช่วง {period}', showingAll: 'แสดงข้อมูลตลอดเวลา',
  rangeOrder: 'วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด',
  byProject: 'ตามโครงการ', byClient: 'ตามลูกค้า', project: 'โครงการ', client: 'ลูกค้า', projects: 'โครงการ', noClient: 'ไม่มีลูกค้า',
  hours: 'ชั่วโมง', billableHours: 'ชั่วโมงที่เรียกเก็บได้', nonBillableHours: 'ชั่วโมงที่เรียกเก็บไม่ได้', amount: 'ยอดเรียกเก็บ', budget: 'งบประมาณ (ตลอดโครงการ)',
  billable: 'เรียกเก็บเงินได้', nonBillable: 'เรียกเก็บเงินไม่ได้', deleted: 'ลบแล้ว', noRate: 'ไม่มีอัตรา', noBudget: 'ไม่มีงบประมาณ',
  usedOf: 'ใช้ {used} จาก {budget} ชั่วโมง', usedOnly: 'ใช้ {used} ชั่วโมง', left: 'เหลือ {hours} ชั่วโมง', overBy: 'เกินงบ {hours} ชั่วโมง',
  barLabel: '{name}: ใช้ {used} จากงบ {budget} ชั่วโมง ({percent}%)', overLabel: '{name}: เกินงบ ใช้ {used} จากงบ {budget} ชั่วโมง',
  totals: 'รวม', unassigned: 'ชั่วโมงที่ไม่ระบุโครงการ: {hours}', noAmounts: '—',
  members: 'สมาชิก', showMembers: 'ดูสมาชิก', hideMembers: 'ซ่อนสมาชิก', memberHours: 'ชั่วโมงของสมาชิกในโครงการ {name}', noMembers: 'ไม่มีชั่วโมงในช่วงนี้',
  formerMember: 'สมาชิกเดิม', loading: 'กำลังโหลด…', retry: 'ลองอีกครั้ง', empty: 'ยังไม่มีโครงการ',
  loadFailed: 'โหลดข้อมูลตามโครงการไม่สำเร็จ', loadFailedClients: 'โหลดข้อมูลตามลูกค้าไม่สำเร็จ', membersFailed: 'โหลดสมาชิกของโครงการนี้ไม่สำเร็จ',
  updated: 'อัปเดตแล้ว: {projects} โครงการ {clients} ลูกค้า',
  notes: 'ยอดเรียกเก็บคำนวณจากอัตราปัจจุบันของแต่ละโครงการ หากเปลี่ยนอัตรา เวลาที่บันทึกไว้แล้วจะคำนวณใหม่ด้วย ยอดต่างสกุลเงินแสดงแยกกัน ไม่รวมกัน งบประมาณและชั่วโมงที่ใช้นับตลอดเวลา ไม่ขึ้นกับช่วงที่เลือก เกินงบแล้วระบบเตือนเท่านั้น ไม่ห้ามบันทึกเวลา'
}, en: {
  title: 'Projects and clients', hint: 'Hours used, budgets, and billable amounts by project and by client. Organization admins only.',
  period: 'Period', thisMonth: 'This month', lastMonth: 'Last month', pickMonth: 'Choose a month', customRange: 'Custom range', allTime: 'All time',
  month: 'Month', year: 'Year', from: 'From', to: 'To', showing: 'Showing {period}', showingAll: 'Showing all time',
  rangeOrder: 'The start date must not be after the end date.',
  byProject: 'By project', byClient: 'By client', project: 'Project', client: 'Client', projects: 'Projects', noClient: 'No client',
  hours: 'Hours', billableHours: 'Billable hours', nonBillableHours: 'Non-billable hours', amount: 'Billable amount', budget: 'Budget (whole project)',
  billable: 'Billable', nonBillable: 'Not billable', deleted: 'Deleted', noRate: 'No rate', noBudget: 'No budget',
  usedOf: '{used} of {budget} h used', usedOnly: '{used} h used', left: '{hours} h left', overBy: 'Over budget by {hours} h',
  barLabel: '{name}: {used} of {budget} budget hours used ({percent}%)', overLabel: '{name}: over budget, {used} of {budget} budget hours used',
  totals: 'Total', unassigned: 'Hours with no project: {hours}', noAmounts: '—',
  members: 'Members', showMembers: 'Show members', hideMembers: 'Hide members', memberHours: 'Hours by member on {name}', noMembers: 'No hours in this period.',
  formerMember: 'Former member', loading: 'Loading…', retry: 'Try again', empty: 'No projects yet.',
  loadFailed: 'Could not load usage by project.', loadFailedClients: 'Could not load usage by client.', membersFailed: 'Could not load the members of this project.',
  updated: 'Updated: {projects} projects, {clients} clients',
  notes: "Amounts use each project's current hourly rate, so changing a rate also reprices time already logged. Amounts in different currencies are listed separately and never added together. Budgets and hours used count all time, whatever period is chosen. Going over budget only warns; time can still be saved."
} })

// ---- Period: month presets, a chosen month, a custom range, or all time -------------------------
const today = computed(() => zonedToday(props.timeZoneId))
const mode = ref<PeriodMode>('thisMonth')
const monthYear = ref(Number(zonedToday(props.timeZoneId).slice(0, 4)))
const monthNumber = ref(Number(zonedToday(props.timeZoneId).slice(5, 7)))
const rangeFrom = ref(`${zonedToday(props.timeZoneId).slice(0, 8)}01`)
const rangeTo = ref(zonedToday(props.timeZoneId))
const monthBounds = (year: number, month: number) => { const period = monthPeriod(year, month); return { from: period.start, to: addDays(period.end, -1) } }
/** The inclusive dates asked for; `{}` is all time and null is a range not yet valid. */
const bounds = computed<{ from?: string; to?: string } | null>(() => {
  const [year, month] = today.value.split('-').map(Number)
  switch (mode.value) {
    case 'thisMonth': return monthBounds(year!, month!)
    case 'lastMonth': { const last = previousPeriod('Monthly', today.value); return { from: last.start, to: addDays(last.end, -1) } }
    case 'month': return monthBounds(monthYear.value, monthNumber.value)
    case 'range': return rangeFrom.value && rangeTo.value && rangeFrom.value <= rangeTo.value ? { from: rangeFrom.value, to: rangeTo.value } : null
    default: return {}
  }
})
const rangeInvalid = computed(() => mode.value === 'range' && Boolean(rangeFrom.value && rangeTo.value) && rangeFrom.value > rangeTo.value)
const periodLabel = computed(() => !bounds.value ? '' : bounds.value.from
  ? copy.value.showing.replace('{period}', `${formatDate(bounds.value.from, props.formats, props.locale)} – ${formatDate(bounds.value.to!, props.formats, props.locale)}`)
  : copy.value.showingAll)
const modeChoices = computed(() => [
  { value: 'thisMonth', label: copy.value.thisMonth }, { value: 'lastMonth', label: copy.value.lastMonth },
  { value: 'month', label: copy.value.pickMonth }, { value: 'range', label: copy.value.customRange }, { value: 'all', label: copy.value.allTime }])
const monthChoices = computed(() => Array.from({ length: 12 }, (_, index) => ({ value: index + 1,
  label: new Intl.DateTimeFormat(props.locale === 'th' ? 'th-TH' : 'en', { month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(2026, index, 1))) })))
const yearChoices = computed(() => Array.from({ length: 6 }, (_, index) => { const year = Number(today.value.slice(0, 4)) - index; return { value: year, label: String(year) } }))

// ---- Members of one project ----------------------------------------------------------------------
const memberNames = computed(() => new Map(props.members.map(member =>
  [member.id, [member.firstName, member.lastName].filter(Boolean).join(' ') || member.email])))
const openProjectId = ref('')
const memberCache = new Map<string, MemberUsage>()
const memberState = reactive<{ loading: string; error: string; data: MemberUsage | null }>({ loading: '', error: '', data: null })
async function toggleMembers(row: ProjectRow) {
  if (openProjectId.value === row.projectId) { openProjectId.value = ''; return }
  openProjectId.value = row.projectId
  memberState.error = ''
  memberState.data = memberCache.get(row.projectId) ?? null
  if (memberState.data) return
  memberState.loading = row.projectId
  try {
    const data = await props.load<MemberUsage>(`/projects/${row.projectId}/usage`, query())
    memberCache.set(row.projectId, data)
    if (openProjectId.value === row.projectId) memberState.data = data
  } catch (cause) {
    if (openProjectId.value === row.projectId) memberState.error = `${copy.value.membersFailed} ${safeError(cause, props.locale)}`
  } finally { if (memberState.loading === row.projectId) memberState.loading = '' }
}
const sortedMembers = computed(() => [...(memberState.data?.members ?? [])].sort((a, b) => b.minutes - a.minutes))

// ---- Loading: each table loads on its own, so one failure leaves the other and the page working ----
const usage = ref<ProjectUsage | null>(null)
const clientUsage = ref<ClientUsage | null>(null)
const usageError = ref('')
const clientError = ref('')
const isLoading = ref(false)
let generation = 0
const query = () => ({ from: bounds.value?.from, to: bounds.value?.to })
async function refresh() {
  if (!bounds.value) return
  const mine = ++generation
  isLoading.value = true
  usageError.value = ''
  clientError.value = ''
  openProjectId.value = ''
  memberCache.clear()
  const [projectResult, clientResult] = await Promise.allSettled([
    props.load<ProjectUsage>('/projects/usage', query()), props.load<ClientUsage>('/clients/usage', query())])
  if (mine !== generation) return
  if (projectResult.status === 'fulfilled') usage.value = projectResult.value
  else { usage.value = null; usageError.value = `${copy.value.loadFailed} ${safeError(projectResult.reason, props.locale)}` }
  if (clientResult.status === 'fulfilled') clientUsage.value = clientResult.value
  else { clientUsage.value = null; clientError.value = `${copy.value.loadFailedClients} ${safeError(clientResult.reason, props.locale)}` }
  isLoading.value = false
}
watch(() => JSON.stringify(bounds.value), refresh, { immediate: true })
const announcement = computed(() => isLoading.value || !usage.value || !clientUsage.value ? ''
  : copy.value.updated.replace('{projects}', String(usage.value.projects.length)).replace('{clients}', String(clientUsage.value.clients.length)))

// ---- Presentation --------------------------------------------------------------------------------
const hoursText = (minutes: number) => formatMinutes(minutes, props.locale)
const money = (amount: number, currency: string | null) => formatMoney(amount, currency, props.locale)
const percentUsed = (row: ProjectRow) => row.budgetHours ? Math.round((row.usedHours / row.budgetHours) * 100) : 0
const barFill = (row: ProjectRow) => `${Math.min(100, Math.max(0, (row.usedHours / (row.budgetHours || 1)) * 100))}%`
function barLabel(row: ProjectRow) {
  const text = row.overBudget ? copy.value.overLabel : copy.value.barLabel
  return text.replace('{name}', row.name).replace('{used}', formatHours(row.usedHours, props.locale))
    .replace('{budget}', formatHours(row.budgetHours ?? 0, props.locale)).replace('{percent}', String(percentUsed(row)))
}
const clientName = (row: ClientRow) => row.client ?? copy.value.noClient
</script>

<template>
  <section class="card usage" aria-labelledby="usage-heading">
    <div class="card-heading">
      <div><h2 id="usage-heading">{{ copy.title }}</h2><p class="muted usage-hint">{{ copy.hint }}</p></div>
    </div>

    <form class="usage-period" @submit.prevent>
      <label>{{ copy.period }}<AutoComplete v-model="mode" :label="copy.period" :options="modeChoices" required /></label>
      <template v-if="mode === 'month'">
        <label>{{ copy.month }}<AutoComplete v-model="monthNumber" :label="copy.month" :options="monthChoices" required /></label>
        <label>{{ copy.year }}<AutoComplete v-model="monthYear" :label="copy.year" :options="yearChoices" inputmode="numeric" required /></label>
      </template>
      <template v-else-if="mode === 'range'">
        <label>{{ copy.from }}<DateField v-model="rangeFrom" :label="copy.from" required /></label>
        <label>{{ copy.to }}<DateField v-model="rangeTo" :label="copy.to" required :min="rangeFrom" /></label>
      </template>
      <p v-if="rangeInvalid" class="usage-problem" role="alert">{{ copy.rangeOrder }}</p>
      <p v-else-if="periodLabel" class="usage-period-label">{{ periodLabel }}</p>
    </form>
    <p class="sr-only" role="status">{{ announcement }}</p>
    <p v-if="isLoading" class="muted" role="status">{{ copy.loading }}</p>

    <section class="usage-block" aria-labelledby="usage-projects-heading">
      <h3 id="usage-projects-heading">{{ copy.byProject }}</h3>
      <p v-if="usageError" class="usage-problem" role="alert">{{ usageError }} <button type="button" @click="refresh">{{ copy.retry }}</button></p>
      <p v-else-if="usage && !usage.projects.length" class="empty">{{ copy.empty }}</p>
      <div v-else-if="usage" class="table-wrap">
        <table class="usage-table projects-table">
          <caption class="sr-only">{{ copy.byProject }}</caption>
          <thead><tr>
            <th scope="col">{{ copy.project }}</th><th scope="col" class="num">{{ copy.hours }}</th><th scope="col" class="num">{{ copy.billableHours }}</th>
            <th scope="col" class="num">{{ copy.amount }}</th><th scope="col">{{ copy.budget }}</th><th scope="col"><span class="sr-only">{{ copy.members }}</span></th>
          </tr></thead>
          <tbody>
            <template v-for="row in usage.projects" :key="row.projectId">
              <tr>
                <td :data-label="copy.project" class="project-cell">
                  <strong>{{ row.name }}</strong><span v-if="row.isDeleted" class="chip chip-muted">{{ copy.deleted }}</span>
                  <small>{{ [row.client, row.isBillable ? copy.billable : copy.nonBillable].filter(Boolean).join(' · ') }}</small>
                </td>
                <td :data-label="copy.hours" class="num">{{ hoursText(row.periodMinutes) }}</td>
                <td :data-label="copy.billableHours" class="num">{{ hoursText(row.billableMinutes) }}</td>
                <td :data-label="copy.amount" class="num">
                  <template v-if="row.billableAmount != null">{{ money(row.billableAmount, row.currencyCode) }}</template>
                  <span v-else class="muted">{{ row.isBillable && row.billableMinutes ? copy.noRate : copy.noAmounts }}</span>
                </td>
                <td :data-label="copy.budget" class="budget-cell">
                  <template v-if="row.budgetHours != null">
                    <span class="budget-bar" :class="{ 'is-over': row.overBudget }" role="img" :aria-label="barLabel(row)"><span class="budget-fill" :style="{ width: barFill(row) }" /></span>
                    <span class="budget-text">{{ copy.usedOf.replace('{used}', formatHours(row.usedHours, locale)).replace('{budget}', formatHours(row.budgetHours, locale)) }}</span>
                    <strong v-if="row.overBudget" class="budget-over">{{ copy.overBy.replace('{hours}', formatHours(Math.abs(row.remainingHours ?? 0), locale)) }}</strong>
                    <small v-else>{{ copy.left.replace('{hours}', formatHours(row.remainingHours ?? 0, locale)) }}</small>
                  </template>
                  <template v-else><span class="muted">{{ copy.noBudget }}</span><small>{{ copy.usedOnly.replace('{used}', formatHours(row.usedHours, locale)) }}</small></template>
                </td>
                <td class="member-toggle">
                  <button type="button" :aria-expanded="openProjectId === row.projectId" :aria-controls="`usage-members-${row.projectId}`"
                    :aria-label="`${openProjectId === row.projectId ? copy.hideMembers : copy.showMembers}: ${row.name}`" @click="toggleMembers(row)">{{ openProjectId === row.projectId ? copy.hideMembers : copy.showMembers }}</button>
                </td>
              </tr>
              <tr v-if="openProjectId === row.projectId" :id="`usage-members-${row.projectId}`" class="members-row">
                <td colspan="6">
                  <p v-if="memberState.loading === row.projectId" class="muted" role="status">{{ copy.loading }}</p>
                  <p v-else-if="memberState.error" class="usage-problem" role="alert">{{ memberState.error }}</p>
                  <template v-else-if="memberState.data">
                    <p v-if="!sortedMembers.length" class="muted">{{ copy.noMembers }}</p>
                    <ul v-else class="member-usage" :aria-label="copy.memberHours.replace('{name}', row.name)">
                      <li v-for="member in sortedMembers" :key="member.ownerId"><span>{{ memberNames.get(member.ownerId) ?? copy.formerMember }}</span><strong>{{ hoursText(member.minutes) }}</strong></li>
                    </ul>
                  </template>
                </td>
              </tr>
            </template>
          </tbody>
          <tfoot><tr>
            <th scope="row" :data-label="copy.totals">{{ copy.totals }}</th>
            <td :data-label="copy.hours" class="num">{{ hoursText(usage.totals.periodMinutes) }}</td>
            <td :data-label="copy.billableHours" class="num">{{ hoursText(usage.totals.billableMinutes) }}</td>
            <td :data-label="copy.amount" class="num"><ul v-if="usage.totals.amounts.length" class="amounts"><li v-for="entry in usage.totals.amounts" :key="entry.currencyCode">{{ money(entry.amount, entry.currencyCode) }}</li></ul><span v-else class="muted">{{ copy.noAmounts }}</span></td>
            <td colspan="2" class="muted">{{ usage.unassignedMinutes ? copy.unassigned.replace('{hours}', hoursText(usage.unassignedMinutes)) : '' }}</td>
          </tr></tfoot>
        </table>
      </div>
    </section>

    <section class="usage-block" aria-labelledby="usage-clients-heading">
      <h3 id="usage-clients-heading">{{ copy.byClient }}</h3>
      <p v-if="clientError" class="usage-problem" role="alert">{{ clientError }} <button type="button" @click="refresh">{{ copy.retry }}</button></p>
      <p v-else-if="clientUsage && !clientUsage.clients.length" class="empty">{{ copy.empty }}</p>
      <div v-else-if="clientUsage" class="table-wrap">
        <table class="usage-table clients-table">
          <caption class="sr-only">{{ copy.byClient }}</caption>
          <thead><tr>
            <th scope="col">{{ copy.client }}</th><th scope="col" class="num">{{ copy.projects }}</th><th scope="col" class="num">{{ copy.hours }}</th>
            <th scope="col" class="num">{{ copy.billableHours }}</th><th scope="col" class="num">{{ copy.nonBillableHours }}</th><th scope="col" class="num">{{ copy.amount }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="row in clientUsage.clients" :key="row.client ?? '-'">
              <td :data-label="copy.client"><strong :class="{ muted: row.client == null }">{{ clientName(row) }}</strong></td>
              <td :data-label="copy.projects" class="num">{{ row.projectCount }}</td>
              <td :data-label="copy.hours" class="num">{{ hoursText(row.periodMinutes) }}</td>
              <td :data-label="copy.billableHours" class="num">{{ hoursText(row.billableMinutes) }}</td>
              <td :data-label="copy.nonBillableHours" class="num">{{ hoursText(row.nonBillableMinutes) }}</td>
              <td :data-label="copy.amount" class="num"><ul v-if="row.amounts.length" class="amounts"><li v-for="entry in row.amounts" :key="entry.currencyCode">{{ money(entry.amount, entry.currencyCode) }}</li></ul><span v-else class="muted">{{ copy.noAmounts }}</span></td>
            </tr>
          </tbody>
          <tfoot><tr>
            <th scope="row" :data-label="copy.totals">{{ copy.totals }}</th><td :data-label="copy.projects" class="num" />
            <td :data-label="copy.hours" class="num">{{ hoursText(clientUsage.totals.periodMinutes) }}</td>
            <td :data-label="copy.billableHours" class="num">{{ hoursText(clientUsage.totals.billableMinutes) }}</td>
            <td :data-label="copy.nonBillableHours" class="num">{{ hoursText(clientUsage.totals.periodMinutes - clientUsage.totals.billableMinutes) }}</td>
            <td :data-label="copy.amount" class="num"><ul v-if="clientUsage.totals.amounts.length" class="amounts"><li v-for="entry in clientUsage.totals.amounts" :key="entry.currencyCode">{{ money(entry.amount, entry.currencyCode) }}</li></ul><span v-else class="muted">{{ copy.noAmounts }}</span></td>
          </tr></tfoot>
        </table>
      </div>
    </section>

    <p class="muted usage-notes">{{ copy.notes }}</p>
  </section>
</template>

<style scoped>
.usage-hint { margin: .3rem 0 0; max-width: 44rem; }
.usage-period { align-items: end; display: flex; flex-wrap: wrap; gap: .75rem; margin-bottom: 1rem; }
.usage-period label { min-width: 10rem; }
.usage-period-label { color: var(--color-ink); font-weight: 600; margin: 0; padding-bottom: .5rem; }
.usage-problem { color: var(--color-danger); flex-basis: 100%; font-size: .9rem; margin: 0; }
.usage-problem button { margin-left: .5rem; }
.usage-block { margin-top: 1.25rem; }
.usage-block h3 { margin: 0 0 .6rem; }
.usage-table { min-width: 44rem; table-layout: auto; }
.usage-table .num { font-variant-numeric: tabular-nums; text-align: right; }
.usage-table tfoot th, .usage-table tfoot td { border-bottom: 0; border-top: 2px solid var(--color-border-strong); font-weight: 600; }
.project-cell strong { overflow-wrap: anywhere; }
.chip { border-radius: .35rem; font-size: .72rem; font-weight: 700; margin-left: .5rem; padding: .1rem .4rem; }
.chip-muted { background: var(--color-surface-muted); color: var(--color-muted); }
.budget-cell { min-width: 12rem; }
.budget-bar { background: var(--color-work-track); border-radius: .2rem; display: block; height: .6rem; overflow: hidden; width: 100%; }
.budget-fill { background: var(--color-brand); display: block; height: 100%; }
.budget-bar.is-over .budget-fill { background: var(--color-danger); }
.budget-text { display: block; font-size: .82rem; font-variant-numeric: tabular-nums; margin-top: .3rem; }
.budget-over { color: var(--color-danger); display: block; font-size: .82rem; }
.amounts { list-style: none; margin: 0; padding: 0; }
.member-toggle { text-align: right; }
.member-toggle button { font-size: .82rem; padding: .35rem .6rem; }
.members-row td { background: var(--color-surface-muted); }
.member-usage { display: grid; gap: .25rem; list-style: none; margin: 0; max-width: 28rem; padding: 0; }
.member-usage li { display: flex; gap: 1rem; justify-content: space-between; }
.member-usage span { overflow-wrap: anywhere; }
.usage-notes { font-size: .82rem; line-height: 1.5; margin-top: 1rem; max-width: 52rem; }
/* On a phone each row stacks into a labelled block instead of scrolling sideways. */
@media (max-width: 48rem) {
  .usage-table { min-width: 0; }
  .usage-table thead { height: 1px; overflow: hidden; position: absolute; width: 1px; clip: rect(0, 0, 0, 0); }
  .usage-table, .usage-table tbody, .usage-table tfoot, .usage-table tr, .usage-table td, .usage-table th { display: block; width: 100%; }
  .usage-table tr { border-bottom: 1px solid var(--color-border); padding: .6rem 0; }
  .usage-table td, .usage-table th { border: 0; padding: .2rem 0; }
  .usage-table td[data-label] { display: grid; gap: .5rem; grid-template-columns: minmax(6rem, 40%) minmax(0, 1fr); }
  .usage-table td[data-label]::before { color: var(--color-muted); content: attr(data-label); font-size: .78rem; font-weight: 600; }
  .usage-table .project-cell { display: block; }
  .usage-table .project-cell::before { display: none; }
  .usage-table .num { text-align: left; }
  .member-toggle { text-align: left; }
  .usage-table tfoot td[colspan] { display: block; }
}
</style>
