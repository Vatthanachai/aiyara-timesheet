<script setup lang="ts">
import type { SupportedLocale } from '../composables/useLocale'

/**
 * The header's language switch: two options with their flags, a thumb that slides to the chosen
 * one. It is a radio group, so arrow keys move between the languages and only the chosen one is a
 * tab stop; each option is named in its own language.
 */
const { locale, setLocale } = useLocale()
const options: { value: SupportedLocale; code: string; name: string }[] = [
  { value: 'th', code: 'TH', name: 'ไทย' },
  { value: 'en', code: 'EN', name: 'English' }
]
const buttons = ref<HTMLButtonElement[]>([])
const flagId = useId()

function choose(value: SupportedLocale, focus = false) {
  setLocale(value)
  if (focus) void nextTick(() => buttons.value[options.findIndex(option => option.value === value)]?.focus())
}
function onKeydown(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return
  event.preventDefault()
  choose(locale.value === 'th' ? 'en' : 'th', true)
}
</script>

<template>
  <div class="locale-switch" role="radiogroup" aria-label="ภาษา / Language" :data-locale="locale" @keydown="onKeydown">
    <span class="locale-thumb" aria-hidden="true" />
    <button v-for="option in options" :key="option.value" ref="buttons" type="button" role="radio"
      :aria-checked="locale === option.value" :aria-label="option.name" :lang="option.value"
      :tabindex="locale === option.value ? 0 : -1" @click="choose(option.value)">
      <svg v-if="option.value === 'th'" class="flag" viewBox="0 0 30 20" aria-hidden="true">
        <rect width="30" height="20" fill="#A51931" />
        <rect y="3.33" width="30" height="13.34" fill="#F4F5F8" />
        <rect y="6.67" width="30" height="6.66" fill="#2D2A4A" />
      </svg>
      <svg v-else class="flag" viewBox="0 0 60 30" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
        <clipPath :id="`${flagId}-saltire`"><path d="M30,15 h30 v15 z v15 h-30 z h-30 v-15 z v-15 h30 z" /></clipPath>
        <rect width="60" height="30" fill="#012169" />
        <path d="M0,0 L60,30 M60,0 L0,30" stroke="#FFF" stroke-width="6" />
        <path d="M0,0 L60,30 M60,0 L0,30" :clip-path="`url(#${flagId}-saltire)`" stroke="#C8102E" stroke-width="4" />
        <path d="M30,0 v30 M0,15 h60" stroke="#FFF" stroke-width="10" />
        <path d="M30,0 v30 M0,15 h60" stroke="#C8102E" stroke-width="6" />
      </svg>
      <span class="locale-code">{{ option.code }}</span>
    </button>
  </div>
</template>

<style scoped>
.locale-switch {
  background: #eef1f5; border: 1px solid var(--color-border); border-radius: 999px; display: inline-grid;
  grid-template-columns: 1fr 1fr; padding: .2rem; position: relative;
}
.locale-thumb {
  background: var(--color-surface); border-radius: 999px; box-shadow: 0 1px 2px rgb(23 32 51 / 18%);
  inset: .2rem auto .2rem .2rem; position: absolute; transition: transform 180ms ease; width: calc(50% - .2rem);
}
.locale-switch[data-locale="en"] .locale-thumb { transform: translateX(100%); }
.locale-switch button {
  align-items: center; background: none; border: 0; border-radius: 999px; color: var(--color-muted); cursor: pointer;
  display: inline-flex; font: inherit; font-size: .82rem; font-weight: 600; gap: .4rem; justify-content: center;
  padding: .35rem .65rem; position: relative; z-index: 1;
}
.locale-switch button[aria-checked="true"] { color: var(--color-ink); }
.locale-switch button:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 1px; }
.flag { border-radius: 2px; box-shadow: 0 0 0 1px rgb(23 32 51 / 12%); flex: 0 0 auto; height: .8rem; width: 1.2rem; }
.locale-switch button[aria-checked="false"] .flag { opacity: .55; }
@media (prefers-reduced-motion: reduce) { .locale-thumb { transition: none; } }
/* On the narrowest phones the flags carry the switch; the names stay for assistive technology. */
@media (max-width: 22rem) {
  .locale-switch button { padding: .35rem .5rem; }
  .locale-code { display: none; }
}
</style>
