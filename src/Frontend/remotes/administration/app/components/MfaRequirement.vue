<script setup lang="ts">
import { nextTick, ref, toRef } from 'vue'
import { safeError, type Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'

/**
 * The organization setting "Require MFA for all members". Turning it on signs out members without an
 * authenticator, so both directions go through a confirmation that says so, with an optional reason
 * for the audit trail. The request is made here; the page is told the new value.
 */
const props = defineProps<{
  locale: Locale; required: boolean
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const emit = defineEmits<{ changed: [required: boolean] }>()
const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'การยืนยันตัวตนสองขั้นตอน (MFA)', hint: 'กำหนดให้สมาชิกทุกคนในองค์กรใช้รหัสจากแอปยืนยันตัวตนทุกครั้งที่เข้าสู่ระบบ',
    switchLabel: 'บังคับใช้ MFA กับสมาชิกทุกคน', on: 'เปิดอยู่', off: 'ปิดอยู่',
    enableTitle: 'เปิดการบังคับใช้ MFA',
    enableWarning: 'สมาชิกที่ยังไม่มีแอปยืนยันตัวตนจะถูกออกจากระบบทันที และต้องตั้งค่าแอปเมื่อเข้าสู่ระบบครั้งต่อไป สมาชิกที่ตั้งค่าไว้แล้วจะไม่ถูกออกจากระบบ คุณต้องตั้งค่าแอปของตัวเองไว้ก่อน',
    disableTitle: 'ยกเลิกการบังคับใช้ MFA', disableWarning: 'สมาชิกจะเข้าสู่ระบบด้วยรหัสผ่านอย่างเดียวได้ ยกเว้นผู้ที่เปิดใช้ MFA ไว้เอง ซึ่งยังต้องกรอกรหัส',
    reason: 'เหตุผล (ไม่บังคับ)', reasonHint: 'บันทึกไว้ในประวัติการดำเนินการ ไม่เกิน 500 ตัวอักษร',
    confirmEnable: 'เปิดและออกจากระบบสมาชิกที่ยังไม่ตั้งค่า', confirmDisable: 'ยกเลิกการบังคับใช้', cancel: 'ยกเลิก', saving: 'กำลังบันทึก…',
    enabledNotice: 'เปิดการบังคับใช้ MFA แล้ว', disabledNotice: 'ยกเลิกการบังคับใช้ MFA แล้ว',
    adminNotEnrolled: 'คุณต้องตั้งค่าแอปยืนยันตัวตนของตัวเองก่อน เปิดโปรไฟล์จากเมนูมุมขวาบน ตั้งค่า "การยืนยันตัวตนสองขั้นตอน" แล้วกลับมาที่นี่'
  },
  en: {
    title: 'Multi-factor authentication (MFA)', hint: 'Require every member of the organization to enter a code from an authenticator app each time they sign in.',
    switchLabel: 'Require MFA for all members', on: 'On', off: 'Off',
    enableTitle: 'Turn on the MFA requirement',
    enableWarning: 'Members who have no authenticator app are signed out now and must set one up the next time they sign in. Members who already have one stay signed in. You must have set up your own authenticator first.',
    disableTitle: 'Stop requiring MFA', disableWarning: 'Members can sign in with a password alone again, except those who turned MFA on themselves; they still enter a code.',
    reason: 'Reason (optional)', reasonHint: 'Kept in the audit trail. Up to 500 characters.',
    confirmEnable: 'Turn on and sign out members without MFA', confirmDisable: 'Stop requiring MFA', cancel: 'Cancel', saving: 'Saving…',
    enabledNotice: 'MFA is now required for all members.', disabledNotice: 'MFA is no longer required.',
    adminNotEnrolled: 'Set up your own authenticator first. Open your profile from the menu at the top right, turn on two-step verification, then come back here.'
  }
})
const confirming = ref(false)
const reason = ref('')
const pending = ref(false)
const error = ref('')
const notice = ref('')
const toggle = ref<HTMLButtonElement | null>(null)
const reasonInput = ref<HTMLTextAreaElement | null>(null)

async function open() {
  notice.value = ''
  error.value = ''
  reason.value = ''
  confirming.value = true
  await nextTick()
  reasonInput.value?.focus()
}
async function close() {
  confirming.value = false
  await nextTick()
  toggle.value?.focus()
}
async function confirm() {
  pending.value = true
  error.value = ''
  const next = !props.required
  try {
    await props.api<{ requireMfa: boolean }>('/identity/organization/mfa', {
      method: 'PUT', body: { required: next, reason: reason.value.trim() || undefined }
    })
    emit('changed', next)
    notice.value = next ? copy.value.enabledNotice : copy.value.disabledNotice
    // The switch is disabled while a request runs, so it can take focus back only after that ends.
    pending.value = false
    await close()
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'MFA_NOT_ENROLLED' ? copy.value.adminNotEnrolled : safeError(cause, props.locale)
  } finally { pending.value = false }
}
</script>

<template>
  <section class="panel mfa-panel" aria-labelledby="mfa-requirement-heading">
    <div class="panel-heading"><h2 id="mfa-requirement-heading">{{ copy.title }}</h2><p>{{ copy.hint }}</p></div>
    <div class="mfa-switch-row">
      <span id="mfa-switch-label" class="mfa-switch-text">{{ copy.switchLabel }}</span>
      <button ref="toggle" type="button" class="mfa-switch" role="switch" :aria-checked="required" aria-labelledby="mfa-switch-label"
        :disabled="pending" @click="confirming ? close() : open()">
        <span class="mfa-switch-track" aria-hidden="true"><span class="mfa-switch-thumb"></span></span>
        <span class="mfa-switch-state">{{ required ? copy.on : copy.off }}</span>
      </button>
    </div>
    <p v-if="notice" class="feedback feedback-success" role="status">{{ notice }}</p>
    <form v-if="confirming" class="mfa-confirm" aria-labelledby="mfa-confirm-heading" @submit.prevent="confirm" @keydown.esc="close">
      <h3 id="mfa-confirm-heading">{{ required ? copy.disableTitle : copy.enableTitle }}</h3>
      <p class="mfa-warning">{{ required ? copy.disableWarning : copy.enableWarning }}</p>
      <label for="mfa-reason"><span>{{ copy.reason }}</span></label>
      <textarea id="mfa-reason" ref="reasonInput" v-model="reason" maxlength="500" rows="2" aria-describedby="mfa-reason-hint"></textarea>
      <small id="mfa-reason-hint">{{ copy.reasonHint }}</small>
      <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
      <div class="row-actions">
        <button class="button" :class="required ? 'button-primary' : 'button-warning'" type="submit" :disabled="pending">{{ pending ? copy.saving : (required ? copy.confirmDisable : copy.confirmEnable) }}</button>
        <button class="button button-subtle" type="button" :disabled="pending" @click="close">{{ copy.cancel }}</button>
      </div>
    </form>
  </section>
</template>

<style scoped>
.mfa-switch-row { align-items: center; display: flex; flex-wrap: wrap; gap: .75rem 1.25rem; justify-content: space-between; }
.mfa-switch-text { font-weight: 600; }
.mfa-switch { align-items: center; background: none; border: 0; color: var(--color-ink); cursor: pointer; display: inline-flex; gap: .6rem; min-height: 2.75rem; padding: 0; }
.mfa-switch:disabled { cursor: not-allowed; opacity: .6; }
.mfa-switch-track { background: var(--color-border-strong); border-radius: 999px; display: inline-block; height: 1.5rem; position: relative; transition: background .15s; width: 2.75rem; }
.mfa-switch-thumb { background: #fff; border-radius: 50%; box-shadow: 0 1px 3px rgb(0 0 0 / 35%); height: 1.1rem; left: .2rem; position: absolute; top: .2rem; transition: transform .15s; width: 1.1rem; }
.mfa-switch[aria-checked="true"] .mfa-switch-track { background: var(--color-brand); }
.mfa-switch[aria-checked="true"] .mfa-switch-thumb { transform: translateX(1.25rem); }
.mfa-switch-state { font-weight: 600; min-width: 3.5rem; text-align: left; }
@media (prefers-reduced-motion: reduce) { .mfa-switch-track, .mfa-switch-thumb { transition: none; } }
.mfa-confirm { background: var(--color-warn-soft); border: 1px solid var(--color-warn-line); border-radius: .6rem; display: grid; gap: .5rem; margin-top: 1rem; padding: 1rem; }
.mfa-confirm h3 { font-size: 1rem; margin: 0; }
.mfa-warning { color: var(--color-warn); font-weight: 600; line-height: 1.5; margin: 0; }
.mfa-confirm label span { font-size: .86rem; font-weight: 600; }
.mfa-confirm textarea { background: var(--color-field); border: 1px solid var(--color-border-strong); border-radius: .5rem; color: var(--color-ink); font: inherit; min-height: 4rem; padding: .55rem .7rem; resize: vertical; width: 100%; }
.mfa-confirm small { color: var(--color-muted); }
</style>
