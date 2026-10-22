<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
import type { ThemePreference } from '../../../shared/theme'

/**
 * The header's theme switch: light, dark, or the system's, drawn as a sun, a moon and a screen.
 * Like the language switch it is a radio group: arrow keys move the choice and only the chosen
 * option is a tab stop. Each option is named for assistive technology and in its tooltip.
 */
const { locale } = useLocale()
const { preference, setPreference } = useTheme()
const text = useLocaleText(locale, {
  th: { theme: 'ธีม', light: 'สว่าง', dark: 'มืด', system: 'ตามระบบ' },
  en: { theme: 'Theme', light: 'Light', dark: 'Dark', system: 'System' }
})
const options: ThemePreference[] = ['light', 'dark', 'system']
const buttons = ref<HTMLButtonElement[]>([])

function choose(value: ThemePreference, focus = false) {
  setPreference(value)
  if (focus) void nextTick(() => buttons.value[options.indexOf(value)]?.focus())
}
function onKeydown(event: KeyboardEvent) {
  const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key]
  if (!step) return
  event.preventDefault()
  choose(options[(options.indexOf(preference.value) + step + options.length) % options.length]!, true)
}
</script>

<template>
  <div class="theme-switch" role="radiogroup" :aria-label="text.theme" :data-choice="preference" @keydown="onKeydown">
    <span class="theme-thumb" aria-hidden="true" />
    <button v-for="option in options" :key="option" ref="buttons" type="button" role="radio"
      :aria-checked="preference === option" :aria-label="text[option]" :title="text[option]"
      :tabindex="preference === option ? 0 : -1" @click="choose(option)">
      <svg viewBox="0 0 20 20" aria-hidden="true">
        <template v-if="option === 'light'">
          <circle cx="10" cy="10" r="3.4" />
          <path d="M10 2.5v1.8M10 15.7v1.8M2.5 10h1.8M15.7 10h1.8M4.7 4.7l1.3 1.3M14 14l1.3 1.3M4.7 15.3 6 14M14 6l1.3-1.3" />
        </template>
        <path v-else-if="option === 'dark'" d="M15.8 12.2A6.3 6.3 0 0 1 7.8 4.2a6.3 6.3 0 1 0 8 8Z" />
        <template v-else>
          <rect x="2.8" y="3.8" width="14.4" height="9.6" rx="1.6" />
          <path d="M7.5 16.5h5M10 13.4v3.1" />
        </template>
      </svg>
    </button>
  </div>
</template>

<style scoped>
.theme-switch {
  background: var(--color-surface-muted); border: 1px solid var(--color-border); border-radius: 999px; box-shadow: var(--inset-field);
  display: inline-grid; grid-template-columns: repeat(3, 1fr); padding: .2rem; position: relative;
}
.theme-thumb {
  background: var(--color-surface); border-radius: 999px; box-shadow: var(--elev-1);
  inset: .2rem auto .2rem .2rem; position: absolute; transition: transform 180ms ease; width: calc((100% - .4rem) / 3);
}
.theme-switch[data-choice="dark"] .theme-thumb { transform: translateX(100%); }
.theme-switch[data-choice="system"] .theme-thumb { transform: translateX(200%); }
.theme-switch button {
  align-items: center; background: none; border: 0; border-radius: 999px; color: var(--color-muted); cursor: pointer;
  display: inline-flex; justify-content: center; padding: .38rem .55rem; position: relative; z-index: 1;
}
.theme-switch button[aria-checked="true"] { color: var(--color-brand); }
.theme-switch button:hover { color: var(--color-ink); }
.theme-switch button:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 1px; }
.theme-switch svg { fill: none; height: 1.05rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.6; width: 1.05rem; }
.theme-switch button[aria-checked="true"] svg { fill: color-mix(in srgb, currentColor 18%, transparent); }
@media (prefers-reduced-motion: reduce) { .theme-thumb { transition: none; } }
@media (max-width: 22rem) { .theme-switch button { padding: .38rem .42rem; } }
</style>
