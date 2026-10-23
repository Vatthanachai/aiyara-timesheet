<script setup lang="ts">
import { fieldErrors, safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { formatHours, formatMoney } from '../../../shared/money'
import { billingBody, validateBilling, type BillingDraft, type BillingField, type BillingProblem } from './utils/billing'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, useLocaleText, type Locale, fieldLocaleKey } from '../../../shared/i18n'
import AutoComplete from '../../../shared/components/AutoComplete.vue'
import DateField from '../../../shared/components/DateField.vue'
import TimeField from '../../../shared/components/TimeField.vue'
import { applyTheme, readSessionTheme } from '../../../shared/theme'
import MfaRequirement from './components/MfaRequirement.vue'
import MemberMfa from './components/MemberMfa.vue'
import MemberMfaReset from './components/MemberMfaReset.vue'
import MemberAuditLog from './components/MemberAuditLog.vue'
import { DATE_FORMATS, TIME_FORMATS, formatDate as formatCalendarDate, formatTime, readFormats, type Formats } from '../../../shared/date-format'
type Member = { id: string; firstName: string; lastName: string; email: string; role: string; status: string; isCurrentUser: boolean; groupId: string | null; groupName: string | null; mfaEnabled?: boolean }
type MemberGroup = { id: string; name: string; memberCount: number }
type Invitation = { id: string; email: string; role: string; invitedByEmail: string | null; createdAtUtc: string; expiresAtUtc: string; groupName?: string | null }
/** A project with its billing details; a catalog from an older API leaves them out. */
type Project = { id: string; name: string; client?: string | null; isBillable?: boolean; hourlyRate?: number | null; currencyCode?: string | null; budgetHours?: number | null }
type Catalog = { projects: Project[]; categories: { id: string; name: string }[]; holidays: { id: string; date: string; name: string }[]; leaveTypes?: LeaveType[]; workingDays: number[]; workHours?: WorkHours }
/** A leave type and its days a year; null days is no limit. */
type LeaveType = { id: string; name: string; annualDays: number | null }
type WorkHours = { startTime: string; endTime: string; breakStart?: string | null; breakEnd?: string | null }
const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
provide(fieldLocaleKey, locale)
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [], workingDays: [1, 2, 3, 4, 5] })
const projectName = ref('')
const categoryName = ref('')
const holidayName = ref('')
const holidayDate = ref('')
const leaveTypeName = ref('')
/** Days a year as typed; empty is no limit. */
const leaveTypeDays = ref<number | ''>('')
// Weekday numbers follow Date.getDay() (Sunday = 0); the panel lists Monday first.
const weekdays = [1, 2, 3, 4, 5, 6, 0]
const workingDays = ref<number[]>([1, 2, 3, 4, 5])
const workHours = reactive<WorkHours>({ startTime: '09:00', endTime: '17:00' })
// The break being edited; it is saved only while hasBreak is on.
const hasBreak = ref(false)
const breakTime = reactive({ start: '12:00', end: '13:00' })
// HH:mm strings compare in clock order.
const hoursValid = computed(() => workHours.startTime < workHours.endTime)
const breakValid = computed(() => !hasBreak.value || workHours.startTime <= breakTime.start &&
  breakTime.start < breakTime.end && breakTime.end <= workHours.endTime)
/** The working day less the break, for the panel's summary ("8 h a day"). */
const workdayMinutes = computed(() => {
  const minutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
  return minutes(workHours.endTime) - minutes(workHours.startTime) -
    (hasBreak.value ? minutes(breakTime.end) - minutes(breakTime.start) : 0)
})
function showWorkHours(value: WorkHours) {
  Object.assign(workHours, { startTime: value.startTime, endTime: value.endTime })
  hasBreak.value = Boolean(value.breakStart && value.breakEnd)
  if (value.breakStart && value.breakEnd) Object.assign(breakTime, { start: value.breakStart, end: value.breakEnd })
}
const isSavingWorkWeek = ref(false)
/** Emails about time not logged, sent at `sendAt` (HH:mm) in the organization's time zone. */
const reminders = reactive({ dailyEnabled: false, monthEndEnabled: false, sendAt: '09:00' })
// Loaded once, so a catalog reload does not discard unsaved reminder edits.
let remindersLoaded = false
const isSavingReminders = ref(false)
const error = ref('')
const notice = ref('')
const isLoading = ref(false)
const isSavingPolicy = ref(false)
const isLocking = ref(false)
const policy = reactive({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
const tenantId = ref('')
const lockMonth = ref(new Date().toISOString().slice(0, 7))
const members = ref<Member[]>([])
const invitations = ref<Invitation[]>([])
const groups = ref<MemberGroup[]>([])
const groupName = ref('')
// The member or invitation whose request is running, and the member awaiting deactivation confirmation.
const busyId = ref('')
const confirmingId = ref('')
// The member whose MFA reset form is open.
const mfaResettingId = ref('')
const inviteEmail = ref('')
const inviteRole = ref<'Employee' | 'TenantAdmin'>('Employee')
const inviteGroup = ref('')
const isInviting = ref(false)
type Organization = { name: string; organizationCode: string; logoUrl: string | null; dateFormat?: string; timeFormat?: string; requireMfa?: boolean }
const organization = ref<Organization | null>(null)
const organizationName = ref('')
const logoPreview = ref('')
const isSavingBrand = ref(false)
// The organization's formats as the Shell sent them, and the choices being edited.
const formats = ref<Formats>(readFormats(null))
const formatForm = reactive<Formats>(readFormats(null))
const formatPreview = computed(() => {
  const now = new Date()
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  return `${formatCalendarDate(today, formatForm, locale.value)} ${formatTime(`${now.getHours()}:${now.getMinutes()}`, formatForm)}`
})
const copy = useLocaleText(locale, { th: {
  title: 'จัดการองค์กร', subtitle: 'จัดระเบียบโครงการ หมวดหมู่ วันหยุด และข้อกำหนดการใช้งานในที่เดียว', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
  settingsTabs: 'หมวดการตั้งค่า', tabOrganization: 'องค์กร', tabPeople: 'สมาชิก', tabTime: 'เวลาทำงาน', tabCatalog: 'โครงการ|และหมวดหมู่', tabSecurity: 'ความปลอดภัย', tabAudit: 'ประวัติ|การเปลี่ยนแปลง',
  organizationCode: 'รหัสองค์กร {code}', dayOff: 'วันหยุด', member: 'สมาชิก', status: 'สถานะ', actions: 'การดำเนินการ',
  projects: 'โครงการ', projectName: 'ชื่อโครงการ', categories: 'หมวดหมู่',
  categoryName: 'ชื่อหมวดหมู่', holidays: 'วันหยุด', holidayName: 'ชื่อวันหยุด',
  date: 'วันที่', add: 'เพิ่มรายการ', save: 'บันทึก', delete: 'ลบ', policy: 'นโยบายรหัสผ่าน',
  tenantId: 'รหัสองค์กร', minimumLength: 'ความยาวขั้นต่ำ (12–128)', expiryDays: 'อายุรหัสผ่าน (1–3650 วัน)',
  lengthUnit: 'ตัวอักษร', expiryUnit: 'วัน',
  uppercase: 'ตัวพิมพ์ใหญ่', lowercase: 'ตัวพิมพ์เล็ก', digit: 'ตัวเลข', symbol: 'สัญลักษณ์',
  lockMonth: 'ปิดรอบเดือน', lock: 'ปิดรอบเดือน', loadedError: 'โหลดข้อมูลไม่สำเร็จ',
  saveError: 'บันทึกไม่สำเร็จ (ต้องเป็นผู้ดูแล)', deleteError: 'ลบไม่สำเร็จ',
  updateError: 'แก้ไขไม่สำเร็จ', policySaved: 'บันทึกนโยบายแล้ว',
  policyError: 'บันทึกนโยบายไม่สำเร็จ', policyInvalid: 'ความยาวขั้นต่ำต้องอยู่ระหว่าง 12–128 ตัวอักษร และอายุรหัสผ่านต้องอยู่ระหว่าง 1–3650 วัน', locked: 'ปิดรอบเดือนแล้ว', lockError: 'ปิดรอบเดือนไม่สำเร็จ',
  projectHint: 'จัดการโครงการที่ทีมใช้บันทึกเวลาทำงาน', categoryHint: 'กำหนดประเภทงานให้เลือกใน timesheet', holidayHint: 'วันหยุดจะแสดงในปฏิทินของทีม',
  empty: 'ยังไม่มีรายการ', loading: 'กำลังโหลดข้อมูล…', policyHint: 'กำหนดมาตรฐานรหัสผ่านสำหรับสมาชิกในองค์กร',
  lockHint: 'เมื่อปิดรอบแล้ว สมาชิกจะไม่สามารถแก้ไข timesheet ของเดือนนั้นได้',
  countProjects: 'โครงการทั้งหมด', countCategories: 'หมวดหมู่ทั้งหมด', countHolidays: 'วันหยุดที่กำหนด',
  profile: 'องค์กรปัจจุบัน', passwordRequirements: 'ข้อกำหนดรหัสผ่าน', passwordLength: 'จำนวนตัวอักษร',
  savePolicy: 'บันทึกนโยบาย', retry: 'ลองโหลดอีกครั้ง',
  organization: 'องค์กร', organizationHint: 'ชื่อและโลโก้ที่สมาชิกทุกคนเห็นที่มุมซ้ายบน',
  organizationName: 'ชื่อองค์กร', saveName: 'บันทึกชื่อ', logo: 'โลโก้', uploadLogo: 'อัปโหลดโลโก้', replaceLogo: 'เปลี่ยนโลโก้',
  removeLogo: 'ลบโลโก้', noLogo: 'ยังไม่มีโลโก้', logoHint: 'PNG, JPEG หรือ WebP ขนาดไม่เกิน 2 MB แนะนำพื้นหลังโปร่งใส',
  nameSaved: 'บันทึกชื่อองค์กรแล้ว', logoSaved: 'อัปเดตโลโก้แล้ว', logoRemoved: 'ลบโลโก้แล้ว',
  invalidLogo: 'ไฟล์โลโก้ต้องเป็น PNG, JPEG หรือ WebP ขนาดไม่เกิน 2 MB',
  members: 'สมาชิก', memberHint: 'ดูสมาชิกขององค์กรและเชิญคนใหม่ด้วยอีเมล', inviteEmail: 'อีเมลผู้ได้รับเชิญ',
  role: 'บทบาท', roleEmployee: 'พนักงาน', roleTenantAdmin: 'ผู้ดูแลองค์กร', invite: 'ส่งคำเชิญ', inviting: 'กำลังส่ง…',
  invited: 'ส่งคำเชิญถึง {email} แล้ว',
  makeAdmin: 'ตั้งเป็นผู้ดูแล', makeEmployee: 'ตั้งเป็นพนักงาน', deactivate: 'ปิดการใช้งาน', confirmDeactivate: 'ยืนยันปิดการใช้งาน',
  cancel: 'ยกเลิก', reactivate: 'เปิดใช้งานอีกครั้ง', resendActivation: 'ส่งรหัสเปิดใช้งานอีกครั้ง',
  pendingInvitations: 'คำเชิญที่รอตอบรับ', noInvitations: 'ไม่มีคำเชิญที่รอตอบรับ', expires: 'หมดอายุ {date}', invitedBy: 'เชิญโดย {email}',
  resendInvitation: 'ส่งคำเชิญใหม่', revokeInvitation: 'ยกเลิกคำเชิญ',
  roleChanged: 'เปลี่ยนบทบาทของ {email} แล้ว', deactivated: 'ปิดการใช้งาน {email} แล้ว และออกจากระบบทุกอุปกรณ์',
  reactivated: 'เปิดใช้งาน {email} อีกครั้งแล้ว', activationResent: 'ส่งรหัสเปิดใช้งานถึง {email} แล้ว',
  invitationResent: 'ส่งคำเชิญใหม่ถึง {email} แล้ว รหัสเดิมใช้ไม่ได้อีก', invitationRevoked: 'ยกเลิกคำเชิญของ {email} แล้ว',
  lastAdmin: 'องค์กรต้องมีผู้ดูแลที่ใช้งานอยู่อย่างน้อยหนึ่งคน', selfChange: 'คุณเปลี่ยนบทบาทหรือสถานะของตัวเองไม่ได้',
  deliveryUnavailable: 'บันทึกแล้ว แต่ส่งอีเมลไม่สำเร็จ กรุณาส่งรหัสเปิดใช้งานอีกครั้งภายหลัง',
  memberChanged: 'ข้อมูลสมาชิกเปลี่ยนไปแล้ว รายการถูกโหลดใหม่ กรุณาลองอีกครั้ง',
  inviteConflict: 'อีเมลนี้เป็นสมาชิกขององค์กรนี้อยู่แล้ว หรือเป็นบัญชีผู้ดูแลระบบ',
  statusActive: 'ใช้งานอยู่', statusPendingActivation: 'รอเปิดใช้งาน', statusDeactivated: 'ปิดการใช้งาน', you: 'คุณ',
  groups: 'กลุ่มสมาชิก', groupHint: 'จัดสมาชิกเป็นกลุ่ม เช่น พนักงานประจำ Outsource หรือสัญญาจ้าง เพื่อคัดกรองและดูรายงานรายกลุ่ม กลุ่มไม่มีผลต่อสิทธิ์การใช้งาน',
  groupName: 'ชื่อกลุ่ม', addGroup: 'เพิ่มกลุ่ม', noGroups: 'ยังไม่มีกลุ่ม', group: 'กลุ่ม', noGroup: 'ไม่มีกลุ่ม',
  groupMembers: 'สมาชิก {count} คน', groupCreated: 'เพิ่มกลุ่ม {name} แล้ว', groupRenamed: 'เปลี่ยนชื่อกลุ่มเป็น {name} แล้ว', groupDeleted: 'ลบกลุ่ม {name} แล้ว',
  groupChanged: 'ย้าย {email} ไปกลุ่มใหม่แล้ว', groupNameTaken: 'มีกลุ่มชื่อนี้อยู่แล้ว', groupInUse: 'ยังมีสมาชิกในกลุ่มนี้ กรุณาย้ายสมาชิกออกก่อนลบ',
  groupNameInvalid: 'ชื่อกลุ่มต้องมี 1–100 ตัวอักษร',
  pickDate: 'เลือกจากปฏิทิน',
  formatsTitle: 'รูปแบบวันที่และเวลา', formatsHint: 'วันที่และเวลาทุกหน้าและในรายงานของทุกคนในองค์กรจะแสดงตามรูปแบบนี้',
  dateFormat: 'รูปแบบวันที่', timeFormat: 'รูปแบบเวลา', formatPreview: 'ตัวอย่าง', saveFormats: 'บันทึกรูปแบบ', formatsSaved: 'บันทึกรูปแบบวันที่และเวลาแล้ว',
  buddhistYear: 'ปี พ.ศ.', monthName: 'ชื่อเดือน', hours24: '24 ชั่วโมง', hours12: '12 ชั่วโมง (AM/PM)',
  workWeek: 'วันและเวลาทำงาน', workWeekHint: 'เลือกวันที่องค์กรทำงาน วันอื่นจะแสดงเป็นวันหยุดประจำสัปดาห์ในปฏิทินของทีม และกำหนดเวลาเริ่มและเลิกงาน ซึ่งจะเป็นเวลาตั้งต้นของรายการใหม่',
  workDays: 'วันทำงาน', workStart: 'เวลาเริ่มงาน', workEnd: 'เวลาเลิกงาน', hoursOrder: 'เวลาเลิกงานต้องอยู่หลังเวลาเริ่มงาน',
  hasBreak: 'มีเวลาพัก (ไม่นับเป็นเวลาทำงาน)', breakStart: 'เริ่มพัก', breakEnd: 'สิ้นสุดพัก', breakOrder: 'เวลาพักต้องอยู่ในเวลาทำงาน และสิ้นสุดหลังเวลาเริ่มพัก',
  workdayLength: 'ทำงานปกติวันละ {hours} ชั่วโมง เกินจากนี้นับเป็นวันทำงานล่วงเวลาในรายงาน',
  leaveTypes: 'ประเภทการลา', leaveTypeHint: 'สมาชิกเลือกประเภทเมื่อบันทึกวันลา และเห็นวันลาที่เหลือของปี เว้นจำนวนวันว่างไว้ถ้าไม่จำกัด',
  leaveTypeName: 'ชื่อประเภท', annualDays: 'วันต่อปี', unlimited: 'ไม่จำกัด', leaveTypeExists: 'มีประเภทการลาชื่อนี้อยู่แล้ว',
  noLeaveTypes: 'ยังไม่มีประเภทการลา สมาชิกเลือกได้จาก พักร้อน ลาป่วย หรืออื่น ๆ',
  reminders: 'การแจ้งเตือน', reminderHint: 'ส่งอีเมลเตือนสมาชิกที่ยังไม่ได้ลงเวลา ตามเวลาที่ตั้งไว้ในเขตเวลาขององค์กร โดยข้ามวันหยุด วันที่ไม่ทำงาน และวันลา',
  dailyReminder: 'เตือนรายวัน', dailyReminderHint: 'ทุกวันทำงาน ถึงสมาชิกที่ไม่ได้ลงเวลาหรือวันลาในวันทำงานก่อนหน้า',
  monthEndReminder: 'เตือนสิ้นเดือน', monthEndReminderHint: 'วันทำงานสุดท้ายของเดือน ถึงสมาชิกที่ชั่วโมงยังไม่ครบตามที่ควรลงถึงเมื่อวาน',
  sendAt: 'เวลาส่ง', saveReminders: 'บันทึกการแจ้งเตือน', remindersSaved: 'บันทึกการแจ้งเตือนแล้ว',
  saveWorkWeek: 'บันทึกวันและเวลาทำงาน', workWeekSaved: 'บันทึกวันและเวลาทำงานแล้ว', workWeekRequired: 'ต้องมีวันทำงานอย่างน้อยหนึ่งวัน',
  billing: 'การเรียกเก็บเงินและงบประมาณ', hideBilling: 'ซ่อนการเรียกเก็บเงิน', billingHint: 'ผู้ดูแลองค์กรเท่านั้นที่เห็นอัตราค่าบริการ รายงานคำนวณจากอัตราปัจจุบัน หากเปลี่ยนอัตรา เวลาที่บันทึกไว้แล้วจะคำนวณใหม่ด้วยอัตรานั้น',
  client: 'ลูกค้า', clientHint: 'ไม่บังคับ ไม่เกิน 200 ตัวอักษร', billable: 'เรียกเก็บเงินได้', nonBillable: 'เรียกเก็บเงินไม่ได้', billableSwitch: 'โครงการนี้เรียกเก็บเงินได้',
  hourlyRate: 'อัตราต่อชั่วโมง', rateHint: '0–1,000,000 ทศนิยมไม่เกิน 2 ตำแหน่ง', currency: 'สกุลเงิน', currencyHint: 'รหัส 3 ตัวอักษร เช่น THB ต้องระบุเมื่อกำหนดอัตรา',
  budgetHours: 'งบประมาณชั่วโมง', budgetHint: 'ชั่วโมงรวมตลอดโครงการ มากกว่า 0 ถึง 1,000,000 เกินงบแล้วระบบเตือนเท่านั้น ไม่ห้ามบันทึกเวลา',
  perHour: 'ต่อชั่วโมง', budgetOf: 'งบ {hours} ชั่วโมง', saveBilling: 'บันทึกข้อมูลการเรียกเก็บเงิน', billingSaved: 'บันทึกข้อมูลการเรียกเก็บเงินของ {name} แล้ว',
  billingInvalid: 'บันทึกไม่สำเร็จ กรุณาแก้ช่องที่มีเครื่องหมายแล้วลองใหม่',
  rateInvalid: 'อัตราต้องเป็นตัวเลข 0–1,000,000 ทศนิยมไม่เกิน 2 ตำแหน่ง', currencyInvalid: 'สกุลเงินต้องเป็นตัวอักษร 3 ตัว เช่น THB', currencyRequired: 'ต้องระบุสกุลเงินเมื่อกำหนดอัตรา',
  budgetInvalid: 'งบประมาณต้องมากกว่า 0 ถึง 1,000,000 ชั่วโมง ทศนิยมไม่เกิน 2 ตำแหน่ง', clientInvalid: 'ชื่อลูกค้าต้องไม่เกิน 200 ตัวอักษร',
  billingForbidden: 'เฉพาะผู้ดูแลองค์กรเท่านั้นที่แก้ข้อมูลการเรียกเก็บเงินได้', billingGone: 'ไม่พบโครงการนี้ อาจถูกลบไปแล้ว รายการถูกโหลดใหม่'
}, en: {
  title: 'Administration', subtitle: 'Manage projects, categories, holidays and access requirements in one place.', signIn: 'Please sign in through Aiyara Shell',
  settingsTabs: 'Settings', tabOrganization: 'Organization', tabPeople: 'People', tabTime: 'Working time', tabCatalog: 'Projects and categories', tabSecurity: 'Security', tabAudit: 'Audit log',
  organizationCode: 'Organization code {code}', dayOff: 'Day off', member: 'Member', status: 'Status', actions: 'Actions',
  projects: 'Projects', projectName: 'Project name', categories: 'Categories',
  categoryName: 'Category name', holidays: 'Holidays', holidayName: 'Holiday name',
  date: 'Date', add: 'Add item', save: 'Save', delete: 'Delete', policy: 'Password policy',
  tenantId: 'Tenant ID', minimumLength: 'Minimum length (12–128)', expiryDays: 'Expiry (1–3650 days)',
  lengthUnit: 'characters', expiryUnit: 'days',
  uppercase: 'Uppercase', lowercase: 'Lowercase', digit: 'Digit', symbol: 'Symbol',
  lockMonth: 'Close timesheet month', lock: 'Close month', loadedError: 'Could not load settings',
  saveError: 'Save failed (admin role required)', deleteError: 'Delete failed',
  updateError: 'Update failed', policySaved: 'Policy saved',
  policyError: 'Could not save policy', policyInvalid: 'Minimum length must be 12–128 characters and expiry 1–3650 days.', locked: 'Month closed', lockError: 'Could not close month',
  projectHint: 'Manage projects available for time entry', categoryHint: 'Set the work types available in timesheets', holidayHint: 'Holidays appear on your team calendar',
  empty: 'No items yet', loading: 'Loading settings…', policyHint: 'Set password standards for members of this organization',
  lockHint: 'Closing a month prevents members from editing timesheets for that month.',
  countProjects: 'Projects', countCategories: 'Categories', countHolidays: 'Scheduled holidays',
  profile: 'Current organization', passwordRequirements: 'Password requirements', passwordLength: 'Character count',
  savePolicy: 'Save policy', retry: 'Try loading again',
  organization: 'Organization', organizationHint: 'The name and logo every member sees in the top-left corner.',
  organizationName: 'Organization name', saveName: 'Save name', logo: 'Logo', uploadLogo: 'Upload logo', replaceLogo: 'Replace logo',
  removeLogo: 'Remove logo', noLogo: 'No logo yet', logoHint: 'PNG, JPEG, or WebP up to 2 MB. A transparent background works best.',
  nameSaved: 'Organization name saved.', logoSaved: 'Logo updated.', logoRemoved: 'Logo removed.',
  invalidLogo: 'The logo must be a PNG, JPEG, or WebP file of at most 2 MB.',
  members: 'Members', memberHint: 'See who belongs to this organization and invite people by email.', inviteEmail: 'Invitee email',
  role: 'Role', roleEmployee: 'Employee', roleTenantAdmin: 'Organization admin', invite: 'Send invitation', inviting: 'Sending…',
  invited: 'Invitation sent to {email}.',
  makeAdmin: 'Make admin', makeEmployee: 'Make employee', deactivate: 'Deactivate', confirmDeactivate: 'Confirm deactivation',
  cancel: 'Cancel', reactivate: 'Reactivate', resendActivation: 'Resend activation',
  pendingInvitations: 'Pending invitations', noInvitations: 'No pending invitations', expires: 'Expires {date}', invitedBy: 'Invited by {email}',
  resendInvitation: 'Resend', revokeInvitation: 'Revoke',
  roleChanged: 'Changed the role of {email}.', deactivated: 'Deactivated {email} and signed them out everywhere.',
  reactivated: 'Reactivated {email}.', activationResent: 'Sent a new activation code to {email}.',
  invitationResent: 'Sent a new invitation to {email}; the old code no longer works.', invitationRevoked: 'Revoked the invitation for {email}.',
  lastAdmin: 'The organization needs at least one active admin.', selfChange: 'You cannot change your own role or status.',
  deliveryUnavailable: 'Saved, but the email could not be sent. Resend activation later.',
  memberChanged: 'This member changed in the meantime. The list was reloaded; try again.',
  inviteConflict: 'This email already belongs to a member of this organization or to a system administrator.',
  statusActive: 'Active', statusPendingActivation: 'Pending activation', statusDeactivated: 'Deactivated', you: 'You',
  groups: 'Member groups', groupHint: 'Sort members into groups such as permanent staff, outsourced, or contract to filter them and report by group. Groups do not change what anyone can do.',
  groupName: 'Group name', addGroup: 'Add group', noGroups: 'No groups yet', group: 'Group', noGroup: 'No group',
  groupMembers: 'Members: {count}', groupCreated: 'Added the group {name}.', groupRenamed: 'Renamed the group to {name}.', groupDeleted: 'Deleted the group {name}.',
  groupChanged: 'Moved {email} to a new group.', groupNameTaken: 'A group with this name already exists.', groupInUse: 'This group still has members. Move them out before deleting it.',
  groupNameInvalid: 'A group name must be 1–100 characters.',
  pickDate: 'Choose from the calendar',
  formatsTitle: 'Date and time format', formatsHint: 'Every page and report shows dates and times this way for everyone in the organization.',
  dateFormat: 'Date format', timeFormat: 'Time format', formatPreview: 'Preview', saveFormats: 'Save format', formatsSaved: 'Date and time format saved.',
  buddhistYear: 'Buddhist year', monthName: 'month name', hours24: '24-hour', hours12: '12-hour (AM/PM)',
  workWeek: 'Working days and hours', workWeekHint: 'Choose the days your organization works (other days are shaded as weekly days off on the team calendar) and when the working day starts and ends, which new entries start with.',
  workDays: 'Working days', workStart: 'Start of work', workEnd: 'End of work', hoursOrder: 'The working day must end after it starts.',
  hasBreak: 'Break (not counted as work)', breakStart: 'Break starts', breakEnd: 'Break ends', breakOrder: 'The break must be within the working hours and end after it starts.',
  workdayLength: 'A normal working day is {hours} hours; reports count a longer day as overtime.',
  leaveTypes: 'Leave types', leaveTypeHint: 'Members choose a type when they record leave and see what is left of it this year. Leave the days empty for no limit.',
  leaveTypeName: 'Type name', annualDays: 'Days a year', unlimited: 'No limit', leaveTypeExists: 'A leave type with this name already exists.',
  noLeaveTypes: 'No leave types yet. Members choose from Annual, Sick, or Other.',
  reminders: 'Reminders', reminderHint: 'Email members about time they have not logged, at this time in the organization\'s time zone. Holidays, days off, and leave are skipped.',
  dailyReminder: 'Daily reminder', dailyReminderHint: 'Each working day, to members who logged no time or leave on the previous working day.',
  monthEndReminder: 'Month-end reminder', monthEndReminderHint: 'On the last working day, to members whose month is short of the hours expected up to yesterday.',
  sendAt: 'Send at', saveReminders: 'Save reminders', remindersSaved: 'Reminders saved.',
  saveWorkWeek: 'Save working days and hours', workWeekSaved: 'Working days and hours saved.', workWeekRequired: 'Keep at least one working day.',
  billing: 'Billing and budget', hideBilling: 'Hide billing', billingHint: 'Only organization admins see rates. Reports multiply hours by the current rate, so changing a rate also reprices time already logged.',
  client: 'Client', clientHint: 'Optional, at most 200 characters.', billable: 'Billable', nonBillable: 'Not billable', billableSwitch: 'This project is billable',
  hourlyRate: 'Hourly rate', rateHint: '0 to 1,000,000, at most 2 decimals.', currency: 'Currency', currencyHint: 'Three letters such as THB. Required when a rate is set.',
  budgetHours: 'Budget (hours)', budgetHint: 'Total hours for the life of the project, above 0 up to 1,000,000. Going over only warns; time can still be saved.',
  perHour: 'per hour', budgetOf: '{hours} h budget', saveBilling: 'Save billing', billingSaved: 'Saved the billing details of {name}.',
  billingInvalid: 'Could not save. Fix the marked fields and try again.',
  rateInvalid: 'The rate must be a number from 0 to 1,000,000 with at most 2 decimals.', currencyInvalid: 'The currency must be exactly 3 letters, such as THB.', currencyRequired: 'Choose a currency when you set a rate.',
  budgetInvalid: 'The budget must be above 0 and at most 1,000,000 hours, with at most 2 decimals.', clientInvalid: 'The client name must be at most 200 characters.',
  billingForbidden: 'Only organization admins can change billing details.', billingGone: 'This project no longer exists. The list was reloaded.'
} })
// ---- Tabs: one subject at a time, remembered for the session ----------------------------------
type TabId = 'organization' | 'people' | 'time' | 'catalog' | 'security' | 'audit'
const tabIds: TabId[] = ['organization', 'people', 'time', 'catalog', 'security', 'audit']
const TAB_KEY = 'aiyara.administration.tab'
const activeTab = ref<TabId>('organization')
const tabButtons = ref<HTMLButtonElement[]>([])
const tabs = computed(() => [
  { id: 'organization' as const, label: copy.value.tabOrganization },
  { id: 'people' as const, label: copy.value.tabPeople, count: members.value.length || null },
  { id: 'time' as const, label: copy.value.tabTime },
  { id: 'catalog' as const, label: copy.value.tabCatalog },
  { id: 'security' as const, label: copy.value.tabSecurity },
  { id: 'audit' as const, label: copy.value.tabAudit }
])
function selectTab(id: TabId, focus = false) {
  activeTab.value = id
  try { sessionStorage.setItem(TAB_KEY, id) } catch { /* the choice is a convenience */ }
  if (focus) void nextTick(() => tabButtons.value[tabIds.indexOf(id)]?.focus())
}
/** Arrow keys move along the tabs (wrapping), Home and End jump to the ends. */
function onTabKey(event: KeyboardEvent) {
  const index = tabIds.indexOf(activeTab.value)
  const next = event.key === 'ArrowRight' ? (index + 1) % tabIds.length : event.key === 'ArrowLeft' ? (index - 1 + tabIds.length) % tabIds.length
    : event.key === 'Home' ? 0 : event.key === 'End' ? tabIds.length - 1 : -1
  if (next < 0) return
  event.preventDefault()
  selectTab(tabIds[next]!, true)
}

// ---- The week: the working days and hours being edited, drawn as seven bars ----------------------
const clockMinutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
/** Whole hours around the working day, so the bars have room either side. */
const weekScale = computed(() => {
  const from = Math.max(0, Math.floor(Math.min(clockMinutes(workHours.startTime), clockMinutes(workHours.endTime)) / 60) - 1)
  const to = Math.min(24, Math.ceil(Math.max(clockMinutes(workHours.startTime), clockMinutes(workHours.endTime)) / 60) + 1)
  const step = to - from > 12 ? 2 : 1
  return { from: from * 60, to: to * 60, hours: Array.from({ length: Math.floor((to - from) / step) + 1 }, (_, index) => from + index * step) }
})
const weekPercent = (minutes: number) => `${((minutes - weekScale.value.from) / (weekScale.value.to - weekScale.value.from)) * 100}%`
const weekSpan = (start: string, end: string) => ({ left: weekPercent(clockMinutes(start)),
  width: `calc(${weekPercent(clockMinutes(end))} - ${weekPercent(clockMinutes(start))})` })
const weekHourLabel = (hour: number) => formats.value.timeFormat === 'HH:mm' ? String(hour).padStart(2, '0') : String(hour % 12 || 12)

onMounted(() => {
  try {
    const saved = sessionStorage.getItem(TAB_KEY) as TabId | null
    if (saved && tabIds.includes(saved)) activeTab.value = saved
  } catch { /* storage may be unavailable */ }
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.source !== window.parent || event.origin !== config.public.shellOrigin ||
      event.data?.type !== 'aiyara.session') return
    // Later messages only renew the token or locale; reloading would overwrite unsaved edits.
    const firstSession = !token.value
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = readSessionLocale(event.data)
    applyTheme(readSessionTheme(event.data))
    formats.value = readFormats(event.data)
    tenantId.value = event.data.tenantId || ''
    if (token.value && firstSession) load()
  })
  // The Shell's first session message can arrive before hydration; ask for it once listening.
  if (window.parent !== window) window.parent.postMessage({ type: 'aiyara.ready' }, config.public.shellOrigin)
})
async function api<T>(path: string, options: { method?: string; body?: object } = {}) {
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1/timesheets${path}`, { ...options,
      headers: { Authorization: `Bearer ${accessToken}` } }), token, config.public.shellOrigin))
}
async function identityApi<T>(path: string, options: { method?: string; body?: object | FormData } = {}) {
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1${path}`, { ...options,
      headers: { Authorization: `Bearer ${accessToken}` } }), token, config.public.shellOrigin))
}
async function load() {
  isLoading.value = true
  error.value = ''
  try {
    catalog.value = await api<Catalog>('/catalog')
    workingDays.value = [...(catalog.value.workingDays ?? [1, 2, 3, 4, 5])]
    showWorkHours(catalog.value.workHours ?? { startTime: '09:00', endTime: '17:00' })
    if (!remindersLoaded) { Object.assign(reminders, await api<typeof reminders>('/reminders')); remindersLoaded = true }
    if (tenantId.value) Object.assign(policy, await identityApi<typeof policy>(`/identity/tenants/${tenantId.value}/password-policy`))
    await Promise.all([loadMembers(), loadOrganization()])
  }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isLoading.value = false }
}
async function add(kind: 'projects' | 'categories' | 'holidays') {
  try {
    const body = kind === 'projects' ? { name: projectName.value } : kind === 'categories' ? { name: categoryName.value } : { date: holidayDate.value, name: holidayName.value }
    await api(`/${kind}`, { method: 'POST', body })
    projectName.value = ''; categoryName.value = ''; holidayName.value = ''
    await load()
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
async function remove(kind: 'projects' | 'categories' | 'holidays', id: string) {
  try { await api(`/${kind}/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function update(kind: 'projects' | 'categories' | 'holidays', item: { id: string; name: string; date?: string }) {
  try { await api(`/${kind}/${item.id}`, { method: 'PUT', body: kind === 'holidays' ? { name: item.name, date: item.date } : { name: item.name } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
// ---- Project billing: client, billable flag, hourly rate with currency, and an hours budget ----------------
/** The project whose billing form is open, its draft, and the problems and refusal shown on that form. */
const billingOpenId = ref('')
const billingDrafts = reactive<Record<string, BillingDraft>>({})
const billingBusyId = ref('')
const billingErrors = reactive<Record<string, string>>({})
const billingServerProblems = reactive<Record<string, Partial<Record<BillingField, BillingProblem>>>>({})
const draftFrom = (item: Project): BillingDraft => ({ client: item.client ?? '', isBillable: item.isBillable ?? false,
  hourlyRate: item.hourlyRate == null ? '' : String(item.hourlyRate), currencyCode: item.currencyCode ?? '',
  budgetHours: item.budgetHours == null ? '' : String(item.budgetHours) })
const draftOf = (item: Project) => billingDrafts[item.id] ??= draftFrom(item)
const billingProblems = (item: Project) => ({ ...validateBilling(draftOf(item)), ...billingServerProblems[item.id] })
/** The text for the rule a field broke. */
const problemText = (problem: BillingProblem) => ({ rate: copy.value.rateInvalid, currencyFormat: copy.value.currencyInvalid,
  currencyRequired: copy.value.currencyRequired, budget: copy.value.budgetInvalid, client: copy.value.clientInvalid })[problem]
function toggleBilling(item: Project) {
  const closing = billingOpenId.value === item.id
  billingOpenId.value = closing ? '' : item.id
  if (!closing) { billingDrafts[item.id] = draftFrom(item); billingErrors[item.id] = ''; billingServerProblems[item.id] = {} }
}
/** "Contoso · Billable · THB 1,200.00 per hour · 100 h budget" for a project's closed row. */
function billingSummary(item: Project) {
  const parts = [item.client, item.isBillable ? copy.value.billable : copy.value.nonBillable]
  if (item.hourlyRate != null) parts.push(`${formatMoney(item.hourlyRate, item.currencyCode, locale.value)} ${copy.value.perHour}`)
  if (item.budgetHours != null) parts.push(copy.value.budgetOf.replace('{hours}', formatHours(item.budgetHours, locale.value)))
  return parts.filter(Boolean).join(' · ')
}
async function saveBilling(item: Project) {
  const draft = draftOf(item)
  billingServerProblems[item.id] = {}
  billingErrors[item.id] = ''
  notice.value = ''
  if (Object.keys(validateBilling(draft)).length) { billingErrors[item.id] = copy.value.billingInvalid; return }
  billingBusyId.value = item.id
  try {
    const saved = await api<Project>(`/projects/${item.id}/billing`, { method: 'PUT', body: billingBody(draft) })
    Object.assign(item, { client: saved.client ?? null, isBillable: saved.isBillable ?? false, hourlyRate: saved.hourlyRate ?? null,
      currencyCode: saved.currencyCode ?? null, budgetHours: saved.budgetHours ?? null })
    billingDrafts[item.id] = draftFrom(item)
    notice.value = copy.value.billingSaved.replace('{name}', item.name)
  } catch (cause) {
    const failure = cause as { data?: { statusCode?: number; errorCode?: string; errors?: Record<string, string[]> }; status?: number }
    const status = failure?.data?.statusCode ?? failure?.status
    const code = failure?.data?.errorCode
    if (code === 'VALIDATION_FAILED' || status === 400) {
      const named = fieldErrors(cause, locale.value)
      const map: Partial<Record<BillingField, BillingProblem>> = {}
      if (named.hourlyRate) map.hourlyRate = 'rate'
      if (named.currencyCode) map.currencyCode = failure?.data?.errors?.CurrencyCode?.includes('REQUIRED') ? 'currencyRequired' : 'currencyFormat'
      if (named.budgetHours) map.budgetHours = 'budget'
      if (named.client) map.client = 'client'
      billingServerProblems[item.id] = map
      billingErrors[item.id] = copy.value.billingInvalid
    } else if (code === 'FORBIDDEN' || status === 403) billingErrors[item.id] = copy.value.billingForbidden
    else if (code === 'NOT_FOUND' || status === 404) { billingErrors[item.id] = copy.value.billingGone; void load() }
    else billingErrors[item.id] = safeError(cause, locale.value)
  } finally { billingBusyId.value = '' }
}
async function saveLeaveType(type?: LeaveType) {
  error.value = ''
  const annualDays = (value: number | '' | null) => value === '' || value == null ? null : Number(value)
  try {
    await api(type ? `/leave-types/${type.id}` : '/leave-types', { method: type ? 'PUT' : 'POST',
      body: type ? { name: type.name, annualDays: annualDays(type.annualDays) } : { name: leaveTypeName.value, annualDays: annualDays(leaveTypeDays.value) } })
    if (!type) { leaveTypeName.value = ''; leaveTypeDays.value = '' }
    await load()
  } catch (cause) {
    error.value = (cause as { data?: { errorCode?: string } })?.data?.errorCode === 'LEAVE_TYPE_EXISTS' ? copy.value.leaveTypeExists : safeError(cause, locale.value)
  }
}
async function removeLeaveType(id: string) {
  try { await api(`/leave-types/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
function weekdayName(day: number) {
  // 18 October 2026 is a Sunday, so adding the weekday number lands on that weekday.
  return new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en-GB', { weekday: 'long' }).format(new Date(2026, 9, 18 + day))
}
async function saveWorkWeek() {
  if (!hoursValid.value || !breakValid.value) return
  isSavingWorkWeek.value = true
  error.value = ''
  notice.value = ''
  try {
    const saved = await api<{ workingDays: number[]; workHours: WorkHours }>('/work-week', { method: 'PUT', body: {
      workingDays: workingDays.value, startTime: `${workHours.startTime}:00`, endTime: `${workHours.endTime}:00`,
      breakStart: hasBreak.value ? `${breakTime.start}:00` : null, breakEnd: hasBreak.value ? `${breakTime.end}:00` : null } })
    workingDays.value = saved.workingDays
    showWorkHours(saved.workHours)
    notice.value = copy.value.workWeekSaved
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isSavingWorkWeek.value = false }
}
async function saveReminders() {
  isSavingReminders.value = true
  error.value = ''
  notice.value = ''
  try {
    Object.assign(reminders, await api<typeof reminders>('/reminders', { method: 'PUT',
      body: { dailyEnabled: reminders.dailyEnabled, monthEndEnabled: reminders.monthEndEnabled, sendAt: `${reminders.sendAt}:00` } }))
    notice.value = copy.value.remindersSaved
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isSavingReminders.value = false }
}
async function showOrganization(value: Organization) {
  organization.value = value
  Object.assign(formatForm, readFormats(value))
  organizationName.value = value.name
  const logo = value.logoUrl ? URL.createObjectURL(await withRemoteRefresh(accessToken =>
    $fetch<Blob>(`${gateway.value}${value.logoUrl}`, { responseType: 'blob', headers: { Authorization: `Bearer ${accessToken}` } }),
  token, config.public.shellOrigin)) : ''
  if (logoPreview.value) URL.revokeObjectURL(logoPreview.value)
  logoPreview.value = logo
}
async function loadOrganization() { await showOrganization(await identityApi<Organization>('/identity/organization')) }
/** Saves a branding change, shows the result, and tells the Shell to refresh its header. */
async function brandAction(request: () => Promise<Organization>, success: string) {
  isSavingBrand.value = true
  error.value = ''
  notice.value = ''
  try {
    await showOrganization(await request())
    notice.value = success
    window.parent.postMessage({ type: 'aiyara.organization.updated' }, config.public.shellOrigin)
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'INVALID_LOGO' ? copy.value.invalidLogo : safeError(cause, locale.value)
  } finally { isSavingBrand.value = false }
}
const saveName = () => brandAction(() => identityApi<Organization>('/identity/organization',
  { method: 'PUT', body: { name: organizationName.value } }), copy.value.nameSaved)
function uploadLogo(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > 2 * 1024 * 1024) { error.value = copy.value.invalidLogo; return }
  const form = new FormData()
  form.append('logo', file)
  return brandAction(() => identityApi<Organization>('/identity/organization/logo', { method: 'POST', body: form }),
    copy.value.logoSaved)
}
/** A date format as an example date (31 December), named when its year or month is not obvious. */
const dateOptionLabel = (format: Formats['dateFormat']) => {
  const example = formatCalendarDate(`${new Date().getFullYear()}-12-31`, { ...formatForm, dateFormat: format }, locale.value)
  return format === 'dd/MM/bbbb' ? `${example} · ${copy.value.buddhistYear}` : format === 'd MMM yyyy' ? `${example} · ${copy.value.monthName}` : example
}
const timeOptionLabel = (format: Formats['timeFormat']) =>
  `${formatTime('13:30', { ...formatForm, timeFormat: format })} · ${format === 'HH:mm' ? copy.value.hours24 : copy.value.hours12}`
const saveFormats = () => brandAction(() => identityApi<Organization>('/identity/organization/formats',
  { method: 'PUT', body: { ...formatForm } }), copy.value.formatsSaved)
const removeLogo = () => brandAction(() => identityApi<Organization>('/identity/organization/logo', { method: 'DELETE' }),
  copy.value.logoRemoved)
function closeMfaReset(id: string) {
  mfaResettingId.value = ''
  void nextTick(() => document.getElementById(`mfa-reset-button-${id}`)?.focus())
}
async function mfaResetDone(member: Member) {
  mfaResettingId.value = ''
  error.value = ''
  notice.value = locale.value === 'th' ? `รีเซ็ต MFA ของ ${member.email} แล้ว` : `Reset MFA for ${member.email}.`
  try { await loadMembers() } catch { /* the notice stays */ }
  void nextTick(() => document.getElementById(`mfa-reset-button-${member.id}`)?.focus())
}
async function loadMembers() {
  const [list, pending, groupList] = await Promise.all([
    identityApi<Member[]>('/identity/members'),
    identityApi<Invitation[]>('/identity/members/invitations'),
    identityApi<MemberGroup[]>('/identity/members/groups')
  ])
  members.value = list
  invitations.value = pending
  groups.value = groupList
}
/** Runs one member or invitation action, then reloads the lists whether or not it succeeded. */
async function memberAction(id: string, request: () => Promise<unknown>, success: string, email: string) {
  busyId.value = id
  error.value = ''
  notice.value = ''
  try {
    await request()
    notice.value = success.replace('{email}', email)
  } catch (cause) {
    const failure = cause as { data?: { statusCode?: number; errorCode?: string }; status?: number }
    const code = failure?.data?.errorCode
    error.value = code === 'LAST_ADMINISTRATOR' ? copy.value.lastAdmin
      : code === 'CANNOT_CHANGE_SELF' ? copy.value.selfChange
      : code === 'GROUP_NAME_TAKEN' ? copy.value.groupNameTaken
      : code === 'GROUP_IN_USE' ? copy.value.groupInUse
      : code === 'VALIDATION_FAILED' ? copy.value.groupNameInvalid
      : (failure?.data?.statusCode ?? failure?.status) === 503 ? copy.value.deliveryUnavailable
      : code === 'CONFLICT' || code === 'NOT_FOUND' ? copy.value.memberChanged
      : safeError(cause, locale.value)
  } finally {
    busyId.value = ''
    confirmingId.value = ''
    try { await loadMembers() } catch { /* the action's own message stays */ }
  }
}
const changeRole = (member: Member) => memberAction(member.id, () => identityApi(`/identity/members/${member.id}/role`,
  { method: 'PUT', body: { role: member.role === 'TenantAdmin' ? 'Employee' : 'TenantAdmin' } }), copy.value.roleChanged, member.email)
function setActive(member: Member, active: boolean) {
  // Deactivation signs the member out everywhere, so it takes a second, explicit click.
  if (!active && confirmingId.value !== member.id) { confirmingId.value = member.id; return }
  return memberAction(member.id, () => identityApi(`/identity/members/${member.id}/status`, { method: 'PUT', body: { active } }),
    active ? copy.value.reactivated : copy.value.deactivated, member.email)
}
const setGroup = (member: Member, groupId: string) => memberAction(member.id, () => identityApi(`/identity/members/${member.id}/group`,
  { method: 'PUT', body: { groupId: groupId || null } }), copy.value.groupChanged, member.email)
async function addGroup() {
  const name = groupName.value.trim()
  await memberAction('new-group', () => identityApi('/identity/members/groups', { method: 'POST', body: { name } }),
    copy.value.groupCreated.replace('{name}', name), '')
  if (!error.value) groupName.value = ''
}
const renameGroup = (group: MemberGroup) => memberAction(group.id, () => identityApi(`/identity/members/groups/${group.id}`,
  { method: 'PUT', body: { name: group.name.trim() } }), copy.value.groupRenamed.replace('{name}', group.name.trim()), '')
const deleteGroup = (group: MemberGroup) => memberAction(group.id, () => identityApi(`/identity/members/groups/${group.id}`,
  { method: 'DELETE' }), copy.value.groupDeleted.replace('{name}', group.name), '')
const resendActivation = (member: Member) => memberAction(member.id, () => identityApi(`/identity/members/${member.id}/activation`,
  { method: 'POST' }), copy.value.activationResent, member.email)
const resendInvitation = (invitation: Invitation) => memberAction(invitation.id, () => identityApi(
  `/identity/members/invitations/${invitation.id}/resend`, { method: 'POST' }), copy.value.invitationResent, invitation.email)
const revokeInvitation = (invitation: Invitation) => memberAction(invitation.id, () => identityApi(
  `/identity/members/invitations/${invitation.id}`, { method: 'DELETE' }), copy.value.invitationRevoked, invitation.email)
/** An instant's calendar date on this device, in the organization's date format. */
const formatDate = (value: string) => {
  const date = new Date(value)
  return formatCalendarDate(`${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`,
    formats.value, locale.value)
}
const memberName = (member: Member) => [member.firstName, member.lastName].filter(part => part?.trim()).join(' ')
const roleLabel = (role: string) => role === 'TenantAdmin' ? copy.value.roleTenantAdmin : copy.value.roleEmployee
const statusLabel = (status: string) => status === 'Active' ? copy.value.statusActive
  : status === 'Deactivated' ? copy.value.statusDeactivated : copy.value.statusPendingActivation
async function invite() {
  isInviting.value = true
  error.value = ''
  notice.value = ''
  const email = inviteEmail.value.trim()
  try {
    await identityApi(`/auth/tenants/${tenantId.value}/invitations`, { method: 'POST',
      body: { email, role: inviteRole.value, ...(inviteGroup.value ? { groupId: inviteGroup.value } : {}) } })
    notice.value = copy.value.invited.replace('{email}', email)
    inviteEmail.value = ''
    inviteRole.value = 'Employee'
    inviteGroup.value = ''
    invitations.value = await identityApi<Invitation[]>('/identity/members/invitations')
  } catch (cause) {
    const failure = cause as { data?: { statusCode?: number }; status?: number }
    error.value = (failure?.data?.statusCode ?? failure?.status) === 409 ? copy.value.inviteConflict : safeError(cause, locale.value)
  } finally { isInviting.value = false }
}
async function savePolicy() {
  isSavingPolicy.value = true
  try {
    await identityApi(`/auth/tenants/${tenantId.value}/password-policy`, { method: 'PUT', body: policy })
    notice.value = copy.value.policySaved
  } catch (cause) {
    // Identity accepts 12–128 characters and 1–3650 days; say so instead of a generic message.
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = code === 'INVALIDINPUT' ? copy.value.policyInvalid : safeError(cause, locale.value)
  }
  finally { isSavingPolicy.value = false }
}
async function lock() {
  isLocking.value = true
  try {
    await api(`/months/${lockMonth.value.slice(0, 4)}/${Number(lockMonth.value.slice(5))}/lock`, { method: 'POST' })
    error.value = ''
    notice.value = copy.value.locked
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isLocking.value = false }
}

// The choices each picker offers, as the shared autocomplete takes them.
const dateFormatChoices = computed(() => DATE_FORMATS.map(format => ({ value: format, label: dateOptionLabel(format) })))
const timeFormatChoices = computed(() => TIME_FORMATS.map(format => ({ value: format, label: timeOptionLabel(format) })))
const roleChoices = computed(() => [{ value: 'Employee', label: copy.value.roleEmployee }, { value: 'TenantAdmin', label: copy.value.roleTenantAdmin }])
const groupChoices = computed(() => [{ value: '', label: copy.value.noGroup }, ...groups.value.map(group => ({ value: group.id, label: group.name }))])
</script>
<template>
  <main class="admin-page">
    <div v-if="!token" class="empty-state"><h2>{{ copy.signIn }}</h2></div>
    <template v-else>
      <!-- The organization itself heads its settings: logo, name, and the code members sign in with. -->
      <header class="org-heading">
        <div class="org-logo" aria-hidden="true"><img v-if="logoPreview" :src="logoPreview" alt="" /><span v-else>{{ (organization?.name || copy.title).slice(0, 1) }}</span></div>
        <div>
          <h1>{{ organization?.name || copy.title }}</h1>
          <p v-if="organization?.organizationCode" class="org-code">{{ copy.organizationCode.replace('{code}', organization.organizationCode) }}</p>
        </div>
      </header>

      <p v-if="error" class="feedback feedback-error" role="alert"><span>{{ error }}</span><button class="button button-subtle" type="button" @click="load">{{ copy.retry }}</button></p>
      <p v-if="notice" class="feedback feedback-success" role="status">{{ notice }}</p>
      <p v-if="isLoading" class="loading-line" role="status">{{ copy.loading }}</p>

      <div class="tab-list" role="tablist" :aria-label="copy.settingsTabs" @keydown="onTabKey">
        <button v-for="tab in tabs" :id="`tab-${tab.id}`" :key="tab.id" ref="tabButtons" type="button" role="tab" class="tab"
          :aria-selected="activeTab === tab.id" :aria-controls="`panel-${tab.id}`" :tabindex="activeTab === tab.id ? 0 : -1"
          @click="selectTab(tab.id)"><!-- Thai has no spaces, so "|" marks where a long label may break; each part stays whole. --><span v-if="tab.label.includes('|')" class="tab-words"><span v-for="part in tab.label.split('|')" :key="part">{{ part }}</span></span><template v-else>{{ tab.label }}</template><span v-if="tab.count" class="tab-count">{{ tab.count }}</span></button>
      </div>

      <!-- Organization: name and logo, date and time format. -->
      <div v-show="activeTab === 'organization'" id="panel-organization" class="tab-panel" role="tabpanel" aria-labelledby="tab-organization" tabindex="0">
        <section class="panel brand-panel" aria-labelledby="organization-heading">
          <div class="panel-heading"><h2 id="organization-heading">{{ copy.organization }}</h2><p>{{ copy.organizationHint }}</p></div>
          <div class="brand-grid">
            <form class="add-form brand-name-form" @submit.prevent="saveName">
              <label class="grow"><span>{{ copy.organizationName }}</span><input v-model="organizationName" required maxlength="200" /></label>
              <button class="button button-primary" :disabled="isSavingBrand || !organizationName.trim() || organizationName.trim() === organization?.name">{{ copy.saveName }}</button>
            </form>
            <div class="brand-logo-field">
              <span class="field-label">{{ copy.logo }}</span>
              <div class="logo-row">
                <div class="logo-preview"><img v-if="logoPreview" :src="logoPreview" :alt="copy.logo" /><span v-else>{{ copy.noLogo }}</span></div>
                <div class="logo-actions">
                  <label class="button button-subtle file-button">{{ logoPreview ? copy.replaceLogo : copy.uploadLogo }}<input class="sr-only" type="file" accept="image/png,image/jpeg,image/webp" :disabled="isSavingBrand" @change="uploadLogo" /></label>
                  <button v-if="logoPreview" class="button button-danger-text" type="button" :disabled="isSavingBrand" @click="removeLogo">{{ copy.removeLogo }}</button>
                </div>
              </div>
              <p class="field-hint">{{ copy.logoHint }}</p>
            </div>
          </div>
        </section>
        <section class="panel formats-panel" aria-labelledby="formats-heading">
          <div class="panel-heading"><h2 id="formats-heading">{{ copy.formatsTitle }}</h2><p>{{ copy.formatsHint }}</p></div>
          <form class="add-form formats-form" @submit.prevent="saveFormats">
            <label><span>{{ copy.dateFormat }}</span><AutoComplete v-model="formatForm.dateFormat" :label="copy.dateFormat" :options="dateFormatChoices" required /></label>
            <label><span>{{ copy.timeFormat }}</span><AutoComplete v-model="formatForm.timeFormat" :label="copy.timeFormat" :options="timeFormatChoices" required /></label>
            <p class="format-preview" aria-live="polite"><span>{{ copy.formatPreview }}</span><strong>{{ formatPreview }}</strong></p>
            <button class="button button-primary" :disabled="isSavingBrand">{{ isSavingBrand ? copy.loading : copy.saveFormats }}</button>
          </form>
        </section>
      </div>

      <!-- People: members and invitations, groups, pending invitations. -->
      <div v-show="activeTab === 'people'" id="panel-people" class="tab-panel" role="tabpanel" aria-labelledby="tab-people" tabindex="0">
        <section class="panel members-panel" aria-labelledby="members-heading">
          <div class="panel-heading"><h2 id="members-heading">{{ copy.members }}</h2><p>{{ copy.memberHint }}</p></div>
          <form class="add-form invite-form" @submit.prevent="invite">
            <label class="grow"><span>{{ copy.inviteEmail }}</span><input v-model="inviteEmail" required type="email" maxlength="320" autocomplete="off" /></label>
            <label><span>{{ copy.role }}</span><AutoComplete v-model="inviteRole" :label="copy.role" :options="roleChoices" required /></label>
            <label v-if="groups.length"><span>{{ copy.group }}</span><AutoComplete v-model="inviteGroup" :label="copy.group" :options="groupChoices" /></label>
            <button class="button button-primary" :disabled="isInviting">{{ isInviting ? copy.inviting : copy.invite }}</button>
          </form>
          <div v-if="members.length" class="table-wrap">
            <table class="member-table" :aria-label="copy.members">
              <colgroup><col class="col-member" /><col class="col-role" /><col class="col-status" /><col class="col-group" /><col class="col-actions" /></colgroup>
              <thead><tr><th scope="col">{{ copy.member }}</th><th scope="col">{{ copy.role }}</th><th scope="col">{{ copy.status }}</th><th scope="col">{{ copy.group }}</th><th scope="col"><span class="sr-only">{{ copy.actions }}</span></th></tr></thead>
              <tbody>
                <template v-for="member in members" :key="member.id"><tr>
                  <td><div class="member-identity"><strong>{{ memberName(member) || member.email }}<span v-if="member.isCurrentUser" class="badge badge-self">{{ copy.you }}</span></strong><span v-if="memberName(member)">{{ member.email }}</span></div></td>
                  <td>{{ roleLabel(member.role) }}</td>
                  <td><span class="status" :class="`status-${member.status}`">{{ statusLabel(member.status) }}</span><MemberMfa :id="member.id" :locale="locale" :email="member.email" :enabled="!!member.mfaEnabled" :can-reset="!member.isCurrentUser" @reset="mfaResettingId = member.id" /></td>
                  <td><label class="group-select"><span class="sr-only">{{ copy.group }}: {{ member.email }}</span><AutoComplete :model-value="member.groupId ?? ''" :disabled="busyId === member.id" :label="`${copy.group}: ${member.email}`" :options="groupChoices" @change="group => setGroup(member, String(group))" /></label></td>
                  <td>
                    <div v-if="!member.isCurrentUser" class="row-actions member-actions">
                      <template v-if="member.status === 'Deactivated'"><button class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${copy.reactivate}: ${member.email}`" @click="setActive(member, true)">{{ copy.reactivate }}</button></template>
                      <template v-else>
                        <button class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${member.role === 'TenantAdmin' ? copy.makeEmployee : copy.makeAdmin}: ${member.email}`" @click="changeRole(member)">{{ member.role === 'TenantAdmin' ? copy.makeEmployee : copy.makeAdmin }}</button>
                        <button v-if="member.status === 'PendingActivation'" class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${copy.resendActivation}: ${member.email}`" @click="resendActivation(member)">{{ copy.resendActivation }}</button>
                        <template v-if="confirmingId === member.id"><button class="button button-warning" type="button" :disabled="busyId === member.id" :aria-label="`${copy.confirmDeactivate}: ${member.email}`" @click="setActive(member, false)">{{ copy.confirmDeactivate }}</button><button class="button button-subtle" type="button" @click="confirmingId = ''">{{ copy.cancel }}</button></template>
                        <button v-else class="button button-danger-text" type="button" :disabled="busyId === member.id" :aria-label="`${copy.deactivate}: ${member.email}`" @click="setActive(member, false)">{{ copy.deactivate }}</button>
                      </template>
                    </div>
                  </td>
                </tr><tr v-if="mfaResettingId === member.id"><td colspan="5"><MemberMfaReset :locale="locale" :account-id="member.id" :email="member.email" :api="identityApi"
                  @close="closeMfaReset(member.id)" @reset="mfaResetDone(member)" /></td></tr></template>
              </tbody>
            </table>
          </div>
          <p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p>
        </section>
        <div class="two-columns">
          <section class="panel" aria-labelledby="groups-heading">
            <div class="panel-heading"><h2 id="groups-heading">{{ copy.groups }}</h2><p>{{ copy.groupHint }}</p></div>
            <form class="add-form" @submit.prevent="addGroup"><label class="sr-only" for="new-group">{{ copy.groupName }}</label><input id="new-group" v-model="groupName" required maxlength="100" :placeholder="copy.groupName" /><button class="button button-primary" :disabled="busyId === 'new-group'">{{ copy.addGroup }}</button></form>
            <ul v-if="groups.length" class="item-list" :aria-label="copy.groups"><li v-for="group in groups" :key="group.id" class="item-row"><input v-model="group.name" required maxlength="100" :aria-label="copy.groupName" /><span class="badge">{{ copy.groupMembers.replace('{count}', String(group.memberCount)) }}</span><div class="row-actions"><button class="button button-subtle" type="button" :disabled="busyId === group.id" @click="renameGroup(group)">{{ copy.save }}</button><button class="button button-danger" type="button" :disabled="busyId === group.id" :aria-label="`${copy.delete}: ${group.name}`" @click="deleteGroup(group)">×</button></div></li></ul>
            <p v-else-if="!isLoading" class="empty-list">{{ copy.noGroups }}</p>
          </section>
          <section class="panel" aria-labelledby="invitations-heading">
            <div class="panel-heading"><h2 id="invitations-heading">{{ copy.pendingInvitations }}</h2></div>
            <ul v-if="invitations.length" class="item-list" :aria-label="copy.pendingInvitations"><li v-for="invitation in invitations" :key="invitation.id" class="invitation-row"><div class="member-identity"><strong>{{ invitation.email }}</strong><span>{{ copy.expires.replace('{date}', formatDate(invitation.expiresAtUtc)) }}</span><span v-if="invitation.invitedByEmail">{{ copy.invitedBy.replace('{email}', invitation.invitedByEmail) }}</span></div><div class="invitation-tags"><span class="badge">{{ roleLabel(invitation.role) }}</span><span v-if="invitation.groupName" class="badge">{{ invitation.groupName }}</span></div><div class="row-actions member-actions"><button class="button button-subtle" type="button" :disabled="busyId === invitation.id" :aria-label="`${copy.resendInvitation}: ${invitation.email}`" @click="resendInvitation(invitation)">{{ copy.resendInvitation }}</button><button class="button button-danger-text" type="button" :disabled="busyId === invitation.id" :aria-label="`${copy.revokeInvitation}: ${invitation.email}`" @click="revokeInvitation(invitation)">{{ copy.revokeInvitation }}</button></div></li></ul>
            <p v-else-if="!isLoading" class="empty-list">{{ copy.noInvitations }}</p>
          </section>
        </div>
      </div>

      <!-- Working time: the week, holidays, and closing a month. -->
      <div v-show="activeTab === 'time'" id="panel-time" class="tab-panel" role="tabpanel" aria-labelledby="tab-time" tabindex="0">
        <section class="panel work-week-panel" aria-labelledby="work-week-heading">
          <div class="panel-heading"><h2 id="work-week-heading">{{ copy.workWeek }}</h2><p>{{ copy.workWeekHint }}</p></div>
          <form class="work-week-form" @submit.prevent="saveWorkWeek">
            <!-- The week as it is being edited: each row is that day's checkbox, its bar the working hours and break. -->
            <fieldset class="week">
              <legend class="sr-only">{{ copy.workDays }}</legend>
              <ol class="week-hours" aria-hidden="true"><li v-for="hour in weekScale.hours" :key="hour" :style="{ left: weekPercent(hour * 60) }">{{ weekHourLabel(hour) }}</li></ol>
              <label v-for="day in weekdays" :key="day" class="week-row" :class="{ 'is-working': workingDays.includes(day) }"
                :title="workingDays.length === 1 && workingDays.includes(day) ? copy.workWeekRequired : undefined">
                <input v-model="workingDays" type="checkbox" :value="day" :disabled="workingDays.length === 1 && workingDays.includes(day)" />
                <span class="check-mark" aria-hidden="true"></span>
                <span class="week-day">{{ weekdayName(day) }}</span>
                <span class="week-track" aria-hidden="true">
                  <template v-if="workingDays.includes(day) && hoursValid">
                    <span class="week-band" :style="weekSpan(workHours.startTime, workHours.endTime)" />
                    <span v-if="hasBreak && breakValid" class="week-break" :style="weekSpan(breakTime.start, breakTime.end)" />
                  </template>
                  <span v-else-if="!workingDays.includes(day)" class="week-off">{{ copy.dayOff }}</span>
                </span>
              </label>
            </fieldset>
            <div class="work-hours"><label><span>{{ copy.workStart }}</span><TimeField v-model="workHours.startTime" :label="copy.workStart" :step="15" required /></label><span class="work-hours-dash" aria-hidden="true">–</span><label><span>{{ copy.workEnd }}</span><TimeField v-model="workHours.endTime" :label="copy.workEnd" :step="15" required /></label><p v-if="!hoursValid" id="work-hours-error" class="field-error" role="alert">{{ copy.hoursOrder }}</p></div>
            <div class="work-hours break-hours"><label class="check-card break-toggle"><input v-model="hasBreak" type="checkbox" /><span class="check-mark"></span><span>{{ copy.hasBreak }}</span></label><template v-if="hasBreak"><label><span>{{ copy.breakStart }}</span><TimeField v-model="breakTime.start" :label="copy.breakStart" :step="15" required /></label><span class="work-hours-dash" aria-hidden="true">–</span><label><span>{{ copy.breakEnd }}</span><TimeField v-model="breakTime.end" :label="copy.breakEnd" :step="15" required /></label></template><p v-if="!breakValid" id="break-error" class="field-error" role="alert">{{ copy.breakOrder }}</p></div>
            <div class="work-week-footer">
              <p v-if="hoursValid && breakValid" class="workday-summary">{{ copy.workdayLength.replace('{hours}', (workdayMinutes / 60).toFixed(2).replace(/\.?0+$/, '')) }}</p>
              <button class="button button-primary" :disabled="isSavingWorkWeek || !hoursValid || !breakValid">{{ isSavingWorkWeek ? copy.loading : copy.saveWorkWeek }}</button>
            </div>
          </form>
        </section>
        <section class="panel" aria-labelledby="reminders-heading">
          <div class="panel-heading"><h2 id="reminders-heading">{{ copy.reminders }}</h2><p>{{ copy.reminderHint }}</p></div>
          <form class="reminder-form" @submit.prevent="saveReminders">
            <label class="check-card reminder-option"><input v-model="reminders.dailyEnabled" type="checkbox" /><span class="check-mark"></span><span><strong>{{ copy.dailyReminder }}</strong><small>{{ copy.dailyReminderHint }}</small></span></label>
            <label class="check-card reminder-option"><input v-model="reminders.monthEndEnabled" type="checkbox" /><span class="check-mark"></span><span><strong>{{ copy.monthEndReminder }}</strong><small>{{ copy.monthEndReminderHint }}</small></span></label>
            <div class="work-week-footer"><label class="reminder-time"><span>{{ copy.sendAt }}</span><TimeField v-model="reminders.sendAt" :label="copy.sendAt" :step="15" required /></label><button class="button button-primary" :disabled="isSavingReminders">{{ isSavingReminders ? copy.loading : copy.saveReminders }}</button></div>
          </form>
        </section>
        <section class="panel" aria-labelledby="holidays-heading">
          <div class="panel-heading"><h2 id="holidays-heading">{{ copy.holidays }}</h2><p>{{ copy.holidayHint }}</p></div>
          <form class="add-form holiday-form" @submit.prevent="add('holidays')"><label><span>{{ copy.date }}</span><DateField v-model="holidayDate" required :label="copy.date" :pick-label="copy.pickDate" /></label><label class="holiday-name"><span>{{ copy.holidayName }}</span><input v-model="holidayName" required maxlength="200" :placeholder="copy.holidayName" /></label><button class="button button-primary">{{ copy.add }}</button></form>
          <ul v-if="catalog.holidays.length" class="item-list"><li v-for="item in catalog.holidays" :key="item.id"><form class="item-row holiday-row" @submit.prevent="update('holidays', item)"><DateField v-model="item.date" required :label="`${copy.date}: ${item.name}`" :pick-label="`${copy.pickDate}: ${item.name}`" /><input v-model="item.name" required maxlength="200" :aria-label="copy.holidayName" /><div class="row-actions"><button class="button button-subtle">{{ copy.save }}</button><button type="button" class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('holidays', item.id)">×</button></div></form></li></ul>
          <p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p>
        </section>
        <section class="panel" aria-labelledby="leave-types-heading">
          <div class="panel-heading"><h2 id="leave-types-heading">{{ copy.leaveTypes }}</h2><p>{{ copy.leaveTypeHint }}</p></div>
          <form class="add-form leave-type-form" @submit.prevent="saveLeaveType()"><label class="leave-type-name"><span>{{ copy.leaveTypeName }}</span><input v-model="leaveTypeName" required maxlength="60" :placeholder="copy.leaveTypeName" /></label><label><span>{{ copy.annualDays }}</span><input v-model="leaveTypeDays" type="number" min="0" max="366" step="0.5" :placeholder="copy.unlimited" /></label><button class="button button-primary">{{ copy.add }}</button></form>
          <ul v-if="catalog.leaveTypes?.length" class="item-list"><li v-for="item in catalog.leaveTypes" :key="item.id"><form class="item-row leave-type-row" @submit.prevent="saveLeaveType(item)"><input v-model="item.name" required maxlength="60" :aria-label="copy.leaveTypeName" /><input v-model="item.annualDays" type="number" min="0" max="366" step="0.5" :placeholder="copy.unlimited" :aria-label="`${copy.annualDays}: ${item.name}`" /><div class="row-actions"><button class="button button-subtle">{{ copy.save }}</button><button type="button" class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="removeLeaveType(item.id)">×</button></div></form></li></ul>
          <p v-else-if="!isLoading" class="empty-list">{{ copy.noLeaveTypes }}</p>
        </section>
        <section class="panel close-panel" aria-labelledby="close-heading">
          <div class="panel-heading"><h2 id="close-heading">{{ copy.lockMonth }}</h2><p>{{ copy.lockHint }}</p></div>
          <form class="close-form" @submit.prevent="lock"><label :aria-label="copy.lockMonth"><input v-model="lockMonth" type="month" required /></label><button class="button button-warning" :disabled="isLocking">{{ isLocking ? copy.loading : copy.lock }}</button></form>
        </section>
      </div>

      <!-- Projects and categories offered when recording time. -->
      <div v-show="activeTab === 'catalog'" id="panel-catalog" class="tab-panel two-columns" role="tabpanel" aria-labelledby="tab-catalog" tabindex="0">
        <section class="panel projects-panel" aria-labelledby="projects-heading"><div class="panel-heading"><h2 id="projects-heading">{{ copy.projects }}</h2><p>{{ copy.projectHint }}</p></div><form class="add-form" @submit.prevent="add('projects')"><label class="sr-only" for="new-project">{{ copy.projectName }}</label><input id="new-project" v-model="projectName" required maxlength="200" :placeholder="copy.projectName" /><button class="button button-primary">{{ copy.add }}</button></form>
          <ul v-if="catalog.projects.length" class="item-list">
            <li v-for="item in catalog.projects" :key="item.id" class="project-item">
              <div class="item-row"><input v-model="item.name" :aria-label="copy.projectName" /><div class="row-actions"><button type="button" class="button button-subtle" :aria-expanded="billingOpenId === item.id" :aria-controls="`billing-${item.id}`" :aria-label="`${billingOpenId === item.id ? copy.hideBilling : copy.billing}: ${item.name}`" @click="toggleBilling(item)">{{ billingOpenId === item.id ? copy.hideBilling : copy.billing }}</button><button class="button button-subtle" @click="update('projects', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('projects', item.id)">×</button></div></div>
              <p class="billing-summary">{{ billingSummary(item) }}</p>
              <form v-if="billingOpenId === item.id" :id="`billing-${item.id}`" class="billing-form" :aria-label="`${copy.billing}: ${item.name}`" novalidate @submit.prevent="saveBilling(item)">
                <p class="field-hint billing-hint">{{ copy.billingHint }}</p>
                <label class="billing-client"><span>{{ copy.client }}</span><input v-model="draftOf(item).client" maxlength="200" :aria-invalid="Boolean(billingProblems(item).client)" :aria-describedby="`billing-client-note-${item.id}`" /><small :id="`billing-client-note-${item.id}`" :class="{ 'field-error': billingProblems(item).client }">{{ billingProblems(item).client ? problemText(billingProblems(item).client!) : copy.clientHint }}</small></label>
                <label class="check-card billing-switch"><input v-model="draftOf(item).isBillable" type="checkbox" /><span class="check-mark"></span><span>{{ copy.billableSwitch }}</span></label>
                <label><span>{{ copy.hourlyRate }}</span><input v-model="draftOf(item).hourlyRate" inputmode="decimal" autocomplete="off" :aria-invalid="Boolean(billingProblems(item).hourlyRate)" :aria-describedby="`billing-rate-note-${item.id}`" /><small :id="`billing-rate-note-${item.id}`" :class="{ 'field-error': billingProblems(item).hourlyRate }">{{ billingProblems(item).hourlyRate ? problemText(billingProblems(item).hourlyRate!) : copy.rateHint }}</small></label>
                <label><span>{{ copy.currency }}</span><input v-model="draftOf(item).currencyCode" list="currency-options" maxlength="3" autocapitalize="characters" autocomplete="off" :aria-invalid="Boolean(billingProblems(item).currencyCode)" :aria-describedby="`billing-currency-note-${item.id}`" /><small :id="`billing-currency-note-${item.id}`" :class="{ 'field-error': billingProblems(item).currencyCode }">{{ billingProblems(item).currencyCode ? problemText(billingProblems(item).currencyCode!) : copy.currencyHint }}</small></label>
                <label><span>{{ copy.budgetHours }}</span><input v-model="draftOf(item).budgetHours" inputmode="decimal" autocomplete="off" :aria-invalid="Boolean(billingProblems(item).budgetHours)" :aria-describedby="`billing-budget-note-${item.id}`" /><small :id="`billing-budget-note-${item.id}`" :class="{ 'field-error': billingProblems(item).budgetHours }">{{ billingProblems(item).budgetHours ? problemText(billingProblems(item).budgetHours!) : copy.budgetHint }}</small></label>
                <div class="billing-footer"><p class="field-error" role="alert">{{ billingErrors[item.id] }}</p><button class="button button-primary" :disabled="billingBusyId === item.id">{{ billingBusyId === item.id ? copy.loading : copy.saveBilling }}</button></div>
              </form>
            </li>
          </ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p>
          <datalist id="currency-options"><option v-for="code in ['THB', 'USD', 'EUR', 'GBP', 'JPY', 'SGD', 'AUD', 'CNY']" :key="code" :value="code" /></datalist></section>
        <section class="panel" aria-labelledby="categories-heading"><div class="panel-heading"><h2 id="categories-heading">{{ copy.categories }}</h2><p>{{ copy.categoryHint }}</p></div><form class="add-form" @submit.prevent="add('categories')"><label class="sr-only" for="new-category">{{ copy.categoryName }}</label><input id="new-category" v-model="categoryName" required maxlength="200" :placeholder="copy.categoryName" /><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.categories.length" class="item-list"><li v-for="item in catalog.categories" :key="item.id" class="item-row"><input v-model="item.name" :aria-label="copy.categoryName" /><div class="row-actions"><button class="button button-subtle" @click="update('categories', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('categories', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
      </div>

      <!-- Security: the password policy. -->
      <div v-show="activeTab === 'security'" id="panel-security" class="tab-panel" role="tabpanel" aria-labelledby="tab-security" tabindex="0">
        <MfaRequirement :locale="locale" :required="organization?.requireMfa ?? false" :api="identityApi" @changed="value => { if (organization) organization.requireMfa = value }" />
        <section class="panel policy-panel" aria-labelledby="policy-heading"><div class="panel-heading"><h2 id="policy-heading">{{ copy.policy }}</h2><p>{{ copy.policyHint }}</p></div><form class="policy-form" @submit.prevent="savePolicy"><div class="policy-fields"><label>{{ copy.minimumLength }}<span class="input-suffix"><input v-model.number="policy.minimumLength" type="number" min="12" max="128" required /><span>{{ copy.lengthUnit }}</span></span></label><label>{{ copy.expiryDays }}<span class="input-suffix"><input v-model.number="policy.expiryDays" type="number" min="1" max="3650" required /><span>{{ copy.expiryUnit }}</span></span></label></div><fieldset><legend>{{ copy.passwordRequirements }}</legend><label class="check-card"><input v-model="policy.requireUppercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.uppercase }}</span></label><label class="check-card"><input v-model="policy.requireLowercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.lowercase }}</span></label><label class="check-card"><input v-model="policy.requireDigit" type="checkbox" /><span class="check-mark"></span><span>{{ copy.digit }}</span></label><label class="check-card"><input v-model="policy.requireSymbol" type="checkbox" /><span class="check-mark"></span><span>{{ copy.symbol }}</span></label></fieldset><button class="button button-primary" :disabled="isSavingPolicy">{{ isSavingPolicy ? copy.loading : copy.savePolicy }}</button></form></section>
      </div>

      <!-- Audit log: the member-management history, read when the tab is first opened. -->
      <div v-show="activeTab === 'audit'" id="panel-audit" class="tab-panel" role="tabpanel" aria-labelledby="tab-audit" tabindex="0">
        <MemberAuditLog :locale="locale" :formats="formats" :active="activeTab === 'audit'" :api="identityApi" />
      </div>
    </template>
  </main>
</template>
<style>
:root{font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;color:var(--color-ink);background:var(--color-canvas)}
*{box-sizing:border-box}body{margin:0}button,input,select{font:inherit}
:focus-visible{outline:2px solid var(--color-brand);outline-offset:2px}
/* The Shell sets the width and the side margins; a few pixels keep focus outlines inside the frame. */
.admin-page{padding:clamp(1rem,3vw,2rem) 3px}
h1,h2,h3{font-family:var(--font-display);font-weight:600}
/* The organization heads the page. */
.org-heading{display:flex;align-items:center;gap:1rem;margin:0 0 1.5rem}
.org-logo{display:grid;place-items:center;flex:0 0 auto;width:3.5rem;height:3.5rem;border:1px solid var(--color-border);border-radius:.75rem;background:var(--color-surface);overflow:hidden}
.org-logo img{max-width:85%;max-height:85%;object-fit:contain}
.org-logo span{font-family:var(--font-display);font-size:1.5rem;font-weight:600;color:var(--color-brand)}
.org-heading h1{font-size:clamp(1.6rem,3.5vw,2.3rem);line-height:1.15;margin:0;overflow-wrap:anywhere}
.org-code{color:var(--color-muted);margin:.2rem 0 0;font-variant-numeric:tabular-nums}
.feedback{display:flex;align-items:center;justify-content:space-between;gap:.75rem;padding:.75rem 1rem;border-radius:.5rem;margin:0 0 1rem}
.feedback-error{background:var(--color-danger-soft);color:var(--color-danger)}.feedback-success{background:var(--color-success-soft);color:var(--color-success)}
.loading-line{color:var(--color-muted);margin:0 0 .75rem}
/* Tabs: one subject at a time. */
.tab-list{display:flex;gap:.15rem;border-bottom:1px solid var(--color-border);margin:0 0 1.25rem}
.tab{display:inline-flex;align-items:center;gap:.45rem;flex:0 0 auto;background:none;border:0;border-bottom:2px solid transparent;margin-bottom:-1px;padding:.7rem .75rem;color:var(--color-muted);font-family:var(--font-display);font-size:.95rem;font-weight:600;cursor:pointer;transition:color 150ms ease,border-color 150ms ease}
.tab:hover{color:var(--color-ink)}
.tab[aria-selected="true"]{color:var(--color-brand);border-bottom-color:var(--color-brand)}
.tab:focus-visible{outline-offset:-2px;border-radius:.35rem .35rem 0 0}
.tab-count{background:var(--color-work-track);color:var(--color-brand-ink);border-radius:999px;font-family:Inter,ui-sans-serif,system-ui,sans-serif;font-size:.75rem;padding:.05rem .45rem}
.tab-words span{display:inline-block;white-space:nowrap}
.tab-panel{display:grid;gap:1.25rem}.tab-panel:focus-visible{outline:none}
/* Narrower than the five tabs in a row: a grid of buttons that wraps, so the bar never scrolls. */
@media (max-width:56rem){
  .tab-list{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:.4rem;border-bottom:0}
  .tab{justify-content:center;margin:0;border:1px solid var(--color-border);border-radius:.5rem;background:var(--color-surface);padding:.6rem .5rem;text-align:center;white-space:normal}
  .tab[aria-selected="true"]{background:var(--color-work-track);border-color:var(--color-brand)}
  .tab:focus-visible{border-radius:.5rem}
}
/* Phones: three tabs on the first row, the two longer ones sharing the second, so no tab sits alone. */
@media (max-width:36rem){
  .tab-list{grid-template-columns:repeat(6,minmax(0,1fr))}
  .tab{grid-column:span 2}
  .tab:nth-child(n+4){grid-column:span 3}
}
.two-columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,22rem),1fr));gap:1.25rem;align-items:start}
/* Panels: flat, one border, a heading and one hint. */
.panel{background:var(--color-surface);border:1px solid var(--color-border);border-radius:.75rem;padding:clamp(1rem,2vw,1.35rem);min-width:0}
.panel-heading{margin:0 0 1rem}
.panel-heading h2{font-size:1.2rem;margin:0 0 .3rem}
.panel-heading p{color:var(--color-muted);font-size:.9rem;line-height:1.5;margin:0;max-width:44rem}
label{color:var(--color-ink-soft);font-size:.85rem;font-weight:600}
input,select{min-width:0;border:1px solid var(--color-border-strong);border-radius:.5rem;background:var(--color-surface);color:var(--color-ink);padding:.55rem .65rem;font-size:.9rem}
input:focus-visible,select:focus-visible{border-color:var(--color-brand);outline-offset:0}
select[aria-invalid="true"]{border-color:var(--color-danger)}
.add-form{display:flex;flex-wrap:wrap;gap:.6rem;align-items:flex-end;margin-bottom:1rem}
.add-form>input{flex:1 1 12rem}
.add-form label{display:grid;gap:.3rem}
.grow{flex:1 1 14rem}
.button{border:1px solid transparent;border-radius:.5rem;padding:.55rem .9rem;font-size:.85rem;font-weight:600;cursor:pointer;white-space:nowrap}
.button:disabled{opacity:.55;cursor:not-allowed}
.button-primary{background:var(--color-brand);color:var(--color-on-brand)}.button-primary:hover:not(:disabled){background:var(--color-brand-strong)}
.button-subtle{background:var(--color-surface);border-color:var(--color-border);color:var(--color-ink-soft)}.button-subtle:hover:not(:disabled){background:var(--color-surface-muted)}
.button-danger{background:transparent;color:var(--color-danger);font-size:1.1rem;padding:.35rem .65rem}.button-danger:hover:not(:disabled){background:var(--color-danger-soft)}
.button-danger-text{background:transparent;color:var(--color-danger)}.button-danger-text:hover:not(:disabled){background:var(--color-danger-soft)}
.button-warning{background:var(--color-danger);color:var(--color-on-brand)}
.item-list{list-style:none;margin:0;padding:0;display:grid}
.item-row{display:flex;align-items:center;gap:.5rem;border-top:1px solid var(--color-border);padding:.5rem 0}
.item-list>li:first-child .item-row,.item-list>.item-row:first-child{border-top:0}
.item-row>input{flex:1;border-color:transparent;background:transparent}
.item-row>input:hover{border-color:var(--color-border)}
.row-actions{display:flex;align-items:center;gap:.35rem;flex-wrap:wrap}
.empty-list{margin:.25rem 0 0;padding:.85rem;border-radius:.5rem;background:var(--color-surface-muted);color:var(--color-muted);text-align:center;font-size:.85rem}
.field-label{display:block;color:var(--color-ink-soft);font-size:.85rem;font-weight:600;margin-bottom:.35rem}
.field-hint{color:var(--color-muted);font-size:.82rem;margin:.5rem 0 0}
.field-error{flex-basis:100%;margin:0;color:var(--color-danger);font-size:.85rem}
.badge{border-radius:999px;background:var(--color-surface-muted);color:var(--color-ink-soft);font-size:.75rem;font-weight:600;padding:.2rem .55rem;white-space:nowrap}
.badge-self{background:var(--color-work-track);color:var(--color-brand-ink);margin-left:.4rem}
.sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}
.empty-state{display:grid;place-items:center;min-height:12rem;border:1px dashed var(--color-border);border-radius:.75rem;color:var(--color-muted);text-align:center}
.empty-state h2{font-size:1rem;font-weight:500}
/* Organization. */
.brand-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,20rem),1fr));gap:1.5rem}
.logo-row{display:flex;align-items:center;gap:.75rem;flex-wrap:wrap}
.logo-preview{display:grid;place-items:center;width:7rem;height:3.5rem;border:1px dashed var(--color-border);border-radius:.5rem;background:var(--color-surface-muted);color:var(--color-muted);font-size:.75rem;text-align:center;padding:.3rem}
.logo-preview img{display:block;max-width:6.4rem;max-height:2.9rem;object-fit:contain}
.logo-actions{display:flex;gap:.45rem;flex-wrap:wrap}
.file-button{cursor:pointer}
.formats-form select{min-width:12rem}
.format-preview{display:grid;gap:.3rem;margin:0;flex:1;min-width:10rem;color:var(--color-ink-soft);font-size:.85rem;font-weight:600}
.format-preview strong{display:flex;align-items:center;min-height:2.4rem;color:var(--color-ink);font-family:var(--font-display);font-size:1.05rem}
/* People: an aligned table of members. */
.table-wrap{overflow-x:auto;position:relative}
.member-table{width:100%;border-collapse:collapse;table-layout:fixed;min-width:46rem}
.member-table .col-member{width:26%}.member-table .col-role{width:14%}.member-table .col-status{width:17%}.member-table .col-group{width:17%}
.member-table th{color:var(--color-muted);font-size:.8rem;font-weight:600;text-align:left;padding:.55rem .5rem;border-bottom:1px solid var(--color-border)}
.member-table td{padding:.7rem .5rem;border-bottom:1px solid var(--color-border);font-size:.9rem;vertical-align:middle}
.member-identity{display:grid;gap:.1rem;min-width:0;overflow-wrap:anywhere}
.member-identity strong{display:flex;align-items:center;flex-wrap:wrap;color:var(--color-ink)}
.member-identity span{color:var(--color-muted);font-size:.82rem}
.status{display:inline-flex;align-items:center;gap:.4rem;font-weight:600;font-size:.85rem}
.status::before{content:"";width:.5rem;height:.5rem;border-radius:50%;background:var(--color-faint)}
.status-Active{color:var(--color-success)}.status-Active::before{background:var(--color-success-strong)}
.status-PendingActivation{color:var(--color-warn)}.status-PendingActivation::before{background:var(--color-warn-strong)}
.status-Deactivated{color:var(--color-danger)}.status-Deactivated::before{background:var(--color-danger)}
.group-select select{width:100%}
.member-actions{justify-content:flex-end}
.member-actions .button{padding:.4rem .6rem;font-size:.8rem}
.invitation-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:.4rem .75rem;align-items:center;border-top:1px solid var(--color-border);padding:.7rem 0}
.invitation-row:first-child{border-top:0;padding-top:0}
.invitation-tags{display:flex;gap:.3rem;flex-wrap:wrap;justify-content:flex-end}
.invitation-row .member-actions{grid-column:1/-1;justify-content:flex-start}
/* Working time: the week drawn from the values being edited. */
.work-week-form{display:grid;gap:1rem}
.week{border:0;margin:0;padding:0 0 0 0;display:grid;gap:.3rem;position:relative}
/* The hour scale sits over the bars: past the checkbox and day name, short of the row's right padding. */
.week-hours{position:relative;height:1.5rem;list-style:none;margin:0 .5rem .2rem 11.5rem;padding:0}
.week-hours li{position:absolute;top:0;transform:translateX(-50%);color:var(--color-muted);font-family:var(--font-display);font-size:.85rem;font-variant-numeric:tabular-nums}
.week-row{position:relative;display:grid;grid-template-columns:1.5rem 9rem minmax(0,1fr);align-items:center;gap:0 .5rem;padding:.25rem .5rem .25rem 0;border-radius:.5rem;cursor:pointer;color:var(--color-ink)}
.week-row:hover{background:var(--color-surface-muted)}
.week-row input{position:absolute;opacity:0;width:1rem;height:1rem;left:.25rem}
.week-row:focus-within{outline:2px solid var(--color-brand);outline-offset:1px}
.week-day{font-weight:600}
.week-row:not(.is-working) .week-day{color:var(--color-muted);font-weight:500}
.week-track{position:relative;height:1.75rem;border-radius:.35rem;background:var(--color-surface);border:1px solid var(--color-border)}
.week-row:not(.is-working) .week-track{background:var(--color-day-off);border-color:var(--color-day-off)}
.week-band{position:absolute;top:.25rem;bottom:.25rem;border-radius:.25rem;background:var(--color-brand)}
.week-break{position:absolute;top:.25rem;bottom:.25rem;background:repeating-linear-gradient(135deg,var(--color-surface) 0 2px,var(--color-break) 2px 4px)}
.week-off{position:absolute;left:.6rem;top:50%;transform:translateY(-50%);color:var(--color-muted);font-size:.8rem;font-weight:500}
.check-mark{flex:0 0 auto;width:1.1rem;height:1.1rem;border:1.5px solid var(--color-faint);border-radius:.3rem;background:var(--color-surface)}
input:checked+.check-mark{border-color:var(--color-brand);background:var(--color-brand);box-shadow:inset 0 0 0 3px var(--color-surface)}
input:disabled+.check-mark{opacity:.55}
.check-card{position:relative;display:flex;align-items:center;gap:.55rem;border:1px solid var(--color-border);border-radius:.5rem;padding:.6rem .7rem;color:var(--color-ink-soft);font-size:.85rem;cursor:pointer}
.check-card input{position:absolute;opacity:0}
.check-card:focus-within{outline:2px solid var(--color-brand);outline-offset:1px}
.work-hours{display:flex;align-items:flex-end;gap:.5rem;flex-wrap:wrap}
.work-hours label:not(.check-card){display:grid;gap:.3rem}
.work-hours select{min-width:8rem}
.work-hours-dash{padding-bottom:.6rem;color:var(--color-muted)}
.break-toggle{flex:0 1 auto}
.work-week-footer{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:.75rem;border-top:1px solid var(--color-border);padding-top:1rem}
.workday-summary{margin:0;color:var(--color-ink)}
.reminder-form{display:grid;gap:.6rem}
.reminder-option{align-items:flex-start}
.reminder-option>span:last-child{display:grid;gap:.15rem;min-width:0}
.reminder-option strong{color:var(--color-ink);font-weight:600}
.reminder-option small{color:var(--color-muted);font-size:.8rem;line-height:1.45}
.reminder-time{display:grid;gap:.3rem}
.holiday-form label{display:grid;gap:.3rem;min-width:9rem}
.leave-type-form label{display:grid;gap:.3rem;min-width:8rem}
.leave-type-form .leave-type-name{flex:1}
.leave-type-row>input[type=number]{max-width:8rem}
.holiday-form .holiday-name{flex:1}
.holiday-row>.date-field{max-width:11rem}
.close-form{display:flex;flex-wrap:wrap;align-items:center;gap:.6rem}
/* Project billing: a form that opens under the project's row. */
.projects-panel{grid-column:1/-1}
.project-item{border-top:1px solid var(--color-border);padding:.15rem 0}
.project-item:first-child{border-top:0}
.project-item .item-row{border-top:0}
.billing-summary{margin:0 .65rem .4rem;color:var(--color-muted);font-size:.82rem}
.billing-summary:empty{display:none}
.billing-form{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,12rem),1fr));gap:.85rem;align-items:start;margin:.25rem 0 .75rem;padding:1rem;border:1px solid var(--color-border);border-radius:.6rem;background:var(--color-surface-muted)}
.billing-form label:not(.check-card){display:grid;gap:.3rem}
.billing-form small{color:var(--color-muted);font-size:.78rem;font-weight:400;line-height:1.4}
.billing-form small.field-error{color:var(--color-danger);font-weight:600}
.billing-form input[aria-invalid="true"]{border-color:var(--color-danger)}
.billing-hint,.billing-client,.billing-footer{grid-column:1/-1}
.billing-switch{align-self:start;background:var(--color-surface)}
.billing-footer{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:.75rem}
.billing-footer .field-error{flex:1 1 14rem}
/* Security. */
.policy-fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,14rem),1fr));gap:.85rem;margin-bottom:1rem}
.policy-fields>label{display:grid;gap:.35rem}
.input-suffix{display:flex;align-items:center;gap:.5rem;border:1px solid var(--color-border-strong);border-radius:.5rem;padding-right:.75rem;color:var(--color-muted);font-size:.8rem;font-weight:500}
.input-suffix input{border:0;flex:1}
.policy-form fieldset{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,10rem),1fr));gap:.5rem;padding:0;border:0;margin:0 0 1rem}
.policy-form legend{margin-bottom:.6rem;color:var(--color-ink-soft);font-size:.85rem;font-weight:600}
@media (prefers-reduced-motion:reduce){.tab{transition:none}}
@media (max-width:48rem){
  .member-table{min-width:0;table-layout:auto}
  .member-table colgroup,.member-table thead{display:none}
  .member-table,.member-table tbody,.member-table tr,.member-table td{display:block;width:100%}
  .member-table tr{border-bottom:1px solid var(--color-border);padding:.6rem 0}
  .member-table td{border:0;padding:.2rem 0}
  .member-actions{justify-content:flex-start}
  .week-hours{margin-left:7.5rem}
  .week-row{grid-template-columns:1.5rem 5rem minmax(0,1fr)}
  .week-day{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
}
@media (max-width:30rem){
  .week-hours li:nth-child(even){display:none}
  .break-toggle{flex:1 1 100%}
  .item-row{flex-wrap:wrap}
  .item-row>input{flex:1 1 100%}
  .invitation-row{grid-template-columns:minmax(0,1fr)}
  .invitation-tags{justify-content:flex-start}
  .work-hours label{flex:1;min-width:0}
  .work-hours select{min-width:0;width:100%}
  .holiday-form{display:grid;grid-template-columns:1fr}
  .holiday-row{flex-wrap:wrap}
  .holiday-row>.date-field{max-width:none;width:100%}
}
</style>
