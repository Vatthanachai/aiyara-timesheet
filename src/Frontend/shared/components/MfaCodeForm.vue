<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useFieldLocale } from '../i18n'

/**
 * One second-factor code, typed by a member: the six digits from an authenticator app or, when
 * `allowRecovery` is set, a recovery code instead. It emits the request fields (`totpCode` or
 * `recoveryCode`) and never keeps, logs, or puts the code in a URL. The caller owns the request,
 * so `pending` and `error` come from it; `clear()` empties the field for a fresh attempt.
 */
const props = withDefaults(defineProps<{
  locale?: 'th' | 'en'; heading?: string; hint?: string; allowRecovery?: boolean; pending?: boolean
  error?: string; submitLabel?: string; cancelLabel?: string; autofocus?: boolean; idPrefix?: string
}>(), { locale: undefined, autofocus: true, idPrefix: 'mfa' })
const emit = defineEmits<{ submit: [factor: { totpCode: string } | { recoveryCode: string }]; cancel: [] }>()
const locale = useFieldLocale(() => props.locale)
const words = computed(() => locale.value === 'th'
  ? { code: 'รหัสยืนยัน', codeHint: 'กรอกรหัส 6 หลักจากแอปยืนยันตัวตน', recovery: 'รหัสกู้คืน',
    recoveryHint: 'กรอกรหัสกู้คืนหนึ่งรหัส รหัสที่ใช้แล้วจะใช้ซ้ำไม่ได้', useRecovery: 'ใช้รหัสกู้คืนแทน',
    useApp: 'ใช้รหัสจากแอปแทน', submit: 'ยืนยัน', checking: 'กำลังตรวจสอบ…' }
  : { code: 'Authentication code', codeHint: 'Enter the 6-digit code from your authenticator app.', recovery: 'Recovery code',
    recoveryHint: 'Enter one recovery code. A code that has been used cannot be used again.', useRecovery: 'Use a recovery code instead',
    useApp: 'Use an authenticator code instead', submit: 'Verify', checking: 'Checking…' })
const recovery = ref(false)
const value = ref('')
const input = ref<HTMLInputElement | null>(null)
const headingId = `${props.idPrefix}-code-heading`
const errorId = `${props.idPrefix}-code-error`
const inputId = `${props.idPrefix}-code-input`

function focus() { input.value?.focus() }
onMounted(() => { if (props.autofocus) focus() })

async function toggle() {
  recovery.value = !recovery.value
  value.value = ''
  await nextTick()
  focus()
}
/** Empties the field, for a caller that wants a fresh attempt after an error. */
function clear() { value.value = ''; focus() }
defineExpose({ focus, clear })

function submit() {
  const typed = value.value.replace(/\s+/g, '')
  if (!typed) return
  emit('submit', recovery.value ? { recoveryCode: typed.toUpperCase() } : { totpCode: typed })
}
</script>

<template>
  <form class="mfa-code" :aria-labelledby="heading ? headingId : undefined" novalidate @submit.prevent="submit">
    <h2 v-if="heading" :id="headingId" class="mfa-heading">{{ heading }}</h2>
    <p class="mfa-hint">{{ hint ?? (recovery ? words.recoveryHint : words.codeHint) }}</p>
    <label :for="inputId" class="mfa-label">{{ recovery ? words.recovery : words.code }}</label>
    <input :id="inputId" ref="input" v-model="value" class="mfa-input" required spellcheck="false" autocapitalize="off"
      :autocomplete="recovery ? 'off' : 'one-time-code'" :inputmode="recovery ? 'text' : 'numeric'"
      :maxlength="recovery ? 24 : 7" :aria-invalid="error ? 'true' : undefined" :aria-describedby="error ? errorId : undefined" />
    <p v-if="error" :id="errorId" class="mfa-error" role="alert">{{ error }}</p>
    <div class="mfa-actions">
      <button class="mfa-button mfa-primary" type="submit" :disabled="pending || !value.trim()">{{ pending ? words.checking : (submitLabel ?? words.submit) }}</button>
      <button v-if="cancelLabel" class="mfa-button mfa-quiet" type="button" :disabled="pending" @click="emit('cancel')">{{ cancelLabel }}</button>
    </div>
    <button v-if="allowRecovery" class="mfa-link" type="button" @click="toggle">{{ recovery ? words.useApp : words.useRecovery }}</button>
  </form>
</template>

<style scoped>
.mfa-code { display: grid; gap: .5rem; min-width: 0; }
.mfa-heading { font-family: var(--font-display); font-size: 1.25rem; font-weight: 600; margin: 0; }
.mfa-hint { color: var(--color-muted); font-size: .92rem; line-height: 1.5; margin: 0; }
.mfa-label { font-size: .86rem; font-weight: 600; }
.mfa-input { background: var(--color-field); border: 1px solid var(--color-border-strong); border-radius: .5rem; box-shadow: var(--inset-field); color: var(--color-ink); font: inherit; font-size: 1.15rem; letter-spacing: .12em; min-height: 2.75rem; padding: .55rem .8rem; width: 100%; }
.mfa-input:focus-visible { border-color: var(--color-brand); outline: 2px solid var(--color-brand); outline-offset: 0; }
.mfa-error { background: var(--color-danger-soft); border: 1px solid var(--color-danger-line); border-radius: .5rem; color: var(--color-danger); font-size: .9rem; margin: 0; padding: .5rem .7rem; }
.mfa-actions { display: flex; flex-wrap: wrap; gap: .5rem; margin-top: .25rem; }
.mfa-button { align-items: center; border: 1px solid transparent; border-radius: .5rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .92rem; font-weight: 600; justify-content: center; min-height: 2.75rem; padding: .55rem 1rem; }
.mfa-button:focus-visible, .mfa-link:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.mfa-button:disabled { cursor: not-allowed; opacity: .6; }
.mfa-primary { background: var(--color-brand); color: var(--color-on-brand); }
.mfa-primary:hover:not(:disabled) { background: var(--color-brand-strong); }
.mfa-quiet { background: transparent; border-color: var(--color-border); color: var(--color-ink); }
.mfa-link { background: none; border: 0; color: var(--color-brand); cursor: pointer; font: inherit; font-size: .9rem; justify-self: start; padding: .35rem 0; text-decoration: underline; }
</style>
