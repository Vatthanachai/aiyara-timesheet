import { computed, type ComputedRef } from 'vue'
import type { Locale } from './api-response'

/**
 * Shared Thai/English locale plumbing for the four remotes. Each remote keeps
 * its own vocabulary (the keys it needs differ), but all of them derive the
 * active locale from the same Shell `aiyara.session` postMessage and build a
 * locale-keyed text dictionary the same way. Pulling that pattern out here
 * stops every remote from re-deriving it independently and keeps template
 * code reading from dictionary keys (`text.value.save`) instead of scattering
 * one-off `locale.value === 'th' ? '...' : '...'` ternaries through handlers
 * and templates. `Locale` itself is re-exported from `api-response` so
 * callers need only one import for both.
 */
export type { Locale }

/** Reads the locale carried by the Shell's `aiyara.session` message, defaulting to Thai. */
export function readSessionLocale(data: unknown): Locale {
  return (data as { locale?: unknown } | null | undefined)?.locale === 'en' ? 'en' : 'th'
}

/**
 * Builds a computed text dictionary that switches with `locale`. Every string
 * the component displays should have a key here rather than an inline
 * ternary, so the Thai and English copy for a given concept lives in exactly
 * one place.
 */
export function useLocaleText<T extends Record<string, string>>(
  locale: { readonly value: Locale },
  dictionaries: { th: T; en: T }
): ComputedRef<T> {
  return computed(() => (locale.value === 'th' ? dictionaries.th : dictionaries.en))
}
