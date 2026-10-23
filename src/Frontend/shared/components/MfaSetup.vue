<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useFieldLocale } from '../i18n'
import MfaCodeForm from './MfaCodeForm.vue'

/**
 * Adds an authenticator: the QR code and the typed key, then the first code from the app. The QR is
 * drawn by the caller from the `otpauth://` URI (`qr` is a data URL), so the secret never reaches a
 * third-party service. The secret is shown, never stored, logged, or placed in a URL.
 */
const props = withDefaults(defineProps<{
  locale?: 'th' | 'en'; secret: string; provisioningUri: string; qr: string; pending?: boolean; error?: string
  submitLabel?: string; cancelLabel?: string; heading?: string; idPrefix?: string
}>(), { locale: undefined, idPrefix: 'mfa-setup' })
const emit = defineEmits<{ submit: [factor: { totpCode: string }]; cancel: [] }>()
const locale = useFieldLocale(() => props.locale)
const words = computed(() => locale.value === 'th'
  ? { title: 'ตั้งค่าแอปยืนยันตัวตน',
    hint: 'สแกน QR ด้วยแอปยืนยันตัวตน (เช่น Google Authenticator หรือ 1Password) หรือกรอกคีย์ด้านล่างลงในแอป',
    qr: 'QR สำหรับเพิ่มบัญชีในแอปยืนยันตัวตน', key: 'คีย์ตั้งค่า', open: 'เปิดในแอปยืนยันตัวตน',
    confirmHint: 'จากนั้นกรอกรหัส 6 หลักที่แอปแสดงตอนนี้ เพื่อยืนยันว่าตั้งค่าสำเร็จ' }
  : { title: 'Set up your authenticator',
    hint: 'Scan the QR code with an authenticator app (such as Google Authenticator or 1Password), or type the key below into it.',
    qr: 'QR code to add this account to an authenticator app', key: 'Setup key', open: 'Open in authenticator app',
    confirmHint: 'Then enter the 6-digit code the app shows now, to confirm it works.' })
const headingId = `${props.idPrefix}-heading`
const title = ref<HTMLElement | null>(null)
const form = ref<InstanceType<typeof MfaCodeForm> | null>(null)
// The key is shown in groups of four so it is easier to type; the app accepts it with or without spaces.
const grouped = computed(() => props.secret.replace(/\s+/g, '').replace(/(.{4})/g, '$1 ').trim())
onMounted(() => title.value?.focus())
defineExpose({ clear: () => form.value?.clear() })
</script>

<template>
  <section class="mfa-setup" :aria-labelledby="headingId">
    <h2 :id="headingId" ref="title" tabindex="-1" class="mfa-heading">{{ heading ?? words.title }}</h2>
    <p class="mfa-hint">{{ words.hint }}</p>
    <img class="mfa-qr" :src="qr" :alt="words.qr" />
    <p class="mfa-key">{{ words.key }}: <code>{{ grouped }}</code></p>
    <a class="mfa-open" :href="provisioningUri">{{ words.open }}</a>
    <MfaCodeForm ref="form" :id-prefix="idPrefix" :locale="locale" :hint="words.confirmHint" :pending="pending" :error="error"
      :submit-label="submitLabel" :cancel-label="cancelLabel" :autofocus="false" @submit="factor => emit('submit', factor as { totpCode: string })" @cancel="emit('cancel')" />
  </section>
</template>

<style scoped>
.mfa-setup { display: grid; gap: .6rem; justify-items: start; min-width: 0; }
.mfa-heading { font-family: var(--font-display); font-size: 1.25rem; font-weight: 600; margin: 0; }
.mfa-heading:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 3px; }
.mfa-hint { color: var(--color-muted); font-size: .92rem; line-height: 1.5; margin: 0; }
.mfa-qr { background: #fff; border: 1px solid var(--color-border); border-radius: .5rem; height: auto; image-rendering: pixelated; max-width: 100%; padding: .5rem; width: 13rem; }
.mfa-key { margin: 0; max-width: 100%; }
.mfa-key code { font-size: 1rem; letter-spacing: .08em; overflow-wrap: anywhere; }
.mfa-open { color: var(--color-brand); font-size: .9rem; }
.mfa-setup :deep(.mfa-code) { justify-self: stretch; width: 100%; }
</style>
