<script setup lang="ts">
import { useLocaleText } from '../../../../shared/i18n'
import type { MembershipStatus, PlatformAdminRosterEntry } from '../../composables/usePlatformApi'

const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', title: 'ผู้ดูแลระบบ',
    subtitle: 'เชิญ ปิดใช้งาน และเปิดใช้งานบัญชีผู้ดูแลระบบอีกครั้ง โดยไม่ต้อง deploy ใหม่',
    roster: 'รายชื่อผู้ดูแลระบบ', you: 'คุณ', since: 'เพิ่มเมื่อ',
    Active: 'ใช้งาน', PendingActivation: 'รอเปิดใช้งาน', Deactivated: 'ปิดใช้งาน',
    deactivate: 'ปิดใช้งาน', reactivate: 'เปิดใช้งานอีกครั้ง', resend: 'ส่งรหัสเปิดใช้งานใหม่',
    reason: 'เหตุผล (ไม่บังคับ)', confirmDeactivate: 'ปิดใช้งานผู้ดูแลระบบ {email}? เซสชันที่เปิดอยู่จะสิ้นสุดทันที',
    invite: 'เชิญผู้ดูแลระบบ', inviteHint: 'ผู้ได้รับเชิญจะได้รับอีเมลเปิดใช้งานเหมือนบัญชีผู้ดูแลระบบแรก บัญชีนี้ใช้กับองค์กรลูกค้าไม่ได้',
    email: 'อีเมล', sendInvite: 'ส่งคำเชิญ', invited: 'ส่งคำเชิญแล้ว', resent: 'ส่งรหัสเปิดใช้งานใหม่แล้ว',
    deactivated: 'ปิดใช้งานแล้ว', reactivated: 'เปิดใช้งานอีกครั้งแล้ว',
    mfaOn: 'ตั้งค่า MFA แล้ว', mfaOff: 'ยังไม่ตั้งค่า MFA', resetMfa: 'รีเซ็ต MFA', mfaReset: 'รีเซ็ต MFA แล้ว ผู้ดูแลคนนี้ต้องตั้งค่าแอปยืนยันตัวตนใหม่เมื่อเข้าสู่ระบบครั้งถัดไป',
    confirmResetMfa: 'รีเซ็ต MFA ของ {email}? ใช้เมื่อผู้ดูแลทำแอปยืนยันตัวตนหาย เซสชันที่เปิดอยู่จะสิ้นสุด',
    loading: 'กำลังโหลด…', loadError: 'โหลดรายชื่อไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
    CONFLICT: 'อีเมลนี้เป็นผู้ดูแลระบบอยู่แล้วหรือเป็นสมาชิกขององค์กรลูกค้า',
    CANNOT_CHANGE_SELF: 'คุณไม่สามารถปิดใช้งานบัญชีของตัวเองได้',
    LAST_ADMINISTRATOR: 'ต้องมีผู้ดูแลระบบที่ใช้งานอยู่อย่างน้อยหนึ่งคน',
    VALIDATION_FAILED: 'กรุณาตรวจสอบอีเมลและเหตุผล', failed: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', title: 'Platform admins',
    subtitle: 'Invite, deactivate, and reactivate system administrators without a redeploy.',
    roster: 'Roster', you: 'You', since: 'Added',
    Active: 'Active', PendingActivation: 'Pending activation', Deactivated: 'Deactivated',
    deactivate: 'Deactivate', reactivate: 'Reactivate', resend: 'Resend activation',
    reason: 'Reason (optional)', confirmDeactivate: 'Deactivate {email}? Their open sessions end immediately.',
    invite: 'Invite a platform admin', inviteHint: 'The invitee gets the same activation email as the first admin. The account cannot belong to a customer organization.',
    email: 'Email', sendInvite: 'Send invitation', invited: 'Invitation sent.', resent: 'Activation code re-sent.',
    deactivated: 'Deactivated.', reactivated: 'Reactivated.',
    mfaOn: 'MFA on', mfaOff: 'MFA not set up', resetMfa: 'Reset MFA', mfaReset: 'MFA reset. They set up an authenticator again at their next sign-in.',
    confirmResetMfa: 'Reset MFA for {email}? Use this when they lost their authenticator. Their open sessions end.',
    loading: 'Loading…', loadError: 'The roster could not be loaded. Please try again.',
    CONFLICT: 'This email is already a platform admin or belongs to a customer organization.',
    CANNOT_CHANGE_SELF: 'You cannot deactivate your own account.',
    LAST_ADMINISTRATOR: 'At least one active platform admin must remain.',
    VALIDATION_FAILED: 'Check the email and reason.', failed: 'The change could not be made. Please try again.'
  }
})
const { isPlatformAdmin, platformGet, platformSend, ready } = usePlatformApi()
const admins = ref<PlatformAdminRosterEntry[]>([])
const loading = ref(false)
const loadError = ref('')
const pending = ref('')
const notice = ref('')
const failure = ref('')
const inviteEmail = ref('')
const reasons = reactive<Record<string, string>>({})

const pillClass = (status: MembershipStatus) =>
  status === 'Active' ? 'is-ok' : status === 'Deactivated' ? 'is-off' : 'is-warn'
const formatDate = (value: string) =>
  new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium' }).format(new Date(value))
const displayName = (admin: PlatformAdminRosterEntry) => `${admin.firstName} ${admin.lastName}`.trim()

/** Loads the roster; refreshes after a change keep the current list on screen. */
async function load(refresh = false) {
  loading.value = !refresh
  loadError.value = ''
  try { admins.value = await platformGet<PlatformAdminRosterEntry[]>('/admins') }
  catch { loadError.value = text.value.loadError }
  finally { loading.value = false }
}

/** Runs one roster change and reports the outcome in the page's single live region. */
async function change(key: string, send: () => Promise<unknown>, success: string) {
  pending.value = key
  notice.value = ''
  failure.value = ''
  try {
    await send()
    delete reasons[key]
    notice.value = success
    await load(true)
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode ?? ''
    failure.value = (text.value as Record<string, string>)[code] ?? text.value.failed
  } finally { pending.value = '' }
}

function invite() {
  const email = inviteEmail.value.trim()
  return change('invite', async () => {
    await platformSend('/admins', { method: 'POST', body: { email } })
    inviteEmail.value = ''
  }, text.value.invited)
}

const resend = (admin: PlatformAdminRosterEntry) =>
  change(admin.email, () => platformSend('/admins', { method: 'POST', body: { email: admin.email } }), text.value.resent)

function setActive(admin: PlatformAdminRosterEntry, active: boolean) {
  if (!active && !window.confirm(text.value.confirmDeactivate.replace('{email}', admin.email))) return
  return change(admin.email, () => platformSend('/admins/status', {
    method: 'PUT', body: { email: admin.email, active, reason: reasons[admin.email]?.trim() || null }
  }), active ? text.value.reactivated : text.value.deactivated)
}

function resetMfa(admin: PlatformAdminRosterEntry) {
  if (!window.confirm(text.value.confirmResetMfa.replace('{email}', admin.email))) return
  return change(admin.email, () => platformSend('/admins/mfa-reset', {
    method: 'POST', body: { email: admin.email, reason: reasons[admin.email]?.trim() || null }
  }), text.value.mfaReset)
}

onMounted(async () => {
  if (await ready()) await load()
})
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <SysAdminRestricted v-if="!isPlatformAdmin" />
  <template v-else>
    <SysAdminNav />
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <p v-if="failure" class="notice notice-error" role="alert">{{ failure }}</p>
    <div class="sysadmin-columns">
      <section class="sysadmin-card" aria-labelledby="roster-heading">
        <h2 id="roster-heading">{{ text.roster }}</h2>
        <p v-if="loading" role="status">{{ text.loading }}</p>
        <p v-else-if="loadError" class="notice notice-error" role="alert">{{ loadError }}</p>
        <ul v-else class="audit-list" aria-labelledby="roster-heading">
          <li v-for="admin in admins" :key="admin.email" class="audit-row">
            <div class="audit-heading">
              <span class="sysadmin-person">
                <strong>{{ displayName(admin) || admin.email }}</strong>
                <small v-if="displayName(admin)">{{ admin.email }}</small>
                <small>{{ text.since }} {{ formatDate(admin.createdAtUtc) }}</small>
              </span>
              <span class="sysadmin-row-actions">
                <span v-if="admin.isCurrentUser" class="sysadmin-pill is-ok">{{ text.you }}</span>
                <span class="sysadmin-pill" :class="pillClass(admin.status)">{{ text[admin.status] }}</span>
                <span v-if="admin.status !== 'Deactivated'" class="sysadmin-pill" :class="admin.mfaEnabled ? 'is-ok' : 'is-warn'">
                  {{ admin.mfaEnabled ? text.mfaOn : text.mfaOff }}
                </span>
              </span>
            </div>
            <div v-if="!admin.isCurrentUser" class="sysadmin-row-actions">
              <input v-model="reasons[admin.email]" class="sysadmin-reason" :placeholder="text.reason" maxlength="500"
                :aria-label="`${text.reason} — ${admin.email}`" />
              <button v-if="admin.status === 'PendingActivation'" class="button button-secondary" type="button"
                :disabled="pending === admin.email" @click="resend(admin)">{{ text.resend }}</button>
              <button v-if="admin.mfaEnabled" class="button button-secondary" type="button"
                :disabled="pending === admin.email" @click="resetMfa(admin)">{{ text.resetMfa }}</button>
              <button v-if="admin.status === 'Deactivated'" class="button button-secondary" type="button"
                :disabled="pending === admin.email" @click="setActive(admin, true)">{{ text.reactivate }}</button>
              <button v-else class="button button-secondary" type="button"
                :disabled="pending === admin.email" @click="setActive(admin, false)">{{ text.deactivate }}</button>
            </div>
          </li>
        </ul>
      </section>
      <section class="sysadmin-card" aria-labelledby="invite-heading">
        <h2 id="invite-heading">{{ text.invite }}</h2>
        <p class="sysadmin-muted">{{ text.inviteHint }}</p>
        <form class="sysadmin-inline-form" @submit.prevent="invite">
          <label>{{ text.email }}<input v-model="inviteEmail" type="email" required maxlength="320" autocomplete="off" /></label>
          <button class="button" type="submit" :disabled="pending === 'invite'">{{ text.sendInvite }}</button>
        </form>
      </section>
    </div>
  </template>
</template>
