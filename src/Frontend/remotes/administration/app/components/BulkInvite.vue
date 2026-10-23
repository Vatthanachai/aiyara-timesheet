<script setup lang="ts">
import { computed, nextTick, ref, toRef } from 'vue'
import { safeError, type Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'
import { MAX_BULK_ROWS, bulkBody, parseInviteRows, toCsv, type GroupRef, type ParsedRow } from '../utils/bulkInvite'

/**
 * "Invite several": paste addresses or load a CSV, check the rows in the browser, send them in one request, and
 * read what happened to each row. The request is made here; the page is told when invitations were created
 * (`sent`) or the panel is dismissed (`close`).
 */
const props = defineProps<{
  locale: Locale; groups: GroupRef[]
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const emit = defineEmits<{ sent: []; close: [] }>()
type RowResult = { index: number; email: string; status: 'Created' | 'Skipped'; reason: string | null; invitationId: string | null; emailSent: boolean }
type BulkResult = { createdCount: number; skippedCount: number; rows: RowResult[] }

const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'เชิญหลายคน', hint: 'วางรายชื่อหรืออัปโหลดไฟล์ CSV คอลัมน์ email, role, group (แถวหัวตารางไม่บังคับ) ได้ครั้งละไม่เกิน {max} แถว บทบาทที่เว้นว่างคือพนักงาน',
    input: 'วางอีเมลหรือข้อมูล CSV', placeholder: 'ada@example.com\nbo@example.com,Employee,Sales\ncy@example.com;TenantAdmin', upload: 'อัปโหลดไฟล์ CSV',
    preview: 'ตัวอย่างก่อนส่ง', rowCount: '{count} แถว', problems: 'พบปัญหา {count} แถว', removeProblems: 'ลบแถวที่มีปัญหา',
    email: 'อีเมล', role: 'บทบาท', group: 'กลุ่ม', status: 'สถานะ', line: 'แถว', noGroup: 'ไม่มีกลุ่ม', ready: 'พร้อมส่ง',
    roleEmployee: 'พนักงาน', roleTenantAdmin: 'ผู้ดูแลองค์กร',
    invalidEmail: 'อีเมลไม่ถูกต้อง', duplicate: 'อีเมลซ้ำในรายการ', invalidRole: 'บทบาทไม่ถูกต้อง', unknownGroup: 'ไม่พบกลุ่มนี้',
    tooMany: 'มี {count} แถว เกินขีดจำกัด {max} แถวต่อครั้ง กรุณาแบ่งส่งหลายครั้ง', fixFirst: 'แก้หรือลบแถวที่มีปัญหาก่อนส่ง',
    fileTooLarge: 'ไฟล์ใหญ่เกินไป (ไม่เกิน 1 MB)', fileFailed: 'อ่านไฟล์ไม่สำเร็จ',
    send: 'ส่งคำเชิญ {count} คน', sending: 'กำลังส่ง…', cancel: 'ยกเลิก', close: 'ปิด', clear: 'ล้าง',
    validation: 'ส่งได้ครั้งละ 1–{max} แถว', done: 'ผลการส่งคำเชิญ', created: 'ส่งคำเชิญแล้ว {count}', skipped: 'ข้าม {count}',
    resultCreated: 'ส่งแล้ว', resultSkipped: 'ข้าม', emailNotSent: 'สร้างคำเชิญแล้วแต่ส่งอีเมลไม่สำเร็จ ให้กดส่งใหม่ในรายการคำเชิญที่รอตอบรับ',
    reason: 'เหตุผล', copySkipped: 'คัดลอกแถวที่ข้าม (CSV)', downloadSkipped: 'ดาวน์โหลดแถวที่ข้าม (CSV)', retrySkipped: 'แก้แถวที่ข้ามและลองใหม่',
    copied: 'คัดลอกแล้ว', copyFailed: 'คัดลอกไม่สำเร็จ ให้ใช้ปุ่มดาวน์โหลดแทน', another: 'เชิญเพิ่ม',
    reasonInvalidEmail: 'อีเมลไม่ถูกต้อง', reasonDuplicateInBatch: 'อีเมลซ้ำในรายการนี้', reasonInvalidRole: 'บทบาทไม่ถูกต้อง', reasonUnknownGroup: 'ไม่พบกลุ่มนี้',
    reasonNotInvitable: 'เชิญอีเมลนี้ไม่ได้', reasonAlreadyMember: 'เป็นสมาชิกขององค์กรนี้อยู่แล้ว', reasonAlreadyInvited: 'มีคำเชิญที่รอตอบรับอยู่แล้ว'
  },
  en: {
    title: 'Invite several', hint: 'Paste addresses or upload a CSV with the columns email, role, group (the header row is optional), up to {max} rows at a time. An empty role means Employee.',
    input: 'Paste emails or CSV data', placeholder: 'ada@example.com\nbo@example.com,Employee,Sales\ncy@example.com;TenantAdmin', upload: 'Upload CSV file',
    preview: 'Preview before sending', rowCount: '{count} rows', problems: '{count} rows have problems', removeProblems: 'Remove rows with problems',
    email: 'Email', role: 'Role', group: 'Group', status: 'Status', line: 'Row', noGroup: 'No group', ready: 'Ready',
    roleEmployee: 'Employee', roleTenantAdmin: 'Organization admin',
    invalidEmail: 'Not a valid email', duplicate: 'Repeated in this list', invalidRole: 'Unknown role', unknownGroup: 'No group with this name',
    tooMany: 'There are {count} rows, over the limit of {max} per batch. Split them into smaller batches.', fixFirst: 'Fix or remove the rows with problems before sending.',
    fileTooLarge: 'The file is too large (1 MB at most).', fileFailed: 'Could not read the file.',
    send: 'Send {count} invitations', sending: 'Sending…', cancel: 'Cancel', close: 'Close', clear: 'Clear',
    validation: 'A batch holds 1 to {max} rows.', done: 'Invitation results', created: '{count} invited', skipped: '{count} skipped',
    resultCreated: 'Invited', resultSkipped: 'Skipped', emailNotSent: 'The invitation was created but its email did not go out. Resend it from the pending invitations list.',
    reason: 'Reason', copySkipped: 'Copy skipped rows (CSV)', downloadSkipped: 'Download skipped rows (CSV)', retrySkipped: 'Fix skipped rows and retry',
    copied: 'Copied.', copyFailed: 'Could not copy. Use the download button instead.', another: 'Invite more',
    reasonInvalidEmail: 'Not a valid email', reasonDuplicateInBatch: 'Repeated in this batch', reasonInvalidRole: 'Unknown role', reasonUnknownGroup: 'No group with this name',
    reasonNotInvitable: 'This email cannot be invited', reasonAlreadyMember: 'Already a member of this organization', reasonAlreadyInvited: 'An invitation is already pending'
  }
})

const text = ref('')
const fileName = ref('')
const isSending = ref(false)
const error = ref('')
const notice = ref('')
const result = ref<BulkResult | null>(null)
/** The rows that were sent, in request order, to pair each result with its row. */
const sentRows = ref<ParsedRow[]>([])
const textArea = ref<HTMLTextAreaElement | null>(null)
const resultHeading = ref<HTMLElement | null>(null)

const rows = computed(() => parseInviteRows(text.value, props.groups))
const problemCount = computed(() => rows.value.filter(row => row.problem).length)
const tooMany = computed(() => rows.value.length > MAX_BULK_ROWS)
const canSend = computed(() => rows.value.length > 0 && !tooMany.value && problemCount.value === 0 && !isSending.value)
const roleLabel = (row: ParsedRow) => row.role === 'TenantAdmin' ? copy.value.roleTenantAdmin : copy.value.roleEmployee
const problemLabel = (row: ParsedRow) => row.problem ? copy.value[row.problem] as string : copy.value.ready
const reasonLabel = (reason: string | null) => (reason && (copy.value as Record<string, string>)[`reason${reason}`]) || reason || ''
const fill = (template: string, values: Record<string, string | number>) =>
  Object.entries(values).reduce((out, [key, value]) => out.replace(`{${key}}`, String(value)), template)

async function readFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  error.value = ''
  notice.value = ''
  if (file.size > 1024 * 1024) { error.value = copy.value.fileTooLarge; return }
  try {
    text.value = await file.text()
    fileName.value = file.name
  } catch { error.value = copy.value.fileFailed }
}
function removeProblems() {
  const good = rows.value.filter(row => !row.problem)
  text.value = good.length ? toCsv(['email', 'role', 'group'], good.map(row => [row.email, row.roleText, row.groupText])) : ''
}
function clear() { text.value = ''; fileName.value = ''; error.value = ''; notice.value = ''; void nextTick(() => textArea.value?.focus()) }

async function send() {
  if (!canSend.value) return
  isSending.value = true
  error.value = ''
  notice.value = ''
  const batch = rows.value
  try {
    const response = await props.api<BulkResult>('/identity/members/invitations/bulk', { method: 'POST', body: bulkBody(batch) })
    sentRows.value = batch
    result.value = response
    if (response.createdCount > 0) emit('sent')
    await nextTick()
    resultHeading.value?.focus()
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'VALIDATION_FAILED' ? fill(copy.value.validation, { max: MAX_BULK_ROWS }) : safeError(cause, props.locale)
  } finally { isSending.value = false }
}

const skipped = computed(() => (result.value?.rows ?? []).filter(row => row.status === 'Skipped'))
/** The sent row behind a result, by its position in the request. */
const sentRow = (item: RowResult) => sentRows.value[item.index]
const skippedCsv = (withReason: boolean) => toCsv(withReason ? ['email', 'role', 'group', 'reason'] : ['email', 'role', 'group'],
  skipped.value.map(item => {
    const row = sentRow(item)
    const base = [item.email, row?.roleText ?? '', row?.groupText ?? '']
    return withReason ? [...base, item.reason ?? ''] : base
  }))

async function copySkipped() {
  notice.value = ''
  const csv = skippedCsv(true)
  try {
    await navigator.clipboard.writeText(csv)
    notice.value = copy.value.copied
    return
  } catch { /* the page is in an iframe without clipboard permission: fall back below */ }
  const holder = document.createElement('textarea')
  holder.value = csv
  holder.setAttribute('readonly', '')
  holder.style.cssText = 'position:fixed;top:0;left:0;opacity:0'
  document.body.appendChild(holder)
  holder.select()
  try { notice.value = document.execCommand('copy') ? copy.value.copied : copy.value.copyFailed }
  catch { notice.value = copy.value.copyFailed }
  holder.remove()
}
function downloadSkipped() {
  const url = URL.createObjectURL(new Blob(['﻿', skippedCsv(true)], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'skipped-invitations.csv'
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
function retrySkipped() {
  const csv = skippedCsv(false)
  result.value = null
  sentRows.value = []
  fileName.value = ''
  notice.value = ''
  text.value = csv
  void nextTick(() => textArea.value?.focus())
}
function again() {
  result.value = null
  sentRows.value = []
  clear()
}
</script>

<template>
  <section class="bulk" :aria-labelledby="'bulk-invite-heading'" @keydown.esc="emit('close')">
    <div class="bulk-head">
      <h3 id="bulk-invite-heading">{{ copy.title }}</h3>
      <button class="button button-subtle" type="button" @click="emit('close')">{{ copy.close }}</button>
    </div>

    <template v-if="!result">
      <p class="bulk-hint">{{ fill(copy.hint, { max: MAX_BULK_ROWS }) }}</p>
      <label class="bulk-field" for="bulk-invite-input"><span>{{ copy.input }}</span></label>
      <textarea id="bulk-invite-input" ref="textArea" v-model="text" rows="6" spellcheck="false" autocomplete="off" :placeholder="copy.placeholder" :disabled="isSending"></textarea>
      <div class="bulk-actions">
        <label class="button button-subtle file-button">{{ copy.upload }}<input class="sr-only" type="file" accept=".csv,.txt,text/csv,text/plain" :disabled="isSending" @change="readFile" /></label>
        <span v-if="fileName" class="bulk-file">{{ fileName }}</span>
        <button v-if="text" class="button button-subtle" type="button" :disabled="isSending" @click="clear">{{ copy.clear }}</button>
      </div>

      <p v-if="tooMany" class="feedback feedback-error" role="alert">{{ fill(copy.tooMany, { count: rows.length, max: MAX_BULK_ROWS }) }}</p>
      <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
      <p v-if="notice" class="feedback feedback-success" role="status">{{ notice }}</p>

      <div v-if="rows.length" class="bulk-preview">
        <div class="bulk-summary" aria-live="polite">
          <strong>{{ copy.preview }}</strong>
          <span>{{ fill(copy.rowCount, { count: rows.length }) }}</span>
          <span v-if="problemCount" class="bulk-problem-count">{{ fill(copy.problems, { count: problemCount }) }}</span>
          <button v-if="problemCount" class="button button-subtle" type="button" :disabled="isSending" @click="removeProblems">{{ copy.removeProblems }}</button>
        </div>
        <table class="bulk-table" :aria-label="copy.preview">
          <thead><tr><th scope="col">{{ copy.line }}</th><th scope="col">{{ copy.email }}</th><th scope="col">{{ copy.role }}</th><th scope="col">{{ copy.group }}</th><th scope="col">{{ copy.status }}</th></tr></thead>
          <tbody>
            <tr v-for="row in rows" :key="row.line" :class="{ 'bulk-bad': row.problem }">
              <td :data-label="copy.line">{{ row.line }}</td>
              <td :data-label="copy.email" class="bulk-email">{{ row.email || '—' }}</td>
              <td :data-label="copy.role">{{ row.role ? roleLabel(row) : row.roleText }}</td>
              <td :data-label="copy.group">{{ row.groupText || copy.noGroup }}</td>
              <td :data-label="copy.status"><span class="bulk-status" :class="row.problem ? 'bulk-status-bad' : 'bulk-status-ok'">{{ problemLabel(row) }}</span></td>
            </tr>
          </tbody>
        </table>
        <p v-if="problemCount && !tooMany" class="bulk-blocked">{{ copy.fixFirst }}</p>
      </div>

      <div class="bulk-actions">
        <button class="button button-primary" type="button" :disabled="!canSend" @click="send">{{ isSending ? copy.sending : fill(copy.send, { count: rows.length }) }}</button>
        <button class="button button-subtle" type="button" :disabled="isSending" @click="emit('close')">{{ copy.cancel }}</button>
      </div>
    </template>

    <template v-else>
      <h4 id="bulk-result-heading" ref="resultHeading" tabindex="-1">{{ copy.done }}</h4>
      <p class="bulk-counts" role="status">
        <span class="bulk-status bulk-status-ok">{{ fill(copy.created, { count: result.createdCount }) }}</span>
        <span class="bulk-status" :class="result.skippedCount ? 'bulk-status-bad' : 'bulk-status-ok'">{{ fill(copy.skipped, { count: result.skippedCount }) }}</span>
      </p>
      <table class="bulk-table" :aria-label="copy.done">
        <thead><tr><th scope="col">{{ copy.email }}</th><th scope="col">{{ copy.status }}</th><th scope="col">{{ copy.reason }}</th></tr></thead>
        <tbody>
          <tr v-for="item in result.rows" :key="item.index" :class="{ 'bulk-bad': item.status === 'Skipped' }">
            <td :data-label="copy.email" class="bulk-email">{{ item.email }}</td>
            <td :data-label="copy.status"><span class="bulk-status" :class="item.status === 'Created' ? 'bulk-status-ok' : 'bulk-status-bad'">{{ item.status === 'Created' ? copy.resultCreated : copy.resultSkipped }}</span></td>
            <td :data-label="copy.reason">{{ item.status === 'Skipped' ? reasonLabel(item.reason) : item.emailSent ? '' : copy.emailNotSent }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="notice" class="feedback feedback-success" role="status">{{ notice }}</p>
      <div class="bulk-actions">
        <template v-if="skipped.length">
          <button class="button button-primary" type="button" @click="retrySkipped">{{ copy.retrySkipped }}</button>
          <button class="button button-subtle" type="button" @click="copySkipped">{{ copy.copySkipped }}</button>
          <button class="button button-subtle" type="button" @click="downloadSkipped">{{ copy.downloadSkipped }}</button>
        </template>
        <button class="button button-subtle" type="button" @click="again">{{ copy.another }}</button>
        <button class="button button-subtle" type="button" @click="emit('close')">{{ copy.close }}</button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.bulk { background: var(--color-surface-muted); border: 1px solid var(--color-border); border-radius: .6rem; display: grid; gap: .65rem; margin-top: 1rem; min-width: 0; padding: 1rem; }
.bulk-head { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem; justify-content: space-between; }
.bulk-head h3, .bulk h4 { font-size: 1.05rem; margin: 0; }
.bulk h4:focus { outline: none; }
.bulk-hint { color: var(--color-muted); line-height: 1.5; margin: 0; }
.bulk-field span { font-size: .86rem; font-weight: 600; }
.bulk textarea { background: var(--color-field); border: 1px solid var(--color-border-strong); border-radius: .5rem; box-sizing: border-box; color: var(--color-ink); font: inherit; max-width: 100%; min-height: 8rem; padding: .55rem .7rem; resize: vertical; width: 100%; }
.bulk-actions { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem; }
.bulk-file { overflow-wrap: anywhere; font-size: .86rem; }
.bulk-summary { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem .9rem; margin-bottom: .4rem; }
.bulk-problem-count, .bulk-blocked { color: var(--color-danger); font-weight: 600; }
.bulk-blocked { margin: .4rem 0 0; }
.bulk-counts { display: flex; flex-wrap: wrap; gap: .5rem; margin: 0; }
.bulk-table { border-collapse: collapse; width: 100%; }
.bulk-table th, .bulk-table td { border-top: 1px solid var(--color-border); padding: .45rem .5rem; text-align: left; vertical-align: top; }
.bulk-table th { font-size: .8rem; }
.bulk-email { overflow-wrap: anywhere; }
.bulk-bad td { background: var(--color-warn-soft); }
.bulk-status { border-radius: 999px; display: inline-block; font-size: .82rem; font-weight: 600; padding: .1rem .55rem; }
.bulk-status-ok { background: var(--color-success-soft); color: var(--color-success); }
.bulk-status-bad { background: var(--color-warn-soft); color: var(--color-warn); }
@media (max-width: 640px) {
  .bulk { padding: .75rem; }
  .bulk-table thead { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
  .bulk-table, .bulk-table tbody, .bulk-table tr, .bulk-table td { display: block; width: 100%; box-sizing: border-box; }
  .bulk-table tr { border-top: 1px solid var(--color-border); padding: .4rem 0; }
  .bulk-table td { border: 0; display: grid; gap: .5rem; grid-template-columns: 5rem minmax(0, 1fr); padding: .15rem .25rem; overflow-wrap: anywhere; }
  .bulk-table td::before { color: var(--color-muted); content: attr(data-label); font-size: .78rem; font-weight: 600; }
  .bulk-actions .button { max-width: 100%; white-space: normal; }
}
</style>
