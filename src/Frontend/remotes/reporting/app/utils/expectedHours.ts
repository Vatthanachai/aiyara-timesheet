/**
 * What a month asks of a member: its working days (the organization's work week, less holidays)
 * times the normal working day (the working hours less the break), as minutes.
 */
export type WorkHours = { startTime: string; endTime: string; breakStart?: string | null; breakEnd?: string | null }
export type WorkSchedule = { workingDays: number[]; holidays: Set<string>; standardMinutes: number }

const minutesOf = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))

/** The working hours less the break; 8 hours when the organization's hours are unknown. */
export function standardMinutes(hours: WorkHours | null | undefined) {
  if (!hours) return 8 * 60
  const breakLength = hours.breakStart && hours.breakEnd ? minutesOf(hours.breakEnd) - minutesOf(hours.breakStart) : 0
  return minutesOf(hours.endTime) - minutesOf(hours.startTime) - breakLength
}

/** Every date of a month (`YYYY-MM`). */
export function monthDates(month: string) {
  const [year, number] = month.split('-').map(Number) as [number, number]
  const count = new Date(Date.UTC(year, number, 0)).getUTCDate()
  return Array.from({ length: count }, (_, index) => `${month}-${String(index + 1).padStart(2, '0')}`)
}

export const isWorkingDate = (date: string, schedule: WorkSchedule) =>
  schedule.workingDays.includes(new Date(`${date}T00:00:00Z`).getUTCDay()) && !schedule.holidays.has(date)

/** Minutes the month expects of one member, through `upTo` (inclusive) when given. */
export function expectedMinutes(month: string, schedule: WorkSchedule, upTo?: string) {
  return monthDates(month).filter(date => (!upTo || date <= upTo) && isWorkingDate(date, schedule)).length
    * schedule.standardMinutes
}
