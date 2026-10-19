import { inject, type InjectionKey, type Ref } from 'vue'
import { useLocaleText, type Locale } from '../../../../shared/i18n'

export type Entry = { id: string; date: string; startTime: string; endTime: string; durationMinutes: number; taskName: string; detail: string | null; notes: string | null; projectId: string | null; categoryId: string | null; personalTaskId: string | null }
export type Task = { id: string; name: string; status: string; projectId: string | null; categoryId: string | null }
export type Leave = { id: string; date: string; kind: string; notes: string | null }
export type CatalogItem = { id: string; name: string }
export type Catalog = { projects: CatalogItem[]; categories: CatalogItem[]; holidays: { id: string; date: string; name: string }[] }
export type TrayItem = { name: string; projectId?: string | null; categoryId?: string | null; personalTaskId?: string | null }
export type EntryForm = { date: string; startTime: string; endTime: string; taskName: string; detail: string; notes: string; projectId: string; categoryId: string; personalTaskId: string }

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
    planTimeOff: 'วางแผนวันหยุด', noLeavePlanned: 'ยังไม่มีวันลาในเดือนนี้'
  }, en: {
    title: 'Time entries', month: 'Month', task: 'Task', date: 'Date', start: 'Start', end: 'End', duration: 'Hours', detail: 'Detail', notes: 'Notes', project: 'Project', category: 'Category', save: 'Save', cancel: 'Cancel', edit: 'Edit', remove: 'Delete', add: 'Add entry', tasks: 'Personal tasks', leaves: 'Leave', kind: 'Kind', readOnly: 'Past months are read only', noEntries: 'No entries yet', status: 'Status', drop: 'Drag a task here to start an entry', chooseDay: 'Choose a day', use: 'Use', todo: 'To do', doing: 'Doing', done: 'Done', annual: 'Annual', sick: 'Sick', other: 'Other', holidays: 'Holidays', signIn: 'Please sign in through Aiyara Shell',
    subtitle: 'Log and review your work time in one place.', saved: 'Saved', confirmDeleteEntry: 'Delete this entry?',
    startFromSaved: 'Start from a saved task', taskHint: 'Save tasks here to reuse them.',
    planTimeOff: 'Plan time off', noLeavePlanned: 'No leave planned this month.'
  } })
  const labels = useLocaleText(locale, { th: {
    hoursLogged: 'ชั่วโมงที่บันทึก', daysTracked: 'วันที่มีรายการ', leaveDays: 'วันลา',
    previousMonth: 'เดือนก่อนหน้า', nextMonth: 'เดือนถัดไป', selectedDate: 'วันที่เลือก',
    estimate: 'รวมเวลารายการนี้', entryList: 'รายการเวลาทำงาน', allMonth: 'สรุปของเดือน',
    taskPlaceholder: 'เช่น ประชุมทีม ออกแบบหน้าจอ', detailPlaceholder: 'เพิ่มรายละเอียดที่ช่วยให้กลับมาทบทวนได้',
    notesPlaceholder: 'บันทึกข้อมูลเพิ่มเติม (ไม่บังคับ)', emptyTitle: 'ยังไม่มีเวลาที่บันทึก',
    emptyHint: 'เริ่มเพิ่มรายการแรกของเดือนนี้ได้จากแบบฟอร์มด้านบน'
  }, en: {
    hoursLogged: 'Hours logged', daysTracked: 'Days with entries', leaveDays: 'Leave days',
    previousMonth: 'Previous month', nextMonth: 'Next month', selectedDate: 'Selected date',
    estimate: 'Entry total', entryList: 'Time entries', allMonth: 'Month summary',
    taskPlaceholder: 'e.g. Team meeting, design review', detailPlaceholder: 'Add a detail that will help later',
    notesPlaceholder: 'Extra notes (optional)', emptyTitle: 'No time logged yet',
    emptyHint: 'Add your first entry for this month using the form above'
  } })
  return { text, labels }
}

export type TimesheetText = ReturnType<typeof useTimesheetText>

/** app.vue provides its copy once so each panel reads the same locale-keyed text. */
export const timesheetTextKey: InjectionKey<TimesheetText> = Symbol('timesheet-text')

export function injectTimesheetText() {
  const value = inject(timesheetTextKey)
  if (!value) throw new Error('Timesheet text was not provided.')
  return value
}
