/**
 * Report periods as calendar dates (YYYY-MM-DD) in the tenant time zone, matching the server's
 * ReportCalendar: weeks start on Monday, months and years follow the calendar. `end` is exclusive.
 */
export type ReportKind = 'Weekly' | 'Monthly' | 'Annual' | 'Performance'
export type CalendarPeriod = { start: string; end: string }

const pad = (value: number) => String(value).padStart(2, '0')
const toDate = (text: string) => {
  const [year, month, day] = text.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day))
}
const toText = (date: Date) => `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`

export function addDays(text: string, days: number) {
  const date = toDate(text)
  date.setUTCDate(date.getUTCDate() + days)
  return toText(date)
}

/** Today's calendar date in `zone`. */
export function zonedToday(zone: string, now = new Date()) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', {
    timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(now).filter(part => part.type !== 'literal').map(part => [part.type, part.value]))
  return `${parts.year}-${parts.month}-${parts.day}`
}

export function weekPeriod(anyDay: string): CalendarPeriod {
  const offset = (toDate(anyDay).getUTCDay() + 6) % 7
  const start = addDays(anyDay, -offset)
  return { start, end: addDays(start, 7) }
}

export function monthPeriod(year: number, month: number): CalendarPeriod {
  const next = month === 12 ? { year: year + 1, month: 1 } : { year, month: month + 1 }
  return { start: `${year}-${pad(month)}-01`, end: `${next.year}-${pad(next.month)}-01` }
}

export function yearPeriod(year: number): CalendarPeriod {
  return { start: `${year}-01-01`, end: `${year + 1}-01-01` }
}

export function rangePeriod(from: string, to: string): CalendarPeriod {
  return { start: from, end: addDays(to, 1) }
}

/** The most recent complete period, which is what people usually report on. */
export function previousPeriod(kind: ReportKind, today: string): CalendarPeriod {
  const [year, month] = today.split('-').map(Number)
  if (kind === 'Weekly') return weekPeriod(addDays(today, -7))
  if (kind === 'Annual') return yearPeriod(year - 1)
  // Monthly and Performance both default to the previous calendar month.
  return month === 1 ? monthPeriod(year - 1, 12) : monthPeriod(year, month - 1)
}

/** UTC instant of local midnight on `text` in `zone` (handles DST by re-checking the offset). */
export function zonedMidnight(text: string, zone: string) {
  const target = toDate(text).getTime()
  const formatter = new Intl.DateTimeFormat('en-US', {
    timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  })
  let guess = target
  for (let attempt = 0; attempt < 2; attempt++) {
    const parts = Object.fromEntries(formatter.formatToParts(new Date(guess))
      .filter(part => part.type !== 'literal').map(part => [part.type, Number(part.value)]))
    guess += target - Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second)
  }
  return new Date(guess)
}

/** Converts a UTC instant back to the calendar date it falls on in `zone`. */
export function zonedDate(utc: string, zone: string) {
  return zonedToday(zone, new Date(utc))
}

/** "1 Sep 2026 – 30 Sep 2026" for an exclusive-end period. */
export function formatPeriod(period: CalendarPeriod, locale: string) {
  const format = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
  return `${format.format(toDate(period.start))} – ${format.format(toDate(addDays(period.end, -1)))}`
}
