<script setup lang="ts">
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useFieldLocale } from '../i18n'

export type AutoCompleteValue = string | number
export type AutoCompleteOption = { value: AutoCompleteValue; label: string; hint?: string }

/**
 * A choice typed or picked from a list, used everywhere a <select> was. Typing filters the list
 * (the matched part is marked); arrow keys move through it, Enter picks, Escape puts back the
 * last choice, and leaving the field keeps a choice only if the text names one. With `custom`,
 * text that names no option can still be a value (a time typed as 9.30, for instance).
 * It follows the ARIA combobox pattern: the field is the combobox and the list a listbox.
 */
const props = withDefaults(defineProps<{
  options: AutoCompleteOption[]
  label?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  locale?: 'th' | 'en'
  inputmode?: 'text' | 'numeric'
  /** Reads typed text that names no option as a value, or null when it is not one. */
  custom?: (text: string) => AutoCompleteValue | null
  /** How a value that is not among the options is shown (with `custom`). */
  display?: (value: AutoCompleteValue) => string
  /** Whether an option matches what was typed; by default its label or hint contains the text. */
  matches?: (option: AutoCompleteOption, query: string) => boolean
}>(), { inputmode: 'text', locale: undefined })
const model = defineModel<AutoCompleteValue | null | undefined>()
const emit = defineEmits<{ change: [value: AutoCompleteValue] }>()

const id = useId()
const field = ref<HTMLInputElement | null>(null)
const list = ref<HTMLUListElement | null>(null)
const query = ref('')
const open = ref(false)
const typed = ref(false)
const active = ref(-1)
const focused = ref(false)

const fold = (text: string) => text.normalize('NFC').toLocaleLowerCase().trim()
const selected = computed(() => props.options.find(option => option.value === model.value))
const selectedText = computed(() => selected.value?.label
  ?? (model.value != null && model.value !== '' && props.custom ? (props.display?.(model.value) ?? String(model.value)) : ''))
const shown = computed(() => {
  if (!typed.value || !query.value.trim()) return props.options
  const text = fold(query.value)
  return props.options.filter(option => props.matches
    ? props.matches(option, query.value)
    : fold(`${option.label} ${option.hint ?? ''}`).includes(text))
})
const locale = useFieldLocale(() => props.locale)
const noMatches = computed(() => locale.value === 'th' ? 'ไม่พบรายการที่ตรงกัน' : 'No matches')

watch(selectedText, value => { if (!focused.value || !typed.value) query.value = value }, { immediate: true })

/** The label split around the typed text, so the matching part can be marked. */
function parts(label: string) {
  const text = typed.value ? query.value.trim() : ''
  const at = text ? fold(label).indexOf(fold(text)) : -1
  return at < 0 ? [label, '', ''] : [label.slice(0, at), label.slice(at, at + text.length), label.slice(at + text.length)]
}

function show(start = true) {
  if (props.disabled) return
  open.value = true
  const index = shown.value.findIndex(option => option.value === model.value)
  active.value = index >= 0 ? index : start ? 0 : shown.value.length - 1
  // On opening, the current choice sits in the middle of the list, with its neighbours around it.
  void nextTick(() => reveal('center'))
}
function close() { open.value = false; active.value = -1 }
/** Scrolls only the list (scrollIntoView would also move the page) to show the active choice. */
function reveal(where: 'nearest' | 'center' = 'nearest') {
  const box = list.value
  const item = box?.querySelector<HTMLElement>(`[data-index="${active.value}"]`)
  if (!box || !item) return
  const top = item.offsetTop, bottom = top + item.offsetHeight
  if (where === 'center') box.scrollTop = top - (box.clientHeight - item.offsetHeight) / 2
  else if (top < box.scrollTop) box.scrollTop = top
  else if (bottom > box.scrollTop + box.clientHeight) box.scrollTop = bottom - box.clientHeight
}
function choose(option: AutoCompleteOption) {
  const changed = option.value !== model.value
  model.value = option.value
  query.value = option.label
  typed.value = false
  close()
  if (changed) emit('change', option.value)
}
/** Settles what was typed: a named option, a custom value, a cleared optional choice, or the last choice back. */
function commit() {
  if (!typed.value) { query.value = selectedText.value; return }
  const text = fold(query.value)
  const named = props.options.find(option => fold(option.label) === text)
  const value = named ? null : props.custom?.(query.value)
  const blank = props.options.find(option => option.value === '')
  if (named) choose(named)
  else if (value != null) {
    const changed = value !== model.value
    model.value = value
    typed.value = false
    query.value = selectedText.value
    if (changed) emit('change', value)
  } else if (!text && blank) choose(blank)
  else { typed.value = false; query.value = selectedText.value }
}

function onInput() {
  typed.value = true
  open.value = true
  active.value = shown.value.length ? 0 : -1
  // A complete custom value (a time typed in full) counts at once, so what depends on it follows the typing.
  const value = props.custom?.(query.value)
  if (value != null && /\d{2}\D?\d{2}$/.test(query.value.trim())) model.value = value
}
function onFocus() {
  focused.value = true
  field.value?.select()
}
function onBlur() {
  focused.value = false
  commit()
  close()
}
function move(step: number) {
  if (!open.value) return show(step > 0)
  if (!shown.value.length) return
  active.value = (active.value + step + shown.value.length) % shown.value.length
  void nextTick(reveal)
}
function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowDown') { event.preventDefault(); event.altKey ? show() : move(1) }
  else if (event.key === 'ArrowUp') { event.preventDefault(); move(-1) }
  else if (event.key === 'Enter' && open.value) {
    event.preventDefault()
    const option = shown.value[active.value]
    if (option) choose(option)
    else { commit(); close() }
  } else if (event.key === 'Escape' && (open.value || typed.value)) {
    event.preventDefault()
    typed.value = false
    query.value = selectedText.value
    close()
  } else if (event.key === 'Home' && open.value) { event.preventDefault(); active.value = 0; void nextTick(reveal) }
  else if (event.key === 'End' && open.value) { event.preventDefault(); active.value = shown.value.length - 1; void nextTick(reveal) }
}
function toggle() {
  if (open.value) close()
  else { typed.value = false; show() }
  field.value?.focus()
}
</script>

<template>
  <span class="autocomplete" :class="{ 'is-open': open }">
    <input :id="`${id}-field`" ref="field" v-model="query" class="autocomplete-field" type="text" role="combobox"
      autocomplete="off" spellcheck="false" :inputmode="inputmode" :aria-label="label" :placeholder="placeholder"
      :disabled="disabled" :required="required" aria-autocomplete="list" :aria-expanded="open" :aria-controls="`${id}-list`"
      :aria-activedescendant="open && active >= 0 ? `${id}-option-${active}` : undefined"
      @input="onInput" @focus="onFocus" @blur="onBlur" @keydown="onKeydown" @click="open || show()" />
    <button class="autocomplete-toggle" type="button" tabindex="-1" aria-hidden="true" :disabled="disabled"
      @mousedown.prevent @click="toggle">
      <svg viewBox="0 0 20 20"><path d="m5 7.5 5 5 5-5" /></svg>
    </button>
    <ul v-show="open" :id="`${id}-list`" ref="list" class="autocomplete-list" role="listbox">
      <li v-for="(option, index) in shown" :id="`${id}-option-${index}`" :key="String(option.value)" role="option"
        :data-index="index" :aria-selected="option.value === model" :class="{ 'is-active': index === active }"
        @mousedown.prevent @click="choose(option)" @mousemove="active = index">
        <span class="autocomplete-label"><template v-for="(part, at) in parts(option.label)" :key="at"><mark v-if="at === 1 && part">{{ part }}</mark><template v-else>{{ part }}</template></template></span>
        <small v-if="option.hint">{{ option.hint }}</small>
      </li>
      <li v-if="!shown.length" class="autocomplete-empty" role="presentation">{{ noMatches }}</li>
    </ul>
  </span>
</template>

<style scoped>
.autocomplete { display: block; min-width: 0; position: relative; width: 100%; }
.autocomplete-field { padding-right: 2.2rem !important; text-overflow: ellipsis; width: 100%; }
.autocomplete-field:disabled { cursor: not-allowed; opacity: .65; }
.autocomplete-toggle {
  align-items: center; background: none; border: 0; border-radius: .4rem; color: var(--color-muted); cursor: pointer;
  display: flex; height: 1.8rem; justify-content: center; padding: 0; position: absolute; right: .3rem; top: 50%;
  transform: translateY(-50%); width: 1.8rem;
}
.autocomplete-toggle:hover:not(:disabled) { background: var(--color-surface-muted); color: var(--color-ink); }
.autocomplete-toggle svg { fill: none; height: 1rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.7; transition: transform 150ms ease; width: 1rem; }
.is-open .autocomplete-toggle svg { transform: rotate(180deg); }
/* The list floats above the page, like the profile menu. */
.autocomplete-list {
  background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .6rem; box-shadow: var(--elev-2);
  left: 0; list-style: none; margin: .3rem 0 0; max-height: 16rem; min-width: 100%; overflow-y: auto; padding: .3rem;
  position: absolute; top: 100%; z-index: 40;
}
.autocomplete-list li {
  border-radius: .4rem; color: var(--color-ink); cursor: pointer; display: grid; font-size: .95rem; gap: .05rem;
  line-height: 1.35; padding: .5rem .6rem;
}
.autocomplete-list li small { color: var(--color-muted); font-size: .8rem; }
.autocomplete-list li.is-active { background: var(--color-brand-soft); }
.autocomplete-list li[aria-selected="true"] { color: var(--color-brand-ink); font-weight: 600; }
.autocomplete-list mark { background: none; color: inherit; font-weight: 700; text-decoration: underline; text-decoration-color: var(--color-brand); text-underline-offset: .2em; }
.autocomplete-list li.autocomplete-empty { color: var(--color-muted); cursor: default; }
@media (prefers-reduced-motion: reduce) { .autocomplete-toggle svg { transition: none; } }
</style>
