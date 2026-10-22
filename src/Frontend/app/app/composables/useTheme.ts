import { applyTheme, type Theme, type ThemePreference } from '../../../shared/theme'

const storageKey = 'aiyara.theme'

/**
 * The member's theme: light, dark, or whatever the system uses. The choice is remembered in this
 * browser; `theme` is what it resolves to now, and follows the system while the choice is 'system'.
 * The head script in nuxt.config applies the remembered theme before the first paint.
 */
export function useTheme() {
  const preference = useState<ThemePreference>('theme-preference', () => 'system')
  const systemDark = useState('theme-system-dark', () => false)
  const theme = computed<Theme>(() =>
    preference.value === 'system' ? (systemDark.value ? 'dark' : 'light') : preference.value)

  function setPreference(value: ThemePreference) {
    preference.value = value
    try { localStorage.setItem(storageKey, value) } catch { /* The choice lasts for this visit. */ }
  }

  /** Reads the remembered choice and starts following the system; call once, from the layout. */
  function start() {
    try {
      const saved = localStorage.getItem(storageKey)
      if (saved === 'light' || saved === 'dark' || saved === 'system') preference.value = saved
    } catch { /* Storage can be blocked; the system's theme applies. */ }
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    systemDark.value = query.matches
    const follow = (event: MediaQueryListEvent) => { systemDark.value = event.matches }
    query.addEventListener('change', follow)
    const stop = watch(theme, applyTheme, { immediate: true })
    return () => { query.removeEventListener('change', follow); stop() }
  }

  return { preference: readonly(preference), theme, setPreference, start }
}
