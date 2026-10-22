<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { FIELD_FORMATS, formatDate, parseDate } from '../date-format'
import { useFieldLocale } from '../i18n'

/**
 * A date, always typed and shown as dd/MM/yyyy, instead of the browser's date input, whose display
 * follows the device. The calendar button is the browser's own date input, invisible and laid over
 * it, so a click opens the browser's picker: `showPicker()` is refused in a cross-origin iframe,
 * which every remote is. The model is always `YYYY-MM-DD`; text that is not a real date, or falls
 * outside `min`–`max`, blocks the form with a message saying how to type it.
 */
const props = withDefaults(defineProps<{
  label?: string; locale?: 'th' | 'en'; required?: boolean; disabled?: boolean; min?: string; max?: string
  invalidMessage?: string; pickLabel?: string
}>(), { locale: undefined })
const locale = useFieldLocale(() => props.locale)
const model = defineModel<string>({ required: true })
const emit = defineEmits<{ change: [value: string] }>()
const text = ref('')
const field = ref<HTMLInputElement | null>(null)
const words = computed(() => locale.value === 'th'
  ? { invalid: 'กรอกวันที่ให้ถูกต้องในรูปแบบ วว/ดด/ปปปป (ปี ค.ศ.) เช่น 31/12/2026', range: 'เลือกวันที่ระหว่าง {min} ถึง {max}', pick: 'เลือกจากปฏิทิน', placeholder: 'วว/ดด/ปปปป' }
  : { invalid: 'Enter a real date as dd/MM/yyyy, for example 31/12/2026.', range: 'Choose a date from {min} to {max}.', pick: 'Choose from the calendar', placeholder: 'dd/MM/yyyy' })
const shown = (value: string) => value ? formatDate(value, FIELD_FORMATS, locale.value) : ''
const inRange = (value: string) => (!props.min || value >= props.min) && (!props.max || value <= props.max)

function check() {
  const empty = !text.value.trim()
  const value = parseDate(text.value, FIELD_FORMATS)
  const message = empty ? '' : !value ? (props.invalidMessage ?? words.value.invalid)
    : inRange(value) ? '' : words.value.range.replace('{min}', shown(props.min ?? '') || '…').replace('{max}', shown(props.max ?? '') || '…')
  // An empty required field gets the browser's own "fill in this field" message.
  field.value?.setCustomValidity(message)
  return !message && !(empty && props.required)
}
function set(value: string) {
  if (value === model.value) return
  model.value = value
  emit('change', value)
}
function onInput() {
  const value = parseDate(text.value, FIELD_FORMATS)
  if (value && inRange(value)) set(value)
  else if (!text.value.trim()) set('')
  check()
}
/** Rewrites what was typed (25/12/2026 for 25-12-2026 or 25.12.26) once the field is left. */
function onChange() {
  const value = parseDate(text.value, FIELD_FORMATS)
  if (value) text.value = shown(value)
  check()
}
function onPicked(event: Event) {
  const value = (event.target as HTMLInputElement).value
  if (!value) return
  set(value)
  text.value = shown(value)
  check()
}
watch([model, locale, () => props.min, () => props.max], () => {
  if (parseDate(text.value, FIELD_FORMATS) !== model.value || !model.value) text.value = shown(model.value)
  void nextTick(check)
}, { immediate: true })
</script>

<template>
  <span class="date-field">
    <input ref="field" v-model="text" class="date-field-text" type="text" inputmode="numeric" autocomplete="off"
      :required="required" :disabled="disabled" :aria-label="label" :placeholder="words.placeholder" @input="onInput" @change="onChange" />
    <!-- Keyboard users type the date; this is the mouse and touch way to the calendar. -->
    <input class="date-field-picker" type="date" :value="model" :min="min" :max="max" :disabled="disabled" tabindex="-1"
      :aria-label="pickLabel ?? words.pick" :title="pickLabel ?? words.pick" @change="onPicked" />
    <svg class="date-field-pick" viewBox="0 0 20 20" aria-hidden="true"><rect x="3.2" y="4.2" width="13.6" height="12.6" rx="2" /><path d="M3.2 8.2h13.6M7 2.8v2.8M13 2.8v2.8" /></svg>
  </span>
</template>

<style scoped>
.date-field { align-items: center; display: flex; min-width: 0; position: relative; width: 100%; }
.date-field-text { font-variant-numeric: tabular-nums; padding-right: 2.4rem !important; width: 100%; }
.date-field-text:disabled { cursor: not-allowed; opacity: .65; }
.date-field-pick {
  border-radius: .4rem; color: var(--color-muted); fill: none; height: 1.8rem; padding: .4rem; pointer-events: none; position: absolute;
  right: .3rem; stroke: currentColor; stroke-linecap: round; stroke-width: 1.5; width: 1.8rem;
}
.date-field-picker:hover:not(:disabled) + .date-field-pick { background: var(--color-surface-muted); color: var(--color-ink); }
/* The browser's date input over the icon; its calendar indicator fills it, so any click opens it. */
.date-field-picker { border: 0; cursor: pointer; height: 1.8rem; margin: 0; opacity: 0; padding: 0; position: absolute; right: .3rem; width: 1.8rem; }
.date-field-picker::-webkit-calendar-picker-indicator { cursor: pointer; height: auto; inset: 0; margin: 0; padding: 0; position: absolute; width: auto; }
</style>
