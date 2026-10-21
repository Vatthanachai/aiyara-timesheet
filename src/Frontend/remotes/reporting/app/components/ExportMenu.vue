<script setup lang="ts">
/**
 * A split button: the main part runs the default export, and the arrow opens a menu of the other
 * file types. The menu follows the WAI-ARIA menu button pattern: arrow keys move between items,
 * Escape closes it and returns focus to the arrow, and a click elsewhere closes it.
 */
const props = defineProps<{
  label: string
  moreLabel: string
  items: { key: string; label: string }[]
  /** Disables both parts while an export is being prepared. */
  busy?: boolean
}>()
const emit = defineEmits<{ run: []; select: [key: string] }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)
const toggle = ref<HTMLButtonElement | null>(null)
const itemButtons = ref<HTMLButtonElement[]>([])
const menuId = useId()

async function show(focusIndex = 0) {
  open.value = true
  await nextTick()
  itemButtons.value.at(focusIndex)?.focus()
}
function hide(returnFocus = false) {
  open.value = false
  if (returnFocus) toggle.value?.focus()
}
function choose(key: string) {
  hide(true)
  emit('select', key)
}
function moveFocus(event: KeyboardEvent) {
  const index = itemButtons.value.indexOf(event.target as HTMLButtonElement)
  const last = itemButtons.value.length - 1
  const next = event.key === 'ArrowDown' ? (index + 1) % (last + 1) : event.key === 'ArrowUp' ? (index - 1 + last + 1) % (last + 1)
    : event.key === 'Home' ? 0 : event.key === 'End' ? last : -1
  if (next < 0) return
  event.preventDefault()
  itemButtons.value[next]?.focus()
}
// Tabbing out of the menu closes it.
function onFocusOut(event: FocusEvent) {
  if (open.value && !root.value?.contains(event.relatedTarget as Node | null)) hide()
}
function onDocumentClick(event: MouseEvent) {
  if (open.value && !root.value?.contains(event.target as Node)) hide()
}
onMounted(() => document.addEventListener('click', onDocumentClick))
onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))
watch(() => props.busy, busy => { if (busy) hide() })
</script>

<template>
  <div ref="root" class="export-menu" @keydown.escape="open && hide(true)" @focusout="onFocusOut">
    <button type="button" class="export-main" :disabled="busy" @click="emit('run')">{{ label }}</button>
    <button ref="toggle" type="button" class="export-toggle" :disabled="busy" :aria-label="moreLabel" aria-haspopup="menu"
      :aria-expanded="open" :aria-controls="menuId" @click="open ? hide() : show()"
      @keydown.down.prevent="show(0)" @keydown.up.prevent="show(items.length - 1)">
      <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m5 7.5 5 5 5-5" /></svg>
    </button>
    <ul v-show="open" :id="menuId" class="export-items" role="menu" :aria-label="moreLabel" @keydown="moveFocus">
      <li v-for="item in items" :key="item.key" role="none">
        <button ref="itemButtons" type="button" role="menuitem" tabindex="-1" @click="choose(item.key)">{{ item.label }}</button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.export-menu { display: inline-flex; position: relative; }
.export-main { border-radius: .5rem 0 0 .5rem; }
.export-toggle { align-items: center; border-left: 0; border-radius: 0 .5rem .5rem 0; display: inline-flex; padding: .5rem .5rem; }
.export-toggle svg { fill: none; height: 1rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.8; transition: transform 150ms ease; width: 1rem; }
.export-toggle[aria-expanded="true"] svg { transform: rotate(180deg); }
.export-items {
  background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .5rem; box-shadow: 0 .75rem 2rem rgb(23 32 51 / 14%);
  list-style: none; margin: 0; min-width: 100%; padding: .3rem; position: absolute; right: 0; top: calc(100% + .35rem); z-index: 10;
}
.export-items button { background: none; border: 0; border-radius: .35rem; display: block; font-weight: 500; padding: .55rem .7rem; text-align: left; white-space: nowrap; width: 100%; }
.export-items button:hover, .export-items button:focus-visible { background: #f3f6fa; outline: none; }
@media (prefers-reduced-motion: reduce) { .export-toggle svg { transition: none; } }
</style>
