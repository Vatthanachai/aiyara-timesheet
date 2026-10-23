<script setup lang="ts">
import { nextTick, onMounted, ref, toRef } from 'vue'
import { safeError, type Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'

/**
 * Confirmation for resetting one member's MFA: it clears their authenticator and recovery codes and
 * signs them out, so the admin gives an optional reason and confirms. The request is made here; the
 * page is told when it succeeded (`reset`) or the form is dismissed (`close`).
 */
const props = defineProps<{
  locale: Locale; accountId: string; email: string
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const emit = defineEmits<{ reset: []; close: [] }>()
const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'รีเซ็ต MFA ของ {email}',
    warning: 'ระบบจะลบแอปยืนยันตัวตนและรหัสกู้คืนของบัญชีนี้ และออกจากระบบทุกอุปกรณ์ การรีเซ็ตมีผลกับบัญชีนี้ในทุกองค์กรที่เป็นสมาชิก หากองค์กรบังคับใช้ MFA สมาชิกต้องตั้งค่าใหม่เมื่อเข้าสู่ระบบ',
    reason: 'เหตุผล (ไม่บังคับ)', confirm: 'รีเซ็ต MFA', cancel: 'ยกเลิก', saving: 'กำลังรีเซ็ต…',
    gone: 'ไม่พบสมาชิกคนนี้ในองค์กรแล้ว', self: 'คุณรีเซ็ต MFA ของตัวเองไม่ได้ ให้ผู้ดูแลองค์กรคนอื่นทำแทน'
  },
  en: {
    title: 'Reset MFA for {email}',
    warning: 'This removes the member\'s authenticator app and recovery codes and signs them out everywhere. The reset applies to the account in every organization it belongs to. If your organization requires MFA, they set it up again at their next sign-in.',
    reason: 'Reason (optional)', confirm: 'Reset MFA', cancel: 'Cancel', saving: 'Resetting…',
    gone: 'This member is no longer in the organization.', self: 'You cannot reset your own MFA. Ask another organization administrator.'
  }
})
const reason = ref('')
const pending = ref(false)
const error = ref('')
const reasonInput = ref<HTMLTextAreaElement | null>(null)
onMounted(async () => { await nextTick(); reasonInput.value?.focus() })

async function confirm() {
  pending.value = true
  error.value = ''
  try {
    await props.api(`/identity/members/${props.accountId}/mfa-reset`, { method: 'POST', body: { reason: reason.value.trim() || undefined } })
    emit('reset')
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'NOT_FOUND' ? copy.value.gone : code === 'CANNOT_CHANGE_SELF' ? copy.value.self : safeError(cause, props.locale)
  } finally { pending.value = false }
}
</script>

<template>
  <form class="mfa-reset" :aria-labelledby="`mfa-reset-${accountId}`" @submit.prevent="confirm" @keydown.esc="emit('close')">
    <h3 :id="`mfa-reset-${accountId}`">{{ copy.title.replace('{email}', email) }}</h3>
    <p class="mfa-warning">{{ copy.warning }}</p>
    <label :for="`mfa-reset-reason-${accountId}`"><span>{{ copy.reason }}</span></label>
    <textarea :id="`mfa-reset-reason-${accountId}`" ref="reasonInput" v-model="reason" maxlength="500" rows="2"></textarea>
    <p v-if="error" class="feedback feedback-error" role="alert">{{ error }}</p>
    <div class="row-actions">
      <button class="button button-warning" type="submit" :disabled="pending">{{ pending ? copy.saving : copy.confirm }}</button>
      <button class="button button-subtle" type="button" :disabled="pending" @click="emit('close')">{{ copy.cancel }}</button>
    </div>
  </form>
</template>

<style scoped>
.mfa-reset { background: var(--color-warn-soft); border: 1px solid var(--color-warn-line); border-radius: .6rem; display: grid; gap: .5rem; max-width: 40rem; padding: 1rem; white-space: normal; }
.mfa-reset h3 { font-size: 1rem; margin: 0; overflow-wrap: anywhere; }
.mfa-warning { color: var(--color-warn); font-weight: 600; line-height: 1.5; margin: 0; }
.mfa-reset label span { font-size: .86rem; font-weight: 600; }
.mfa-reset textarea { background: var(--color-field); border: 1px solid var(--color-border-strong); border-radius: .5rem; color: var(--color-ink); font: inherit; min-height: 4rem; padding: .55rem .7rem; resize: vertical; width: 100%; }
</style>
