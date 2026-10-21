import type { Locale } from '../../../shared/api-response'

/** Minutes after midnight for `HH:mm` or `HH:mm:ss`. */
export const clockMinutes = (value: string) => {
  const [hours, minutes] = value.split(':').map(Number) as [number, number]
  return hours * 60 + minutes
}

/** A length of time as "5 h 30 m" / "5 ชม. 30 นาที". */
export function formatDuration(minutes: number, locale: Locale) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  const [h, m] = locale === 'th' ? ['ชม.', 'นาที'] : ['h', 'm']
  if (!hours) return `${rest} ${m}`
  return rest ? `${hours} ${h} ${rest} ${m}` : `${hours} ${h}`
}
