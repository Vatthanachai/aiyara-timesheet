import type { Locale } from './api-response'

/**
 * The organization's date and time formats, chosen by a Tenant Admin in Administration and
 * carried to every remote in the Shell's `aiyara.session` message. Keep the values in step with
 * Identity's `OrganizationFormats`: `bbbb` is the Buddhist year (Gregorian + 543) and `MMM` the
 * month's short name in the viewer's language.
 */
export const DATE_FORMATS = ['dd/MM/yyyy', 'dd/MM/bbbb', 'd MMM yyyy', 'yyyy-MM-dd', 'MM/dd/yyyy'] as const
export const TIME_FORMATS = ['HH:mm', 'h:mm a'] as const
export type DateFormat = typeof DATE_FORMATS[number]
export type TimeFormat = typeof TIME_FORMATS[number]
export type Formats = { dateFormat: DateFormat; timeFormat: TimeFormat }
export const DEFAULT_FORMATS: Formats = { dateFormat: 'dd/MM/yyyy', timeFormat: 'HH:mm' }

const pad = (value: number) => String(value).padStart(2, '0')
const intlLocale = (locale: Locale) => locale === 'th' ? 'th-TH-u-ca-gregory' : 'en-GB'

/** Reads the formats from a session message or organization, falling back to the defaults. */
export function readFormats(data: unknown): Formats {
  const value = data as { dateFormat?: unknown; timeFormat?: unknown } | null | undefined
  return {
    dateFormat: DATE_FORMATS.includes(value?.dateFormat as DateFormat) ? value!.dateFormat as DateFormat : DEFAULT_FORMATS.dateFormat,
    timeFormat: TIME_FORMATS.includes(value?.timeFormat as TimeFormat) ? value!.timeFormat as TimeFormat : DEFAULT_FORMATS.timeFormat
  }
}

/** A year as the format shows it: Buddhist for `bbbb`, Gregorian otherwise. */
export const displayYear = (year: number, formats: Formats) => formats.dateFormat.includes('bbbb') ? year + 543 : year

/** Formats a calendar date (`YYYY-MM-DD`, or the date part of a longer value). With `weekday`,
 * the day's short name in the viewer's language comes first. */
export function formatDate(value: string, formats: Formats, locale: Locale, options: { weekday?: boolean } = {}) {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number) as [number, number, number]
  const utc = new Date(Date.UTC(year, month - 1, day))
  const monthName = new Intl.DateTimeFormat(intlLocale(locale), { month: 'short', timeZone: 'UTC' }).format(utc)
  const text = formats.dateFormat
    .replace('yyyy', String(year)).replace('bbbb', String(year + 543))
    .replace('dd', pad(day)).replace(/\bd\b/, String(day))
    .replace('MMM', '\u0000').replace('MM', pad(month)).replace('\u0000', monthName)
  if (!options.weekday) return text
  return `${new Intl.DateTimeFormat(intlLocale(locale), { weekday: 'short', timeZone: 'UTC' }).format(utc)} ${text}`
}

/** Formats a clock time (`HH:mm` or `HH:mm:ss`). */
export function formatTime(value: string, formats: Formats) {
  const [hours, minutes] = value.split(':').map(Number) as [number, number]
  if (formats.timeFormat === 'HH:mm') return `${pad(hours)}:${pad(minutes)}`
  return `${hours % 12 || 12}:${pad(minutes)} ${hours < 12 ? 'AM' : 'PM'}`
}

/** Formats an instant (an ISO timestamp) as a date and time in the given time zone. */
export function formatDateTime(value: string, timeZone: string, formats: Formats, locale: Locale) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).formatToParts(new Date(value)).map(part => [part.type, part.value]))
  return `${formatDate(`${parts.year}-${parts.month}-${parts.day}`, formats, locale)} ${formatTime(`${parts.hour}:${parts.minute}`, formats)}`
}

/** A month (`YYYY-MM`) as "October 2026" / "ตุลาคม 2569", with the year style of the date format. */
export function formatMonth(value: string, formats: Formats, locale: Locale) {
  const [year, month] = value.split('-').map(Number) as [number, number]
  const name = new Intl.DateTimeFormat(intlLocale(locale), { month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)))
  return `${name} ${displayYear(year, formats)}`
}
