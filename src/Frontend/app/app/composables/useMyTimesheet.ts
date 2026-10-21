import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'

export type MyEntry = {
  id: string
  date: string
  startTime: string
  endTime: string
  durationMinutes: number
  taskName: string
  projectName: string | null
  categoryName: string | null
}
export type MyLeave = { id: string; date: string; kind: string; notes: string | null }
export type MyHoliday = { date: string; name: string }
export type WorkHours = { startTime: string; endTime: string; breakStart: string | null; breakEnd: string | null }
type MyMonth = {
  year: number
  month: number
  locked: boolean
  entries: MyEntry[]
  leaves: MyLeave[]
  holidays: MyHoliday[]
  workingDays: number[]
  workHours: WorkHours
}

/** The signed-in member's week and month, read from Timesheet's `me/timesheet`. */
export type MyWeek = {
  today: string
  /** Monday to Sunday, as `YYYY-MM-DD`. */
  days: string[]
  entries: MyEntry[]
  leaves: MyLeave[]
  holidays: MyHoliday[]
  workingDays: number[]
  workHours: WorkHours
  /** The current month's entries, for its total. */
  monthEntries: MyEntry[]
  monthLocked: boolean
}

const pad = (value: number) => String(value).padStart(2, '0')
export const isoDate = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`

/** Monday to Sunday around `date`, in the browser's local calendar. */
export function weekOf(date: Date) {
  const monday = new Date(date.getFullYear(), date.getMonth(), date.getDate() - (date.getDay() + 6) % 7)
  return Array.from({ length: 7 }, (_, index) =>
    isoDate(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + index)))
}

export function useMyTimesheet() {
  const config = useRuntimeConfig()
  const authorizedFetch = useAuthorizedFetch()

  async function loadWeek(now = new Date()): Promise<MyWeek> {
    const today = isoDate(now)
    const days = weekOf(now)
    // A week can cross into the previous or next month; the current month always comes first.
    const months = [...new Set([today, ...days].map(day => day.slice(0, 7)))]
    const results = await Promise.all(months.map(async value => {
      const [year, month] = value.split('-').map(Number)
      return unwrapResponse(await authorizedFetch<ApiEnvelope<MyMonth>>(
        `${config.public.gatewayUrl}/api/v1/timesheets/me/timesheet?year=${year}&month=${month}`))
    }))
    const current = results[0]!
    const inWeek = <T extends { date: string }>(items: T[]) => items.filter(item => days.includes(item.date.slice(0, 10)))
    const all = <K extends 'entries' | 'leaves' | 'holidays'>(key: K) => results.flatMap(result => result[key]) as MyMonth[K]
    return {
      today, days,
      entries: inWeek(all('entries')),
      leaves: inWeek(all('leaves')),
      holidays: inWeek(all('holidays')),
      workingDays: current.workingDays?.length ? current.workingDays : [1, 2, 3, 4, 5],
      workHours: current.workHours ?? { startTime: '09:00', endTime: '17:00', breakStart: null, breakEnd: null },
      monthEntries: current.entries,
      monthLocked: current.locked
    }
  }

  return { loadWeek }
}
