<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useFieldLocale } from '../i18n'

/**
 * The ten recovery codes, shown once. The member can copy them or save them as a text file, and must
 * tick that they are saved before continuing; the codes live only in this component's props and are
 * never logged or placed in a URL.
 */
const props = withDefaults(defineProps<{
  locale?: 'th' | 'en'; codes: string[]; heading?: string; continueLabel?: string; idPrefix?: string
}>(), { locale: undefined, idPrefix: 'recovery' })
const emit = defineEmits<{ done: [] }>()
const locale = useFieldLocale(() => props.locale)
const words = computed(() => locale.value === 'th'
  ? { title: 'บันทึกรหัสกู้คืน',
    hint: 'ใช้รหัสเหล่านี้แทนแอปเมื่อเข้าถึงแอปไม่ได้ แต่ละรหัสใช้ได้ครั้งเดียว และจะไม่แสดงอีก เก็บไว้ในที่ปลอดภัย',
    list: 'รหัสกู้คืน', copy: 'คัดลอกรหัส', copied: 'คัดลอกรหัสแล้ว', copyFailed: 'คัดลอกไม่สำเร็จ กรุณาจดหรือบันทึกเป็นไฟล์แทน',
    download: 'บันทึกเป็นไฟล์', saved: 'ฉันบันทึกรหัสเหล่านี้ไว้แล้ว', continue: 'ดำเนินการต่อ' }
  : { title: 'Save your recovery codes',
    hint: 'Use one of these instead of your authenticator if you lose access to it. Each works once and they will not be shown again. Keep them somewhere safe.',
    list: 'Recovery codes', copy: 'Copy codes', copied: 'Codes copied.', copyFailed: 'Could not copy. Write them down or save them as a file instead.',
    download: 'Save as a file', saved: 'I have saved these codes', continue: 'Continue' })
const headingId = `${props.idPrefix}-heading`
const title = ref<HTMLElement | null>(null)
const acknowledged = ref(false)
const status = ref('')
onMounted(() => title.value?.focus())

async function copy() {
  try { await navigator.clipboard.writeText(props.codes.join('\n')); status.value = words.value.copied }
  catch { status.value = words.value.copyFailed }
}
function download() {
  const url = URL.createObjectURL(new Blob([props.codes.join('\n') + '\n'], { type: 'text/plain' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'aiyara-recovery-codes.txt'
  link.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <section class="recovery" :aria-labelledby="headingId">
    <h2 :id="headingId" ref="title" tabindex="-1" class="recovery-heading">{{ heading ?? words.title }}</h2>
    <p class="recovery-hint">{{ words.hint }}</p>
    <ol class="recovery-list" :aria-label="words.list"><li v-for="item in codes" :key="item"><code>{{ item }}</code></li></ol>
    <div class="recovery-tools">
      <button class="recovery-button recovery-quiet" type="button" @click="copy">{{ words.copy }}</button>
      <button class="recovery-button recovery-quiet" type="button" @click="download">{{ words.download }}</button>
    </div>
    <p class="recovery-status" role="status">{{ status }}</p>
    <label class="recovery-ack"><input v-model="acknowledged" type="checkbox" /><span>{{ words.saved }}</span></label>
    <button class="recovery-button recovery-primary" type="button" :disabled="!acknowledged" @click="emit('done')">{{ continueLabel ?? words.continue }}</button>
  </section>
</template>

<style scoped>
.recovery { display: grid; gap: .6rem; justify-items: start; min-width: 0; }
.recovery-heading { font-family: var(--font-display); font-size: 1.25rem; font-weight: 600; margin: 0; }
.recovery-heading:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 3px; }
.recovery-hint { color: var(--color-muted); font-size: .92rem; line-height: 1.5; margin: 0; }
.recovery-list { background: var(--color-surface-muted); border: 1px solid var(--color-border); border-radius: .5rem; display: grid; gap: .35rem .75rem; grid-template-columns: repeat(auto-fill, minmax(9.5rem, 1fr)); margin: 0; padding: .75rem .75rem .75rem 2.1rem; width: 100%; }
.recovery-list code { font-size: 1rem; letter-spacing: .06em; overflow-wrap: anywhere; }
.recovery-tools { display: flex; flex-wrap: wrap; gap: .5rem; }
.recovery-status { color: var(--color-success); font-size: .88rem; margin: 0; min-height: 1.2em; }
.recovery-ack { align-items: center; display: flex; font-weight: 600; gap: .6rem; min-height: 2.75rem; }
.recovery-ack input { height: 1.2rem; width: 1.2rem; }
.recovery-ack input:focus-visible, .recovery-button:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.recovery-button { align-items: center; border: 1px solid transparent; border-radius: .5rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .92rem; font-weight: 600; justify-content: center; min-height: 2.75rem; padding: .55rem 1rem; }
.recovery-button:disabled { cursor: not-allowed; opacity: .55; }
.recovery-primary { background: var(--color-brand); color: var(--color-on-brand); }
.recovery-primary:hover:not(:disabled) { background: var(--color-brand-strong); }
.recovery-quiet { background: transparent; border-color: var(--color-border); color: var(--color-ink); }
</style>
