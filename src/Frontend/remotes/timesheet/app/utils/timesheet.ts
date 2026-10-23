import { inject, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import { useLocaleText, type Locale } from '../../../../shared/i18n'

export type Entry = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null; projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak?: boolean
  /** Set on an entry that exists only on this device so far (`pending`), or that the server refused (`failed`). */
  sync?: 'pending' | 'failed' }
/** Kanban columns in board order; the Timesheet API accepts exactly these task statuses. */
export const taskStatuses = ['todo', 'doing', 'done'] as const
export type TaskStatus = typeof taskStatuses[number]
export type Task = { id: string; name: string; status: TaskStatus; projectId: string | null; categoryId: string | null }
/** A day of leave; `minutes` is a part of the day (null for the whole day). */
export type Leave = { id: string; date: string; kind: string; notes: string | null; leaveTypeId?: string | null; minutes?: number | null }
export type LeaveType = { id: string; name: string; annualDays: number | null }
/** One leave type's calendar year, as `GET /me/leave-balance` returns it. */
export type LeaveBalance = { leaveTypeId: string; name: string; annualDays: number | null; usedDays: number; remainingDays: number | null }
export type LeaveAmount = 'day' | 'half' | 'hours'
export type CatalogItem = { id: string; name: string }
export type Catalog = { projects: CatalogItem[]; categories: CatalogItem[]; holidays: { id: string; date: string; name: string }[]; leaveTypes: LeaveType[]; workingDays: number[]; workHours: WorkHours }
/** When the organization's working day starts and ends, and its break (null for none), as HH:mm. */
export type WorkHours = { startTime: string; endTime: string; breakStart?: string | null; breakEnd?: string | null }
export const defaultWorkHours: WorkHours = { startTime: '09:00', endTime: '17:00' }
/** A member's month in the approval flow, as `GET /me/months/{year}/{month}` returns it. */
export type ApprovalStatus = 'open' | 'submitted' | 'approved' | 'returned'
export type MonthState = { locked: boolean; status: ApprovalStatus; returnReason: string | null; editable: boolean }

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

/** A member's saved entry, as `GET /entry-templates` returns it (times as HH:mm:ss; a deleted project or category is null). */
export type EntryTemplate = { id: string; name: string; taskName: string; startTime: string; endTime: string; projectId: string | null; categoryId: string | null; detail: string | null; notes: string | null; includesBreak: boolean }
/** Why a copy left a target day alone, as `POST /entries/copy` reports it. */
export type SkipReason = 'holiday' | 'day-off' | 'leave' | 'has-entries'
export type CopyResult = { created: Entry[]; skipped: { date: string; reason: SkipReason }[] }
/** A day the several-days form offers; `offDay` days (days off, holidays, whole-day leave) start unticked. */
export type BulkDay = { date: string; offDay: boolean; note: string }

/** `YYYY-MM-DD` dates counted in whole days, so no time zone moves them. */
export function addDays(day: string, days: number) {
  const date = new Date(`${day}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}
export const daysFrom = (start: string, end: string) => {
  const days: string[] = []
  for (let day = start; day <= end; day = addDays(day, 1)) days.push(day)
  return days
}
/** The Monday-to-Sunday week of a day, as the calendar draws it, kept within `min`–`max`. */
export function weekOf(day: string, min: string, max: string) {
  const monday = addDays(day, -((new Date(`${day}T00:00:00Z`).getUTCDay() + 6) % 7))
  const sunday = addDays(monday, 6)
  return { start: monday < min ? min : monday, end: sunday > max ? max : sunday }
}
/** The API's error code, and the days a `409 MONTH_CLOSED` names in `errors.dates`. */
export const errorCodeOf = (cause: unknown) => (cause as { data?: { errorCode?: string } } | null)?.data?.errorCode
export const closedDatesOf = (cause: unknown) => (cause as { data?: { errors?: { dates?: string[] } } } | null)?.data?.errors?.dates ?? []

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
    overtime: 'เกินเวลาทำงานปกติ', weekdays: 'จ.,อ.,พ.,พฤ.,ศ.,ส.,อา.',
    statusOpen: 'ยังไม่ส่ง', statusSubmitted: 'รออนุมัติ', statusApproved: 'อนุมัติแล้ว', statusReturned: 'ถูกส่งกลับ', statusLocked: 'ปิดรอบแล้ว',
    submitMonth: 'ส่งให้อนุมัติ', resubmitMonth: 'ส่งอีกครั้ง', returnReason: 'เหตุผล',
    submitHint: 'เมื่อบันทึกเดือนนี้ครบแล้ว ส่งให้ผู้ดูแลอนุมัติ', submittedHint: 'ส่งแล้ว ระหว่างรออนุมัติจะแก้ไขเดือนนี้ไม่ได้',
    approvedHint: 'เดือนนี้อนุมัติแล้วและแก้ไขไม่ได้อีก', returnedHint: 'ผู้ดูแลส่งกลับให้แก้ไข แก้ไขแล้วส่งอีกครั้ง', lockedHint: 'เดือนนี้ปิดรอบแล้ว',
    confirmSubmit: 'ส่ง{month}ให้อนุมัติ? ระหว่างรออนุมัติจะแก้ไขไม่ได้', submittedNotice: 'ส่งให้อนุมัติแล้ว',
    leaveAmount: 'ระยะเวลา', wholeDay: 'ทั้งวัน', halfDay: 'ครึ่งวัน', someHours: 'ระบุชั่วโมง', leaveHours: 'จำนวนชั่วโมง',
    leaveHoursShort: '{hours} ชม.', leaveBalance: 'วันลาปี {year}', balanceLeft: 'ใช้ {used} จาก {total} วัน เหลือ {left}', balanceTaken: 'ใช้ {used} วัน',
    copyPreviousDay: 'คัดลอกจากวันทำงานก่อนหน้า', copyPreviousWeek: 'คัดลอกจากสัปดาห์ก่อน', copyFrom: 'คัดลอกรายการของ {from} มาที่ {to}',
    copyHint: 'ข้ามวันหยุด วันลาทั้งวัน และวันที่มีรายการอยู่แล้ว', copyLabel: 'คัดลอกรายการ',
    copied: 'คัดลอกแล้ว {count} รายการ', copiedOne: 'คัดลอกแล้ว 1 รายการ', copiedNone: 'ไม่มีรายการที่คัดลอก', skipped: 'ข้าม',
    skipHoliday: 'วันหยุด', skipDayOff: 'วันหยุดประจำสัปดาห์', skipLeave: 'ลาทั้งวัน', skipHasEntries: 'มีรายการอยู่แล้ว',
    monthClosed: 'เดือนนี้แก้ไขไม่ได้', monthClosedDays: 'แก้ไขวันเหล่านี้ไม่ได้: {dates}', invalidTimeRange: 'เวลาสิ้นสุดต้องไม่ตรงกับเวลาเริ่ม',
    severalDays: 'หลายวัน', days: 'วัน', pickWeek: 'สัปดาห์นี้', pickMonth: 'ทั้งเดือน', pickNone: 'ล้าง',
    severalHint: 'เลือกวันทำงานไว้ให้แล้ว เพิ่มวันอื่นได้', daysChosen: 'เลือก {count} วัน',
    addToDays: 'เพิ่มใน {count} วัน', addedToDays: 'เพิ่มแล้วใน {count} วัน',
    template: 'แม่แบบ', fillFromTemplate: 'กรอกจากแม่แบบ', saveAsTemplate: 'บันทึกเป็นแม่แบบ', templateName: 'ชื่อแม่แบบ', saveTemplate: 'บันทึกแม่แบบ',
    templates: 'แม่แบบรายการ', templatesHint: 'กรอกแบบฟอร์มจากรายการที่บันทึกไว้', noTemplates: 'บันทึกรายการเป็นแม่แบบ เพื่อใช้ซ้ำ',
    templateSaved: 'บันทึกแม่แบบแล้ว', templateDeleted: 'ลบแม่แบบแล้ว', templateExists: 'มีแม่แบบชื่อ "{name}" อยู่แล้ว',
    saveName: 'บันทึกชื่อ', replaceWithForm: 'แทนที่ด้วยแบบฟอร์ม', confirmDeleteTemplate: 'ลบแม่แบบ "{name}"?', templateTaskFirst: 'กรอกชื่องานก่อน',
    templateMore: 'เพิ่มเติมสำหรับ {name}', useTemplate: 'ใช้ {name}',
    offlineNow: 'ไม่มีการเชื่อมต่อ การเปลี่ยนแปลงจะเก็บไว้ในเครื่องนี้และซิงก์เมื่อกลับมาออนไลน์', savedCopy: 'กำลังแสดงข้อมูลที่เก็บไว้ในเครื่องนี้',
    waiting: 'รอซิงก์ {count} รายการ', waitingOne: 'รอซิงก์ 1 รายการ', syncNow: 'ซิงก์ตอนนี้', syncing: 'กำลังซิงก์…', syncRetry: 'ติดต่อเซิร์ฟเวอร์ไม่ได้ จะลองอีกครั้ง',
    notSynced: 'ยังไม่ซิงก์', syncFailed: 'ไม่ถูกรับ', needsAttention: '{count} รายการต้องตรวจสอบ', needsAttentionOne: '1 รายการต้องตรวจสอบ',
    fixItem: 'แก้ไข', discardItem: 'ทิ้ง', confirmDiscardItem: 'ทิ้งการเปลี่ยนแปลงนี้?', synced: 'ซิงก์แล้ว {count} รายการ', syncedOne: 'ซิงก์แล้ว 1 รายการ',
    savedOffline: 'เก็บไว้ในเครื่องนี้แล้ว จะซิงก์เมื่อกลับมาออนไลน์', itemCreate: 'รายการใหม่', itemUpdate: 'แก้ไข', itemDelete: 'ลบ', itemTimer: 'ทิ้งตัวจับเวลา',
    noAccount: 'ต้องเชื่อมต่ออินเทอร์เน็ตอย่างน้อยหนึ่งครั้งจึงจะเปิดเวลาของคุณในเครื่องนี้ได้', entryGone: 'ไม่มีรายการนี้แล้ว', syncStatus: 'สถานะการซิงก์'
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
    overtime: 'Over the normal working day', weekdays: 'Mon,Tue,Wed,Thu,Fri,Sat,Sun',
    statusOpen: 'Not submitted', statusSubmitted: 'Submitted', statusApproved: 'Approved', statusReturned: 'Returned', statusLocked: 'Closed',
    submitMonth: 'Submit for approval', resubmitMonth: 'Submit again', returnReason: 'Reason',
    submitHint: 'When the month is complete, submit it for your admin to approve.', submittedHint: 'Waiting for approval. The month cannot be changed meanwhile.',
    approvedHint: 'This month is approved and can no longer be changed.', returnedHint: 'Your admin returned this month for changes. Update it, then submit it again.', lockedHint: 'This month is closed.',
    confirmSubmit: 'Submit {month} for approval? It cannot be changed while it waits.', submittedNotice: 'Submitted for approval',
    leaveAmount: 'How long', wholeDay: 'Whole day', halfDay: 'Half day', someHours: 'Some hours', leaveHours: 'Hours',
    leaveHoursShort: '{hours} h', leaveBalance: 'Leave in {year}', balanceLeft: '{used} of {total} days taken, {left} left', balanceTaken: '{used} days taken',
    copyPreviousDay: 'Copy previous working day', copyPreviousWeek: 'Copy previous week', copyFrom: 'Copy the entries of {from} onto {to}',
    copyHint: 'Days off, holidays, whole-day leave and days that already have entries are skipped.', copyLabel: 'Copy entries',
    copied: 'Copied {count} entries.', copiedOne: 'Copied 1 entry.', copiedNone: 'Nothing was copied.', skipped: 'Skipped',
    skipHoliday: 'Holiday', skipDayOff: 'Day off', skipLeave: 'Whole-day leave', skipHasEntries: 'Already has entries',
    monthClosed: 'This month cannot be changed.', monthClosedDays: 'These days cannot be changed: {dates}.', invalidTimeRange: 'The end time must differ from the start time.',
    severalDays: 'Several days', days: 'Days', pickWeek: 'This week', pickMonth: 'Whole month', pickNone: 'Clear',
    severalHint: 'Working days are chosen; add any other day.', daysChosen: '{count} days chosen',
    addToDays: 'Add to {count} days', addedToDays: 'Added to {count} days.',
    template: 'Template', fillFromTemplate: 'Fill from a template', saveAsTemplate: 'Save as template', templateName: 'Template name', saveTemplate: 'Save template',
    templates: 'Entry templates', templatesHint: 'Fill the entry form from a saved entry.', noTemplates: 'Save an entry as a template to reuse it.',
    templateSaved: 'Template saved.', templateDeleted: 'Template deleted.', templateExists: 'You already have a template called "{name}".',
    saveName: 'Save name', replaceWithForm: 'Replace with the form', confirmDeleteTemplate: 'Delete the template "{name}"?', templateTaskFirst: 'Enter the task first.',
    templateMore: 'More for {name}', useTemplate: 'Use {name}',
    offlineNow: 'You are offline. Changes are saved on this device and sync when you reconnect.', savedCopy: 'Showing the copy saved on this device.',
    waiting: '{count} changes waiting to sync', waitingOne: '1 change waiting to sync', syncNow: 'Sync now', syncing: 'Syncing…', syncRetry: 'The server could not be reached. Trying again soon.',
    notSynced: 'Not synced', syncFailed: 'Not accepted', needsAttention: '{count} changes need your attention', needsAttentionOne: '1 change needs your attention',
    fixItem: 'Fix', discardItem: 'Discard', confirmDiscardItem: 'Discard this change?', synced: 'Synced {count} changes.', syncedOne: 'Synced 1 change.',
    savedOffline: 'Saved on this device. It will sync when you are back online.', itemCreate: 'New entry', itemUpdate: 'Change', itemDelete: 'Delete', itemTimer: 'Discard timer',
    noAccount: 'Connect once to open your time on this device.', entryGone: 'The entry no longer exists.', syncStatus: 'Sync status'
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
