import { inject, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import { useLocaleText, type Locale } from '../../../../shared/i18n'

export type Entry = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null; projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak?: boolean }
/** Kanban columns in board order; the Timesheet API accepts exactly these task statuses. */
export const taskStatuses = ['todo', 'doing', 'done'] as const
export type TaskStatus = typeof taskStatuses[number]
export type Task = { id: string; name: string; status: TaskStatus; projectId: string | null; categoryId: string | null }
export type Leave = { id: string; date: string; kind: string; notes: string | null }
export type CatalogItem = { id: string; name: string }
export type Catalog = { projects: CatalogItem[]; categories: CatalogItem[]; holidays: { id: string; date: string; name: string }[]; workingDays: number[]; workHours: WorkHours }
/** When the organization's working day starts and ends, and its break (null for none), as HH:mm. */
export type WorkHours = { startTime: string; endTime: string; breakStart?: string | null; breakEnd?: string | null }
export const defaultWorkHours: WorkHours = { startTime: '09:00', endTime: '17:00' }

const minutesOf = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
const overlap = (from: number, to: number, breakFrom: number, breakTo: number) =>
  Math.max(0, Math.min(to, breakTo) - Math.max(from, breakFrom))

/** Minutes of an entry inside the organization's break (both halves of an overnight entry);
 * the same rule as the Timesheet API's TimesheetRules.BreakOverlapMinutes. */
export function breakOverlapMinutes(startTime: string, endTime: string, hours: WorkHours) {
  if (!hours.breakStart || !hours.breakEnd || !startTime || !endTime) return 0
  const [start, end, breakStart, breakEnd] = [startTime, endTime, hours.breakStart, hours.breakEnd].map(minutesOf) as [number, number, number, number]
  return end > start ? overlap(start, end, breakStart, breakEnd)
    : overlap(start, 24 * 60, breakStart, breakEnd) + overlap(0, end, breakStart, breakEnd)
}

/** The minutes an entry counts as work: its length less the break, unless it was worked through. */
export function workedMinutes(form: Pick<EntryForm, 'startTime' | 'endTime' | 'includesBreak'>, hours: WorkHours) {
  if (!form.startTime || !form.endTime) return 0
  let minutes = minutesOf(form.endTime) - minutesOf(form.startTime)
  // Ends before it starts: it runs past midnight. A zero-length entry stays zero (the API refuses it).
  if (minutes < 0) minutes += 24 * 60
  return form.includesBreak ? minutes : minutes - breakOverlapMinutes(form.startTime, form.endTime, hours)
}
/** Minutes of the normal working day: the working hours less the break. */
export function standardMinutes(hours: WorkHours) {
  const breakLength = hours.breakStart && hours.breakEnd ? minutesOf(hours.breakEnd) - minutesOf(hours.breakStart) : 0
  return minutesOf(hours.endTime) - minutesOf(hours.startTime) - breakLength
}

/** Minutes as decimal hours without trailing zeros: 450 → "7.5", 420 → "7". */
export const decimalHours = (minutes: number) => (minutes / 60).toFixed(2).replace(/\.?0+$/, '')
export const clockMinutes = minutesOf

export type TrayItem = { name: string; projectId?: string | null; categoryId?: string | null; personalTaskId?: string | null }
export type EntryForm = { date: string; startTime: string; endTime: string; taskName: string; detail: string; notes: string; projectId: string; categoryId: string; personalTaskId: string; includesBreak: boolean }

const trayItemType = 'application/x-aiyara-task'

export function dragTrayItem(event: DragEvent, item: TrayItem) {
  event.dataTransfer?.setData(trayItemType, JSON.stringify(item))
}

export function readDroppedTrayItem(event: DragEvent): TrayItem | null {
  const value = event.dataTransfer?.getData(trayItemType)
  return value ? JSON.parse(value) as TrayItem : null
}

export function useTimesheetText(locale: Ref<Locale>) {
  const text = useLocaleText(locale, { th: {
    title: 'บันทึกเวลาทำงาน', month: 'เดือน', task: 'งาน', date: 'วันที่', start: 'เริ่ม', end: 'สิ้นสุด', duration: 'ชั่วโมง', detail: 'รายละเอียด', notes: 'หมายเหตุ', project: 'โครงการ', category: 'หมวดหมู่', save: 'บันทึก', cancel: 'ยกเลิก', edit: 'แก้ไข', remove: 'ลบ', add: 'เพิ่มรายการ', tasks: 'งานส่วนตัว', leaves: 'วันลา', kind: 'ประเภท', readOnly: 'เดือนที่ผ่านมาอ่านได้อย่างเดียว', noEntries: 'ยังไม่มีรายการ', status: 'สถานะ', drop: 'ลากงานมาที่แบบฟอร์มเพื่อเริ่มบันทึก', chooseDay: 'เลือกวัน', use: 'ใช้', todo: 'ต้องทำ', doing: 'กำลังทำ', done: 'เสร็จ', annual: 'พักร้อน', sick: 'ลาป่วย', other: 'อื่น ๆ', holidays: 'วันหยุด', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
    subtitle: 'บันทึกและติดตามเวลาทำงานของคุณในที่เดียว', saved: 'บันทึกแล้ว', confirmDeleteEntry: 'ลบรายการนี้?',
    startFromSaved: 'เริ่มจากงานที่บันทึกไว้', taskHint: 'เพิ่มงานที่ทำบ่อยไว้ใช้ซ้ำได้',
    planTimeOff: 'วางแผนวันหยุด', noLeavePlanned: 'ยังไม่มีวันลาในเดือนนี้',
    view: 'มุมมอง', entriesView: 'รายการเวลา', board: 'บอร์ดงาน', boardHint: 'ลากการ์ดไปคอลัมน์อื่นเพื่อเปลี่ยนสถานะ',
    newTask: 'เพิ่มงานใหม่', moveTo: 'ย้ายไป', logTime: 'บันทึกเวลา', emptyColumn: 'ไม่มีงาน',
    leaveAndHolidays: 'วันลาและวันหยุด', savedTasks: 'งานที่บันทึกไว้'
  }, en: {
    title: 'Time entries', month: 'Month', task: 'Task', date: 'Date', start: 'Start', end: 'End', duration: 'Hours', detail: 'Detail', notes: 'Notes', project: 'Project', category: 'Category', save: 'Save', cancel: 'Cancel', edit: 'Edit', remove: 'Delete', add: 'Add entry', tasks: 'Personal tasks', leaves: 'Leave', kind: 'Kind', readOnly: 'Past months are read only', noEntries: 'No entries yet', status: 'Status', drop: 'Drag a task here to start an entry', chooseDay: 'Choose a day', use: 'Use', todo: 'To do', doing: 'Doing', done: 'Done', annual: 'Annual', sick: 'Sick', other: 'Other', holidays: 'Holidays', signIn: 'Please sign in through Aiyara Shell',
    subtitle: 'Log and review your work time in one place.', saved: 'Saved', confirmDeleteEntry: 'Delete this entry?',
    startFromSaved: 'Start from a saved task', taskHint: 'Save tasks here to reuse them.',
    planTimeOff: 'Plan time off', noLeavePlanned: 'No leave planned this month.',
    view: 'View', entriesView: 'Entries', board: 'Task board', boardHint: 'Drag a card to another column to change its status.',
    newTask: 'New task', moveTo: 'Move to', logTime: 'Log time', emptyColumn: 'No tasks',
    leaveAndHolidays: 'Leave and holidays', savedTasks: 'Saved tasks'
  } })
  const labels = useLocaleText(locale, { th: {
    hoursLogged: 'ชั่วโมงที่บันทึก', daysTracked: 'วันที่มีรายการ', leaveDays: 'วันลา',
    previousMonth: 'เดือนก่อนหน้า', nextMonth: 'เดือนถัดไป', selectedDate: 'วันที่เลือก',
    workHours: 'เวลาทำงาน {start}–{end}', breakHours: 'พัก {start}–{end}',
    includesBreak: 'ทำงานในช่วงพัก {start}–{end} (นับเป็นเวลาทำงาน)', breakDeducted: 'หักเวลาพัก {minutes} นาที',
    entryInBreak: 'รายการนี้อยู่ในช่วงพักทั้งหมด ถ้าทำงานจริงให้เลือก "ทำงานในช่วงพัก"', estimate: 'รวมเวลารายการนี้', entryList: 'รายการเวลาทำงาน', allMonth: 'สรุปของเดือน',
    taskPlaceholder: 'เช่น ประชุมทีม ออกแบบหน้าจอ', detailPlaceholder: 'เพิ่มรายละเอียดที่ช่วยให้กลับมาทบทวนได้',
    notesPlaceholder: 'บันทึกข้อมูลเพิ่มเติม (ไม่บังคับ)', emptyTitle: 'ยังไม่มีเวลาที่บันทึก',
    emptyHint: 'เลือกวันในปฏิทิน แล้วเพิ่มรายการแรกของเดือนนี้จากแบบฟอร์ม',
    hoursUnit: 'ชม.', monthTotals: 'บันทึกแล้ว {hours} ชม. ใน {days} วัน', monthTotalsOneDay: 'บันทึกแล้ว {hours} ชม. ใน 1 วัน', monthLeave: ' ลา {count} วัน', monthLeaveOne: ' ลา 1 วัน', monthEmpty: 'เดือนนี้ยังไม่มีเวลาที่บันทึก',
    editEntry: 'แก้ไขรายการ', saveChanges: 'บันทึกการแก้ไข', entriesIn: 'รายการทั้งหมดใน{month}',
    dayEmpty: 'ยังไม่มีรายการในวันนี้', dayEmptyReadOnly: 'ไม่มีรายการในวันนี้', dayOff: 'วันหยุดประจำสัปดาห์',
    overtime: 'เกินเวลาทำงานปกติ', weekdays: 'จ.,อ.,พ.,พฤ.,ศ.,ส.,อา.'
  }, en: {
    hoursLogged: 'Hours logged', daysTracked: 'Days with entries', leaveDays: 'Leave days',
    previousMonth: 'Previous month', nextMonth: 'Next month', selectedDate: 'Selected date',
    workHours: 'Working hours {start}–{end}', breakHours: 'break {start}–{end}',
    includesBreak: 'Worked through the {start}–{end} break (count it as work)', breakDeducted: '{minutes} min break deducted',
    entryInBreak: 'This entry is entirely in the break. If you worked then, tick "Worked through the break".', estimate: 'Entry total', entryList: 'Time entries', allMonth: 'Month summary',
    taskPlaceholder: 'e.g. Team meeting, design review', detailPlaceholder: 'Add a detail that will help later',
    notesPlaceholder: 'Extra notes (optional)', emptyTitle: 'No time logged yet',
    emptyHint: 'Choose a day on the calendar, then add your first entry for this month with the form.',
    hoursUnit: 'h', monthTotals: '{hours} h logged over {days} days', monthTotalsOneDay: '{hours} h logged on 1 day', monthLeave: ', {count} days of leave', monthLeaveOne: ', 1 day of leave', monthEmpty: 'No time logged this month yet',
    editEntry: 'Edit entry', saveChanges: 'Save changes', entriesIn: 'Entries in {month}',
    dayEmpty: 'Nothing logged on this day yet', dayEmptyReadOnly: 'Nothing logged on this day', dayOff: 'Day off',
    overtime: 'Over the normal working day', weekdays: 'Mon,Tue,Wed,Thu,Fri,Sat,Sun'
  } })
  return { text, labels }
}

export type TimesheetText = ReturnType<typeof useTimesheetText>

/** app.vue provides its copy once so each panel reads the same locale-keyed text. */
export const timesheetTextKey: InjectionKey<TimesheetText> = Symbol('timesheet-text')

/** Dates and times in the organization's formats, which app.vue provides from the session. */
export type TimesheetDates = ComputedRef<{ date: (value: string) => string; time: (value: string) => string }>
export const timesheetDatesKey: InjectionKey<TimesheetDates> = Symbol('timesheet-dates')

export function injectTimesheetDates() {
  const value = inject(timesheetDatesKey)
  if (!value) throw new Error('Timesheet dates were not provided.')
  return value
}

export function injectTimesheetText() {
  const value = inject(timesheetTextKey)
  if (!value) throw new Error('Timesheet text was not provided.')
  return value
}
