<script setup lang="ts">
import { computed } from 'vue'
import { parseTime } from '../date-format'
import AutoComplete, { type AutoCompleteOption, type AutoCompleteValue } from './AutoComplete.vue'

/**
 * A time of day, always in 24-hour HH:mm. It suggests every half hour, narrowed as digits are
 * typed (9 offers 09:00, 09:30, 19:00…), and takes any time typed as 9, 930, 9.30, or 09:30.
 * The model is `HH:mm`; a value read from the server as `HH:mm:ss` is shown without seconds.
 */
const props = withDefaults(defineProps<{ label?: string; required?: boolean; disabled?: boolean; locale?: 'th' | 'en'; step?: number }>(),
  { step: 30, locale: undefined })
const model = defineModel<string>({ required: true })
const emit = defineEmits<{ change: [value: string] }>()

const pad = (value: number) => String(value).padStart(2, '0')
const options = computed<AutoCompleteOption[]>(() => Array.from({ length: (24 * 60) / props.step }, (_, index) => {
  const minutes = index * props.step
  const text = `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`
  return { value: text, label: text }
}))
const digits = (text: string) => text.replace(/\D/g, '')
/** Typed digits match a time that starts with them. The hour's leading zero may be left out only where
 * the digits cannot begin a two-digit hour: 9 or 93 reach 09:…, but 13 is 13:00, not 01:30. */
const matches = (option: AutoCompleteOption, query: string) => {
  const typed = digits(query)
  const time = digits(option.label)
  const zeroLeftOut = typed.length === 1 || Number(typed[0]) >= 3
  return !typed || time.startsWith(typed) || (zeroLeftOut && time.startsWith(`0${typed}`))
}
const value = computed<AutoCompleteValue>({
  get: () => model.value?.slice(0, 5) ?? '',
  set: next => { model.value = String(next) }
})
</script>

<template>
  <AutoComplete v-model="value" class="time-field" :options="options" :label="label" :required="required" :disabled="disabled"
    :locale="locale" inputmode="numeric" placeholder="HH:mm" :custom="parseTime" :display="time => String(time)" :matches="matches"
    @change="next => emit('change', String(next))" />
</template>
