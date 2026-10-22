/**
 * Light and dark themes. The Shell resolves the member's choice (light, dark, or the system's)
 * to one of two themes and sends it to every remote in the `aiyara.session` message; each app
 * applies it as `data-theme` on <html>, which switches the tokens in tokens.css.
 */
export type Theme = 'light' | 'dark'
export type ThemePreference = Theme | 'system'

/** Reads the theme carried by the Shell's `aiyara.session` message, defaulting to light. */
export function readSessionTheme(data: unknown): Theme {
  return (data as { theme?: unknown } | null | undefined)?.theme === 'dark' ? 'dark' : 'light'
}

export function applyTheme(theme: Theme) {
  if (typeof document !== 'undefined') document.documentElement.dataset.theme = theme
}
