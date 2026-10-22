<script setup lang="ts">
import { computed, nextTick, ref, toRef, watch } from 'vue'
import { renderSVG } from 'uqr'
import { safeError, type Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'
import { DEFAULT_FORMATS, formatDate } from '../../../../shared/date-format'
import MfaCodeForm from '../../../../shared/components/MfaCodeForm.vue'
import MfaSetup from '../../../../shared/components/MfaSetup.vue'
import RecoveryCodes from '../../../../shared/components/RecoveryCodes.vue'

type MfaStatus = { enabled: boolean; enabledAtUtc: string | null; recoveryCodesRemaining: number; requiredByOrganization: boolean; canDisable: boolean }
type Mode = 'idle' | 'enroll' | 'regenerate' | 'disable' | 'codes'

/**
 * The signed-in member's own two-step verification: its state, opting in with an authenticator app,
 * new recovery codes, and opting out. Secrets and codes live only in this component's state, are
 * never logged, and are dropped as soon as the step ends.
 */
const props = defineProps<{
  token: string; locale: Locale
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'การยืนยันตัวตนสองขั้นตอน', hint: 'เพิ่มความปลอดภัยให้บัญชีด้วยรหัส 6 หลักจากแอปยืนยันตัวตนทุกครั้งที่เข้าสู่ระบบ',
    loading: 'กำลังโหลดสถานะ…', loadError: 'โหลดสถานะไม่สำเร็จ', retry: 'ลองอีกครั้ง',
    on: 'เปิดใช้งาน', off: 'ยังไม่เปิดใช้งาน', onSince: 'เปิดใช้งานเมื่อ {date}',
    remaining: 'รหัสกู้คืนคงเหลือ {count} จาก 10 รหัส', remainingLow: 'รหัสกู้คืนใกล้หมด เหลือ {count} รหัส ควรสร้างชุดใหม่',
    requiredNote: 'องค์กรของคุณกำหนดให้ทุกคนใช้การยืนยันตัวตนสองขั้นตอน จึงปิดการใช้งานเองไม่ได้ หากต้องการปิดหรือเปลี่ยนแอป ให้ติดต่อผู้ดูแลองค์กรเพื่อรีเซ็ต',
    enable: 'ตั้งค่าแอปยืนยันตัวตน', regenerate: 'สร้างรหัสกู้คืนชุดใหม่', disable: 'ปิดการใช้งาน', cancel: 'ยกเลิก',
    starting: 'กำลังเตรียมการตั้งค่า…', confirmEnable: 'เปิดใช้งาน',
    regenerateTitle: 'สร้างรหัสกู้คืนชุดใหม่', regenerateHint: 'กรอกรหัส 6 หลักจากแอป รหัสกู้คืนชุดเดิมทั้งหมดจะใช้ไม่ได้อีก',
    disableTitle: 'ปิดการยืนยันตัวตนสองขั้นตอน', disableHint: 'กรอกรหัสจากแอปหรือรหัสกู้คืนเพื่อยืนยัน บัญชีของคุณจะป้องกันด้วยรหัสผ่านอย่างเดียว',
    confirmDisable: 'ปิดการใช้งาน', enabledNotice: 'เปิดการยืนยันตัวตนสองขั้นตอนแล้ว', regeneratedNotice: 'สร้างรหัสกู้คืนชุดใหม่แล้ว',
    disabledNotice: 'ปิดการยืนยันตัวตนสองขั้นตอนแล้ว', stateChanged: 'สถานะเปลี่ยนไป โหลดสถานะล่าสุดแล้ว',
    enrolledCodesHeading: 'เปิดใช้งานแล้ว บันทึกรหัสกู้คืน', newCodesHeading: 'รหัสกู้คืนชุดใหม่ของคุณ'
  },
  en: {
    title: 'Two-step verification', hint: 'Protect your account with a 6-digit code from an authenticator app each time you sign in.',
    loading: 'Loading status…', loadError: 'Could not load the status.', retry: 'Try again',
    on: 'On', off: 'Off', onSince: 'On since {date}',
    remaining: '{count} of 10 recovery codes left', remainingLow: 'Only {count} recovery codes left. Generate a new set.',
    requiredNote: 'Your organization requires two-step verification for everyone, so you cannot turn it off. To turn it off or change your app, ask an organization administrator to reset it.',
    enable: 'Set up an authenticator app', regenerate: 'Get new recovery codes', disable: 'Turn off', cancel: 'Cancel',
    starting: 'Preparing setup…', confirmEnable: 'Turn on',
    regenerateTitle: 'Get new recovery codes', regenerateHint: 'Enter the 6-digit code from your app. Your current recovery codes will stop working.',
    disableTitle: 'Turn off two-step verification', disableHint: 'Enter a code from your app or a recovery code to confirm. Your account will be protected by your password alone.',
    confirmDisable: 'Turn off', enabledNotice: 'Two-step verification is on.', regeneratedNotice: 'New recovery codes created.',
    disabledNotice: 'Two-step verification is off.', stateChanged: 'The status had changed. Showing the latest.',
    enrolledCodesHeading: 'Turned on. Save your recovery codes', newCodesHeading: 'Your new recovery codes'
  }
})
const status = ref<MfaStatus | null>(null)
const mode = ref<Mode>('idle')
const loading = ref(false)
const pending = ref(false)
const error = ref('')
const notice = ref('')
const enrollment = ref<{ secret: string; provisioningUri: string } | null>(null)
const codes = ref<string[]>([])
const codesHeading = ref('')
const title = ref<HTMLElement | null>(null)
const form = ref<{ clear: () => void } | null>(null)
// Drawn in the browser so the secret never reaches a third-party QR service.
const qr = computed(() => enrollment.value
  ? `data:image/svg+xml;charset=utf-8,${encodeURIComponent(renderSVG(enrollment.value.provisioningUri, { pixelSize: 6 }))}`
  : '')
const errorCode = (cause: unknown) => (cause as { data?: { errorCode?: string } })?.data?.errorCode
const since = computed(() => {
  const iso = status.value?.enabledAtUtc
  if (!iso) return ''
  const day = new Date(iso).toLocaleDateString('sv-SE')
  return copy.value.onSince.replace('{date}', formatDate(day, DEFAULT_FORMATS, props.locale))
})
const lowCodes = computed(() => (status.value?.recoveryCodesRemaining ?? 10) <= 2)

async function load() {
  loading.value = true
  error.value = ''
  try { status.value = await props.api<MfaStatus>('') }
  catch (cause) { status.value = null; error.value = safeError(cause, props.locale) }
  finally { loading.value = false }
}
watch(() => props.token, value => {
  if (value) void load()
  else { status.value = null; mode.value = 'idle'; enrollment.value = null; codes.value = [] }
}, { immediate: true })

/** Back to the status view, with focus on the opener (or the heading when the opener is gone). */
async function toIdle(focusId?: string) {
  mode.value = 'idle'
  enrollment.value = null
  codes.value = []
  error.value = ''
  await nextTick()
  const target = focusId ? document.getElementById(focusId) : null
  ;(target ?? title.value)?.focus()
}

async function run(action: () => Promise<void>) {
  pending.value = true
  error.value = ''
  try { await action() }
  catch (cause) {
    const code = errorCode(cause)
    if (code === 'MFA_ALREADY_ENROLLED' || code === 'MFA_NOT_ENROLLED' || code === 'MFA_REQUIRED_BY_ORGANIZATION') {
      await load()
      await toIdle()
      notice.value = copy.value.stateChanged
      error.value = safeError(cause, props.locale)
    } else {
      error.value = safeError(cause, props.locale)
      await nextTick()
      form.value?.clear()
    }
  } finally { pending.value = false }
}

async function startEnrollment() {
  notice.value = ''
  await run(async () => {
    enrollment.value = await props.api<{ secret: string; provisioningUri: string }>('/enrollment', { method: 'POST' })
    mode.value = 'enroll'
  })
}
const confirmEnrollment = (factor: { totpCode: string }) => run(async () => {
  const result = await props.api<{ recoveryCodes: string[] }>('/enrollment/confirm', { method: 'POST', body: { totpCode: factor.totpCode } })
  enrollment.value = null
  codes.value = result.recoveryCodes
  codesHeading.value = copy.value.enrolledCodesHeading
  mode.value = 'codes'
  notice.value = copy.value.enabledNotice
  await load()
})
const regenerate = (factor: { totpCode: string }) => run(async () => {
  const result = await props.api<{ recoveryCodes: string[] }>('/recovery-codes', { method: 'POST', body: { totpCode: factor.totpCode } })
  codes.value = result.recoveryCodes
  codesHeading.value = copy.value.newCodesHeading
  mode.value = 'codes'
  notice.value = copy.value.regeneratedNotice
  await load()
})
const turnOff = (factor: { totpCode: string } | { recoveryCode: string }) => run(async () => {
  await props.api('/disable', { method: 'POST', body: factor })
  await load()
  await toIdle()
  notice.value = copy.value.disabledNotice
})
function open(next: Mode) {
  notice.value = ''
  error.value = ''
  mode.value = next
}
</script>

<template>
  <section class="mfa" aria-labelledby="mfa-heading" :aria-busy="loading || pending">
    <h2 id="mfa-heading" ref="title" tabindex="-1">{{ copy.title }}</h2>
    <p class="mfa-hint">{{ copy.hint }}</p>

    <div class="mfa-live" aria-live="polite">
      <p v-if="notice" class="mfa-notice" role="status">{{ notice }}</p>
    </div>
    <p v-if="loading && !status" class="mfa-muted" role="status">{{ copy.loading }}</p>
    <p v-else-if="error && mode === 'idle'" class="mfa-error" role="alert">{{ error }}
      <button v-if="!status" class="mfa-button mfa-quiet" type="button" @click="load">{{ copy.retry }}</button>
    </p>

    <template v-if="status">
      <template v-if="mode === 'idle'">
        <dl class="mfa-state">
          <div><dt class="sr-only">{{ copy.title }}</dt>
            <dd><span class="mfa-badge" :class="status.enabled ? 'is-on' : 'is-off'">{{ status.enabled ? copy.on : copy.off }}</span>
              <span v-if="status.enabled && since" class="mfa-muted">{{ since }}</span></dd></div>
          <div v-if="status.enabled"><dt class="sr-only">{{ copy.regenerate }}</dt>
            <dd :class="{ 'mfa-warn': lowCodes }">{{ (lowCodes ? copy.remainingLow : copy.remaining).replace('{count}', String(status.recoveryCodesRemaining)) }}</dd></div>
        </dl>
        <p v-if="status.requiredByOrganization" class="mfa-muted mfa-required">{{ copy.requiredNote }}</p>
        <div class="mfa-actions">
          <button v-if="!status.enabled" id="mfa-enable" class="mfa-button mfa-primary" type="button" :disabled="pending" @click="startEnrollment">{{ pending ? copy.starting : copy.enable }}</button>
          <template v-else>
            <button id="mfa-regenerate" class="mfa-button mfa-secondary" type="button" @click="open('regenerate')">{{ copy.regenerate }}</button>
            <button v-if="status.canDisable && !status.requiredByOrganization" id="mfa-disable" class="mfa-button mfa-danger" type="button" @click="open('disable')">{{ copy.disable }}</button>
          </template>
        </div>
      </template>

      <MfaSetup v-else-if="mode === 'enroll' && enrollment" ref="form" id-prefix="profile-mfa" :locale="locale" :secret="enrollment.secret"
        :provisioning-uri="enrollment.provisioningUri" :qr="qr" :pending="pending" :error="error" :submit-label="copy.confirmEnable"
        :cancel-label="copy.cancel" @submit="confirmEnrollment" @cancel="toIdle('mfa-enable')" />

      <MfaCodeForm v-else-if="mode === 'regenerate'" ref="form" id-prefix="profile-regenerate" :locale="locale" :heading="copy.regenerateTitle"
        :hint="copy.regenerateHint" :pending="pending" :error="error" :submit-label="copy.regenerate" :cancel-label="copy.cancel"
        @submit="factor => regenerate(factor as { totpCode: string })" @cancel="toIdle('mfa-regenerate')" />

      <MfaCodeForm v-else-if="mode === 'disable'" ref="form" id-prefix="profile-disable" :locale="locale" :heading="copy.disableTitle"
        :hint="copy.disableHint" allow-recovery :pending="pending" :error="error" :submit-label="copy.confirmDisable" :cancel-label="copy.cancel"
        @submit="turnOff" @cancel="toIdle('mfa-disable')" />

      <RecoveryCodes v-else-if="mode === 'codes'" id-prefix="profile-codes" :locale="locale" :codes="codes" :heading="codesHeading"
        @done="toIdle('mfa-regenerate')" />
    </template>
  </section>
</template>

<style scoped>
.mfa { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .9rem; display: grid; gap: .75rem; justify-items: start; min-width: 0; padding: clamp(1rem, 2.5vw, 1.5rem); }
.mfa h2 { font-family: var(--font-display); font-size: 1.2rem; font-weight: 600; margin: 0; }
.mfa h2:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 3px; }
.mfa > :deep(*) { max-width: 100%; }
.mfa-hint, .mfa-muted { color: var(--color-muted); font-size: .9rem; line-height: 1.5; margin: 0; }
.mfa-live { min-height: 0; }
.mfa-notice { background: var(--color-success-soft); border: 1px solid var(--color-success-line); border-radius: .5rem; color: var(--color-success); font-size: .9rem; margin: 0; padding: .5rem .7rem; }
.mfa-error { align-items: center; background: var(--color-danger-soft); border: 1px solid var(--color-danger-line); border-radius: .5rem; color: var(--color-danger); display: flex; flex-wrap: wrap; font-size: .9rem; gap: .5rem; margin: 0; padding: .5rem .7rem; }
.mfa-state { display: grid; gap: .4rem; margin: 0; }
.mfa-state dd { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem; margin: 0; }
.mfa-badge { border-radius: 999px; font-size: .8rem; font-weight: 700; padding: .15rem .65rem; }
.mfa-badge.is-on { background: var(--color-success-soft); color: var(--color-success); }
.mfa-badge.is-off { background: var(--color-surface-muted); color: var(--color-muted); }
.mfa-warn { color: var(--color-warn); font-weight: 600; }
.mfa-required { background: var(--color-warn-soft); border: 1px solid var(--color-warn-line); border-radius: .5rem; color: var(--color-warn); padding: .5rem .7rem; }
.mfa-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
.mfa-button { align-items: center; border: 1px solid transparent; border-radius: .5rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .9rem; font-weight: 600; justify-content: center; min-height: 2.75rem; padding: .55rem 1rem; }
.mfa-button:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.mfa-button:disabled { cursor: not-allowed; opacity: .6; }
.mfa-primary { background: var(--color-brand); color: var(--color-on-brand); }
.mfa-primary:hover:not(:disabled) { background: var(--color-brand-strong); }
.mfa-secondary, .mfa-quiet { background: var(--color-surface); border-color: var(--color-border); color: var(--color-ink); }
.mfa-danger { background: transparent; border-color: var(--color-danger-line); color: var(--color-danger); }
.sr-only { clip: rect(0, 0, 0, 0); height: 1px; margin: -1px; overflow: hidden; position: absolute; white-space: nowrap; width: 1px; }
</style>
