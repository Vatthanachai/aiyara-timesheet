<script setup lang="ts">
import { formatDate, inputDateFormat, parseDate, type Formats } from '../../../../shared/date-format'
import type { Locale } from '../../../../shared/i18n'

/**
 * A date typed in the organization's format (dd/MM/yyyy by default) instead of the browser's own
 * date input, whose display follows the device. The calendar icon is the browser's own date input,
 * invisible and laid over it, so a click opens the browser's picker: `showPicker()` is refused in
 * a cross-origin iframe, which every remote is. The model is always `YYYY-MM-DD`; text that is not
 * a real date blocks the form.
 */
const props = defineProps<{ formats: Formats; locale: Locale; label: string; invalidMessage: string; pickLabel: string; required?: boolean }>()
const model = defineModel<string>({ required: true })
const text = ref('')
const field = ref<HTMLInputElement | null>(null)
const placeholder = computed(() => inputDateFormat(props.formats).dateFormat.replace('bbbb', 'yyyy').toUpperCase())
const shown = (value: string) => value ? formatDate(value, inputDateFormat(props.formats), props.locale) : ''

function check() {
  const empty = !text.value.trim()
  const valid = empty ? !props.required : parseDate(text.value, props.formats) !== null
  // An empty required field gets the browser's own "fill in this field" message.
  field.value?.setCustomValidity(valid || empty ? '' : props.invalidMessage)
  return valid
}
function onInput() {
  const value = parseDate(text.value, props.formats)
  if (value) model.value = value
  else if (!text.value.trim()) model.value = ''
  check()
}
/** Rewrites what was typed (25/12/2026 for 25-12-2026) once the field is left. */
function onChange() {
  const value = parseDate(text.value, props.formats)
  if (value) text.value = shown(value)
  check()
}
function onPicked(event: Event) {
  const value = (event.target as HTMLInputElement).value
  if (!value) return
  model.value = value
  text.value = shown(model.value)
  check()
}
watch([model, () => props.formats], () => {
  if (parseDate(text.value, props.formats) !== model.value || !model.value) text.value = shown(model.value)
  nextTick(check)
}, { immediate: true })
</script>

<template>
  <span class="date-field">
    <input ref="field" v-model="text" type="text" inputmode="numeric" autocomplete="off" :required="required"
      :aria-label="label" :placeholder="placeholder" @input="onInput" @change="onChange" />
    <!-- Keyboard users type the date; this is the mouse and touch way to the calendar. -->
    <input class="date-field-picker" type="date" :value="model" tabindex="-1" :aria-label="pickLabel" :title="pickLabel" @change="onPicked" />
    <span class="date-field-pick" aria-hidden="true">▦</span>
  </span>
</template>

<style scoped>
.date-field{position:relative;display:flex;align-items:center;min-width:0;width:100%}
.date-field input[type="text"]{padding-right:2.4rem!important}
.date-field-pick{position:absolute;right:.3rem;display:grid;place-items:center;width:1.9rem;height:1.9rem;border-radius:.45rem;color:var(--color-muted);font-size:.95rem;pointer-events:none}
.date-field-picker:hover+.date-field-pick{background:var(--color-surface-muted);color:var(--color-ink-soft)}
/* The browser's date input over the icon; its calendar indicator fills it, so any click opens it. */
.date-field-picker{position:absolute;right:.3rem;width:1.9rem;height:1.9rem;margin:0;padding:0;border:0;opacity:0;cursor:pointer}
.date-field-picker::-webkit-calendar-picker-indicator{position:absolute;inset:0;width:auto;height:auto;margin:0;padding:0;cursor:pointer}
</style>
