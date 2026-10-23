<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'
import { safeError, type Locale } from '../../../../shared/api-response'
import { formatDateTime, type Formats } from '../../../../shared/date-format'
import { useLocaleText } from '../../../../shared/i18n'
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import DateField from '../../../../shared/components/DateField.vue'

/**
 * The organization's member-management history for Tenant Admins: who changed what and when, newest
 * first, with an action and date filter and paging. The log is read from Identity the first time the
 * panel is shown (`active`), and again for every filter or page change.
 */
type AuditEntry = { id: string; createdAtUtc: string; action: string; actorEmail: string | null; beforeJson: string | null; afterJson: string | null }
type AuditPage = { totalCount: number; page: number; pageSize: number; items: AuditEntry[] }
const props = defineProps<{
  locale: Locale; formats: Formats; active: boolean
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const actions = [
  'member.invited', 'member.invitation_resent', 'member.invitation_revoked', 'member.role_changed',
  'member.deactivated', 'member.reactivated', 'member.activation_resent', 'member.mfa_reset',
  'member.group_created', 'member.group_renamed', 'member.group_deleted', 'member.group_changed'
] as const
const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'ประวัติการเปลี่ยนแปลงสมาชิก', hint: 'ดูว่าผู้ดูแลองค์กรคนใดเชิญ เปลี่ยนบทบาท หรือปิดการใช้งานสมาชิกเมื่อใด เรียงจากล่าสุด',
    filters: 'ตัวกรองประวัติ', action: 'การดำเนินการ', anyAction: 'ทุกการดำเนินการ', from: 'ตั้งแต่วันที่', to: 'ถึงวันที่',
    apply: 'ใช้ตัวกรอง', clear: 'ล้างตัวกรอง', loading: 'กำลังโหลดประวัติ…', retry: 'ลองโหลดอีกครั้ง',
    empty: 'ไม่มีรายการที่ตรงกับตัวกรอง', results: 'พบ {n} รายการ', unknownActor: 'ไม่ทราบบัญชี', by: 'โดย {email}',
    previous: 'ก่อนหน้า', next: 'ถัดไป', pagination: 'การแบ่งหน้า', rangeInvalid: 'วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด',
    list: 'รายการประวัติ',
    'member.invited': 'เชิญสมาชิก', 'member.invitation_resent': 'ส่งคำเชิญสมาชิกใหม่', 'member.invitation_revoked': 'ยกเลิกคำเชิญสมาชิก',
    'member.role_changed': 'เปลี่ยนบทบาทสมาชิก', 'member.deactivated': 'ปิดการใช้งานสมาชิก', 'member.reactivated': 'เปิดใช้งานสมาชิกอีกครั้ง',
    'member.activation_resent': 'ส่งรหัสเปิดใช้งานสมาชิกใหม่', 'member.mfa_reset': 'รีเซ็ต MFA ของสมาชิก',
    'member.group_created': 'เพิ่มกลุ่มสมาชิก', 'member.group_renamed': 'เปลี่ยนชื่อกลุ่มสมาชิก',
    'member.group_deleted': 'ลบกลุ่มสมาชิก', 'member.group_changed': 'ย้ายกลุ่มของสมาชิก',
    fieldEmail: 'อีเมล', fieldRole: 'บทบาท', fieldStatus: 'สถานะ', fieldName: 'ชื่อกลุ่ม', fieldGroup: 'กลุ่ม', fieldMfaEnabled: 'MFA',
    Employee: 'พนักงาน', TenantAdmin: 'ผู้ดูแลองค์กร', Active: 'ใช้งานอยู่', PendingActivation: 'รอเปิดใช้งาน', Deactivated: 'ปิดการใช้งาน',
    yes: 'เปิด', no: 'ปิด', none: 'ไม่มี'
  },
  en: {
    title: 'Member activity', hint: 'See when an organization administrator invited, changed the role of, or deactivated a member, newest first.',
    filters: 'Audit filters', action: 'Action', anyAction: 'Any action', from: 'From date', to: 'To date',
    apply: 'Apply filters', clear: 'Clear filters', loading: 'Loading the audit log…', retry: 'Try again',
    empty: 'No entries match these filters.', results: '{n} entries', unknownActor: 'Unknown account', by: 'by {email}',
    previous: 'Previous', next: 'Next', pagination: 'Pagination', rangeInvalid: 'The start date must not be after the end date.',
    list: 'Audit entries',
    'member.invited': 'Member invited', 'member.invitation_resent': 'Member invitation re-sent', 'member.invitation_revoked': 'Member invitation revoked',
    'member.role_changed': 'Member role changed', 'member.deactivated': 'Member deactivated', 'member.reactivated': 'Member reactivated',
    'member.activation_resent': 'Member activation re-sent', 'member.mfa_reset': 'Member MFA reset',
    'member.group_created': 'Member group added', 'member.group_renamed': 'Member group renamed',
    'member.group_deleted': 'Member group deleted', 'member.group_changed': 'Member moved to another group',
    fieldEmail: 'Email', fieldRole: 'Role', fieldStatus: 'Status', fieldName: 'Group name', fieldGroup: 'Group', fieldMfaEnabled: 'MFA',
    Employee: 'Employee', TenantAdmin: 'Organization admin', Active: 'Active', PendingActivation: 'Pending activation', Deactivated: 'Deactivated',
    yes: 'On', no: 'Off', none: 'None'
  }
})
const text = (key: string) => (copy.value as Record<string, string>)[key]
const pageSize = 25
const action = ref('')
const from = ref('')
const to = ref('')
const page = ref(1)
const result = ref<AuditPage | null>(null)
const loading = ref(false)
const error = ref('')
let started = false
const choices = computed(() => [{ value: '', label: copy.value.anyAction }, ...actions.map(item => ({ value: item, label: text(item)! }))])
const pageCount = computed(() => Math.max(1, Math.ceil((result.value?.totalCount ?? 0) / pageSize)))
const rangeInvalid = computed(() => Boolean(from.value && to.value && from.value > to.value))
// Date fields are local calendar days; the whole day is sent as a UTC range.
const startOfDay = (day: string) => new Date(`${day}T00:00:00`).toISOString()
const endOfDay = (day: string) => new Date(`${day}T23:59:59.999`).toISOString()
const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone

async function load() {
  loading.value = true
  error.value = ''
  try {
    const query = new URLSearchParams({ page: String(page.value), pageSize: String(pageSize) })
    if (action.value) query.set('action', action.value)
    if (from.value) query.set('fromUtc', startOfDay(from.value))
    if (to.value) query.set('toUtc', endOfDay(to.value))
    result.value = await props.api<AuditPage>(`/identity/members/audit?${query}`)
  } catch (cause) { error.value = safeError(cause, props.locale) }
  finally { loading.value = false }
}
function applyFilters() {
  if (rangeInvalid.value) return
  page.value = 1
  return load()
}
function clearFilters() {
  action.value = ''; from.value = ''; to.value = ''
  page.value = 1
  return load()
}
function goTo(target: number) {
  page.value = target
  return load()
}
watch(() => props.active, active => { if (active && !started) { started = true; void load() } }, { immediate: true })

function parse(json: string | null): Record<string, unknown> {
  if (!json) return {}
  try {
    const value = JSON.parse(json)
    return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
  } catch { return {} }
}
/** One field value in words: roles and statuses by name, a flag as on or off, a missing group as none. */
function show(key: string, value: unknown) {
  if (value === null || value === undefined || value === '') return text('none')!
  if (typeof value === 'boolean') return value ? text('yes')! : text('no')!
  return (key === 'Role' || key === 'Status') && text(String(value)) ? text(String(value))! : String(value)
}
/** The fields an entry touched as "Label: before → after", or "Label: value" where nothing changed. */
function summary(entry: AuditEntry) {
  const before = parse(entry.beforeJson)
  const after = parse(entry.afterJson)
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].map(key => {
    const label = text(`field${key}`) ?? key
    const changed = entry.beforeJson && entry.afterJson && key in before && before[key] !== after[key]
    return changed ? `${label}: ${show(key, before[key])} → ${show(key, after[key])}` : `${label}: ${show(key, after[key] ?? before[key])}`
  })
}
const rows = computed(() => (result.value?.items ?? []).map(entry => ({
  ...entry,
  label: text(entry.action) ?? entry.action,
  when: formatDateTime(entry.createdAtUtc, timeZone, props.formats, props.locale),
  lines: summary(entry)
})))
</script>

<template>
  <section class="panel audit-panel" aria-labelledby="audit-heading">
    <div class="panel-heading"><h2 id="audit-heading">{{ copy.title }}</h2><p>{{ copy.hint }}</p></div>
    <form class="audit-filters" :aria-label="copy.filters" @submit.prevent="applyFilters">
      <label>{{ copy.action }}<AutoComplete v-model="action" :label="copy.action" :options="choices" :locale="locale" /></label>
      <label>{{ copy.from }}<DateField v-model="from" :label="copy.from" :locale="locale" :max="to || undefined" /></label>
      <label>{{ copy.to }}<DateField v-model="to" :label="copy.to" :locale="locale" :min="from || undefined" /></label>
      <p v-if="rangeInvalid" class="field-error" role="alert">{{ copy.rangeInvalid }}</p>
      <div class="row-actions">
        <button class="button button-primary" type="submit" :disabled="loading || rangeInvalid">{{ copy.apply }}</button>
        <button class="button button-subtle" type="button" :disabled="loading" @click="clearFilters">{{ copy.clear }}</button>
      </div>
    </form>

    <p v-if="error" class="feedback feedback-error" role="alert"><span>{{ error }}</span><button class="button button-subtle" type="button" @click="load">{{ copy.retry }}</button></p>
    <p v-else-if="loading" class="loading-line" role="status">{{ copy.loading }}</p>
    <template v-else-if="result">
      <p class="audit-count">{{ copy.results.replace('{n}', String(result.totalCount)) }}</p>
      <p v-if="!rows.length" class="empty-list">{{ copy.empty }}</p>
      <ol v-else class="audit-list" :aria-label="copy.list">
        <li v-for="row in rows" :key="row.id" class="audit-row">
          <div class="audit-heading"><strong>{{ row.label }}</strong><time :datetime="row.createdAtUtc">{{ row.when }}</time></div>
          <p class="audit-actor">{{ copy.by.replace('{email}', row.actorEmail ?? copy.unknownActor) }}</p>
          <ul v-if="row.lines.length" class="audit-changes"><li v-for="line in row.lines" :key="line">{{ line }}</li></ul>
        </li>
      </ol>
      <nav v-if="pageCount > 1" class="audit-pagination" :aria-label="copy.pagination">
        <button class="button button-subtle" type="button" :disabled="page <= 1" @click="goTo(page - 1)">{{ copy.previous }}</button>
        <span>{{ page }} / {{ pageCount }}</span>
        <button class="button button-subtle" type="button" :disabled="page >= pageCount" @click="goTo(page + 1)">{{ copy.next }}</button>
      </nav>
    </template>
  </section>
</template>

<style scoped>
.audit-filters { align-items: end; display: grid; gap: .75rem 1rem; grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr)); margin-bottom: 1rem; }
.audit-filters label { color: var(--color-ink-soft); display: grid; font-size: .85rem; font-weight: 600; gap: .35rem; min-width: 0; }
.audit-filters .field-error, .audit-filters .row-actions { grid-column: 1 / -1; }
.audit-count { color: var(--color-muted); font-size: .85rem; margin: 0 0 .5rem; }
.audit-list { display: grid; gap: .6rem; list-style: none; margin: 0; padding: 0; }
.audit-row { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .6rem; display: grid; gap: .3rem; padding: .75rem .9rem; }
.audit-heading { align-items: baseline; display: flex; flex-wrap: wrap; gap: .25rem .75rem; justify-content: space-between; }
.audit-heading strong { overflow-wrap: anywhere; }
.audit-heading time { color: var(--color-muted); font-size: .85rem; font-variant-numeric: tabular-nums; }
.audit-actor { color: var(--color-ink-soft); font-size: .85rem; margin: 0; overflow-wrap: anywhere; }
.audit-changes { color: var(--color-ink-soft); display: grid; font-size: .85rem; gap: .15rem; list-style: none; margin: .15rem 0 0; padding: 0; }
.audit-changes li { overflow-wrap: anywhere; }
.audit-pagination { align-items: center; display: flex; flex-wrap: wrap; gap: .75rem; justify-content: center; margin-top: 1rem; }
</style>
