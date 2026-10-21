<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, useLocaleText, type Locale } from '../../../shared/i18n'
import { DATE_FORMATS, TIME_FORMATS, formatDate as formatCalendarDate, formatTime, readFormats, type Formats } from '../../../shared/date-format'
type Member = { id: string; firstName: string; lastName: string; email: string; role: string; status: string; isCurrentUser: boolean; groupId: string | null; groupName: string | null }
type MemberGroup = { id: string; name: string; memberCount: number }
type Invitation = { id: string; email: string; role: string; invitedByEmail: string | null; createdAtUtc: string; expiresAtUtc: string; groupName?: string | null }
type Catalog = { projects: { id: string; name: string }[]; categories: { id: string; name: string }[]; holidays: { id: string; date: string; name: string }[]; workingDays: number[]; workHours?: WorkHours }
type WorkHours = { startTime: string; endTime: string; breakStart?: string | null; breakEnd?: string | null }
const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [], workingDays: [1, 2, 3, 4, 5] })
const projectName = ref('')
const categoryName = ref('')
const holidayName = ref('')
const holidayDate = ref('')
// Weekday numbers follow Date.getDay() (Sunday = 0); the panel lists Monday first.
const weekdays = [1, 2, 3, 4, 5, 6, 0]
const workingDays = ref<number[]>([1, 2, 3, 4, 5])
const workHours = reactive<WorkHours>({ startTime: '09:00', endTime: '17:00' })
// The break being edited; it is saved only while hasBreak is on.
const hasBreak = ref(false)
const breakTime = reactive({ start: '12:00', end: '13:00' })
/** Every quarter hour of the day, as HH:mm. */
const quarterHours = Array.from({ length: 96 }, (_, index) =>
  `${String(Math.floor(index / 4)).padStart(2, '0')}:${String(index % 4 * 15).padStart(2, '0')}`)
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
const inviteEmail = ref('')
const inviteRole = ref<'Employee' | 'TenantAdmin'>('Employee')
const inviteGroup = ref('')
const isInviting = ref(false)
type Organization = { name: string; organizationCode: string; logoUrl: string | null; dateFormat?: string; timeFormat?: string }
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
  eyebrow: 'WORKSPACE SETTINGS',
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
  invalidDate: 'กรุณากรอกวันที่ให้ถูกต้อง เช่น 31/12/2026', pickDate: 'เลือกจากปฏิทิน',
  formatsTitle: 'รูปแบบวันที่และเวลา', formatsHint: 'วันที่และเวลาทุกหน้าและในรายงานของทุกคนในองค์กรจะแสดงตามรูปแบบนี้',
  dateFormat: 'รูปแบบวันที่', timeFormat: 'รูปแบบเวลา', formatPreview: 'ตัวอย่าง', saveFormats: 'บันทึกรูปแบบ', formatsSaved: 'บันทึกรูปแบบวันที่และเวลาแล้ว',
  buddhistYear: 'ปี พ.ศ.', monthName: 'ชื่อเดือน', hours24: '24 ชั่วโมง', hours12: '12 ชั่วโมง (AM/PM)',
  workWeek: 'วันและเวลาทำงาน', workWeekHint: 'เลือกวันที่องค์กรทำงาน วันอื่นจะแสดงเป็นวันหยุดประจำสัปดาห์ในปฏิทินของทีม และกำหนดเวลาเริ่มและเลิกงาน ซึ่งจะเป็นเวลาตั้งต้นของรายการใหม่',
  workDays: 'วันทำงาน', workStart: 'เวลาเริ่มงาน', workEnd: 'เวลาเลิกงาน', hoursOrder: 'เวลาเลิกงานต้องอยู่หลังเวลาเริ่มงาน',
  hasBreak: 'มีเวลาพัก (ไม่นับเป็นเวลาทำงาน)', breakStart: 'เริ่มพัก', breakEnd: 'สิ้นสุดพัก', breakOrder: 'เวลาพักต้องอยู่ในเวลาทำงาน และสิ้นสุดหลังเวลาเริ่มพัก',
  workdayLength: 'ทำงานปกติวันละ {hours} ชั่วโมง เกินจากนี้นับเป็นวันทำงานล่วงเวลาในรายงาน',
  saveWorkWeek: 'บันทึกวันและเวลาทำงาน', workWeekSaved: 'บันทึกวันและเวลาทำงานแล้ว', workWeekRequired: 'ต้องมีวันทำงานอย่างน้อยหนึ่งวัน'
}, en: {
  title: 'Administration', subtitle: 'Manage projects, categories, holidays and access requirements in one place.', signIn: 'Please sign in through Aiyara Shell',
  eyebrow: 'WORKSPACE SETTINGS',
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
  invalidDate: 'Enter a real date, for example 31/12/2026.', pickDate: 'Choose from the calendar',
  formatsTitle: 'Date and time format', formatsHint: 'Every page and report shows dates and times this way for everyone in the organization.',
  dateFormat: 'Date format', timeFormat: 'Time format', formatPreview: 'Preview', saveFormats: 'Save format', formatsSaved: 'Date and time format saved.',
  buddhistYear: 'Buddhist year', monthName: 'month name', hours24: '24-hour', hours12: '12-hour (AM/PM)',
  workWeek: 'Working days and hours', workWeekHint: 'Choose the days your organization works (other days are shaded as weekly days off on the team calendar) and when the working day starts and ends, which new entries start with.',
  workDays: 'Working days', workStart: 'Start of work', workEnd: 'End of work', hoursOrder: 'The working day must end after it starts.',
  hasBreak: 'Break (not counted as work)', breakStart: 'Break starts', breakEnd: 'Break ends', breakOrder: 'The break must be within the working hours and end after it starts.',
  workdayLength: 'A normal working day is {hours} hours; reports count a longer day as overtime.',
  saveWorkWeek: 'Save working days and hours', workWeekSaved: 'Working days and hours saved.', workWeekRequired: 'Keep at least one working day.'
} })
onMounted(() => {
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.source !== window.parent || event.origin !== config.public.shellOrigin ||
      event.data?.type !== 'aiyara.session') return
    // Later messages only renew the token or locale; reloading would overwrite unsaved edits.
    const firstSession = !token.value
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = readSessionLocale(event.data)
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
</script>
<template>
  <main class="admin-page">
    <header class="page-heading">
      <div><p class="eyebrow">{{ copy.eyebrow }}</p><h1>{{ copy.title }}</h1><p class="subtitle">{{ copy.subtitle }}</p></div>
      <div v-if="token && tenantId" class="tenant-pill"><span class="tenant-dot"></span><span>{{ copy.profile }}</span><strong>{{ tenantId }}</strong></div>
    </header>
    <div v-if="!token" class="empty-state"><span class="empty-icon">⌑</span><h2>{{ copy.signIn }}</h2></div>
    <template v-else>
      <p v-if="error" class="feedback feedback-error" role="alert"><span>{{ error }}</span><button class="button button-subtle" type="button" @click="load">{{ copy.retry }}</button></p>
      <p v-if="notice" class="feedback feedback-success" role="status"><span class="success-dot"></span>{{ notice }}</p>
      <div class="stats-grid" :aria-label="copy.title">
        <article class="stat-card"><span class="stat-icon violet">▦</span><div><strong>{{ catalog.projects.length }}</strong><span>{{ copy.countProjects }}</span></div></article>
        <article class="stat-card"><span class="stat-icon blue">◇</span><div><strong>{{ catalog.categories.length }}</strong><span>{{ copy.countCategories }}</span></div></article>
        <article class="stat-card"><span class="stat-icon orange">▣</span><div><strong>{{ catalog.holidays.length }}</strong><span>{{ copy.countHolidays }}</span></div></article>
      </div>
      <p v-if="isLoading" class="loading-line" role="status">{{ copy.loading }}</p>
      <section class="panel brand-panel" aria-labelledby="organization-heading"><div class="panel-heading"><span class="panel-icon violet">◈</span><div><h2 id="organization-heading">{{ copy.organization }}</h2><p>{{ copy.organizationHint }}</p></div></div><div class="brand-grid"><form class="add-form brand-name-form" @submit.prevent="saveName"><label class="invite-email"><span>{{ copy.organizationName }}</span><input v-model="organizationName" required maxlength="200" /></label><button class="button button-primary" :disabled="isSavingBrand || !organizationName.trim() || organizationName.trim() === organization?.name">{{ copy.saveName }}</button></form><div class="brand-logo-field"><span class="field-label">{{ copy.logo }}</span><div class="logo-row"><div class="logo-preview"><img v-if="logoPreview" :src="logoPreview" :alt="copy.logo" /><span v-else>{{ copy.noLogo }}</span></div><div class="logo-actions"><label class="button button-subtle file-button">{{ logoPreview ? copy.replaceLogo : copy.uploadLogo }}<input class="sr-only" type="file" accept="image/png,image/jpeg,image/webp" :disabled="isSavingBrand" @change="uploadLogo" /></label><button v-if="logoPreview" class="button button-danger-text" type="button" :disabled="isSavingBrand" @click="removeLogo">{{ copy.removeLogo }}</button></div></div><p class="field-hint">{{ copy.logoHint }}</p></div></div></section>
      <section class="panel formats-panel" aria-labelledby="formats-heading"><div class="panel-heading"><span class="panel-icon blue">◷</span><div><h2 id="formats-heading">{{ copy.formatsTitle }}</h2><p>{{ copy.formatsHint }}</p></div></div><form class="add-form formats-form" @submit.prevent="saveFormats"><label><span>{{ copy.dateFormat }}</span><select v-model="formatForm.dateFormat"><option v-for="format in DATE_FORMATS" :key="format" :value="format">{{ dateOptionLabel(format) }}</option></select></label><label><span>{{ copy.timeFormat }}</span><select v-model="formatForm.timeFormat"><option v-for="format in TIME_FORMATS" :key="format" :value="format">{{ timeOptionLabel(format) }}</option></select></label><p class="format-preview" aria-live="polite"><span>{{ copy.formatPreview }}</span><strong>{{ formatPreview }}</strong></p><button class="button button-primary" :disabled="isSavingBrand">{{ isSavingBrand ? copy.loading : copy.saveFormats }}</button></form></section>
      <section class="panel members-panel"><div class="panel-heading"><span class="panel-icon green">◎</span><div><h2>{{ copy.members }}</h2><p>{{ copy.memberHint }}</p></div></div><form class="add-form invite-form" @submit.prevent="invite"><label class="invite-email"><span>{{ copy.inviteEmail }}</span><input v-model="inviteEmail" required type="email" maxlength="320" autocomplete="off" /></label><label><span>{{ copy.role }}</span><select v-model="inviteRole"><option value="Employee">{{ copy.roleEmployee }}</option><option value="TenantAdmin">{{ copy.roleTenantAdmin }}</option></select></label><label v-if="groups.length"><span>{{ copy.group }}</span><select v-model="inviteGroup" :aria-label="copy.group"><option value="">{{ copy.noGroup }}</option><option v-for="group in groups" :key="group.id" :value="group.id">{{ group.name }}</option></select></label><button class="button button-primary" :disabled="isInviting">{{ isInviting ? copy.inviting : copy.invite }}</button></form><ul v-if="members.length" class="item-list" :aria-label="copy.members"><li v-for="member in members" :key="member.id" class="member-row"><div class="member-identity"><strong>{{ memberName(member) || member.email }}<span v-if="member.isCurrentUser" class="badge badge-self">{{ copy.you }}</span></strong><span v-if="memberName(member)">{{ member.email }}</span></div><span class="badge">{{ roleLabel(member.role) }}</span><span class="badge" :class="`status-${member.status}`">{{ statusLabel(member.status) }}</span><label class="group-select"><span class="sr-only">{{ copy.group }}: {{ member.email }}</span><select :value="member.groupId ?? ''" :disabled="busyId === member.id" :aria-label="`${copy.group}: ${member.email}`" @change="setGroup(member, ($event.target as HTMLSelectElement).value)"><option value="">{{ copy.noGroup }}</option><option v-for="group in groups" :key="group.id" :value="group.id">{{ group.name }}</option></select></label><div v-if="!member.isCurrentUser" class="row-actions member-actions"><template v-if="member.status === 'Deactivated'"><button class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${copy.reactivate}: ${member.email}`" @click="setActive(member, true)">{{ copy.reactivate }}</button></template><template v-else><button class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${member.role === 'TenantAdmin' ? copy.makeEmployee : copy.makeAdmin}: ${member.email}`" @click="changeRole(member)">{{ member.role === 'TenantAdmin' ? copy.makeEmployee : copy.makeAdmin }}</button><button v-if="member.status === 'PendingActivation'" class="button button-subtle" type="button" :disabled="busyId === member.id" :aria-label="`${copy.resendActivation}: ${member.email}`" @click="resendActivation(member)">{{ copy.resendActivation }}</button><template v-if="confirmingId === member.id"><button class="button button-warning" type="button" :disabled="busyId === member.id" :aria-label="`${copy.confirmDeactivate}: ${member.email}`" @click="setActive(member, false)">{{ copy.confirmDeactivate }}</button><button class="button button-subtle" type="button" @click="confirmingId = ''">{{ copy.cancel }}</button></template><button v-else class="button button-danger-text" type="button" :disabled="busyId === member.id" :aria-label="`${copy.deactivate}: ${member.email}`" @click="setActive(member, false)">{{ copy.deactivate }}</button></template></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p><h3 class="subheading">{{ copy.groups }}</h3><p class="field-hint group-hint">{{ copy.groupHint }}</p><form class="add-form" @submit.prevent="addGroup"><label class="sr-only" for="new-group">{{ copy.groupName }}</label><input id="new-group" v-model="groupName" required maxlength="100" :placeholder="copy.groupName" /><button class="button button-primary" :disabled="busyId === 'new-group'">{{ copy.addGroup }}</button></form><ul v-if="groups.length" class="item-list" :aria-label="copy.groups"><li v-for="group in groups" :key="group.id" class="item-row"><input v-model="group.name" required maxlength="100" :aria-label="copy.groupName" /><span class="badge">{{ copy.groupMembers.replace('{count}', String(group.memberCount)) }}</span><div class="row-actions"><button class="button button-subtle" type="button" :disabled="busyId === group.id" @click="renameGroup(group)">{{ copy.save }}</button><button class="button button-danger" type="button" :disabled="busyId === group.id" :aria-label="`${copy.delete}: ${group.name}`" @click="deleteGroup(group)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.noGroups }}</p><h3 class="subheading">{{ copy.pendingInvitations }}</h3><ul v-if="invitations.length" class="item-list" :aria-label="copy.pendingInvitations"><li v-for="invitation in invitations" :key="invitation.id" class="member-row"><div class="member-identity"><strong>{{ invitation.email }}</strong><span>{{ copy.expires.replace('{date}', formatDate(invitation.expiresAtUtc)) }}<template v-if="invitation.invitedByEmail"> · {{ copy.invitedBy.replace('{email}', invitation.invitedByEmail) }}</template></span></div><span class="badge">{{ roleLabel(invitation.role) }}</span><span v-if="invitation.groupName" class="badge">{{ invitation.groupName }}</span><div class="row-actions member-actions"><button class="button button-subtle" type="button" :disabled="busyId === invitation.id" :aria-label="`${copy.resendInvitation}: ${invitation.email}`" @click="resendInvitation(invitation)">{{ copy.resendInvitation }}</button><button class="button button-danger-text" type="button" :disabled="busyId === invitation.id" :aria-label="`${copy.revokeInvitation}: ${invitation.email}`" @click="revokeInvitation(invitation)">{{ copy.revokeInvitation }}</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.noInvitations }}</p></section>
      <div class="catalog-grid">
        <section class="panel"><div class="panel-heading"><span class="panel-icon violet">▦</span><div><h2>{{ copy.projects }}</h2><p>{{ copy.projectHint }}</p></div></div><form class="add-form" @submit.prevent="add('projects')"><label class="sr-only" for="new-project">{{ copy.projectName }}</label><input id="new-project" v-model="projectName" required maxlength="200" :placeholder="copy.projectName" /><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.projects.length" class="item-list"><li v-for="item in catalog.projects" :key="item.id" class="item-row"><input v-model="item.name" :aria-label="copy.projectName" /><div class="row-actions"><button class="button button-subtle" @click="update('projects', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('projects', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
        <section class="panel"><div class="panel-heading"><span class="panel-icon blue">◇</span><div><h2>{{ copy.categories }}</h2><p>{{ copy.categoryHint }}</p></div></div><form class="add-form" @submit.prevent="add('categories')"><label class="sr-only" for="new-category">{{ copy.categoryName }}</label><input id="new-category" v-model="categoryName" required maxlength="200" :placeholder="copy.categoryName" /><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.categories.length" class="item-list"><li v-for="item in catalog.categories" :key="item.id" class="item-row"><input v-model="item.name" :aria-label="copy.categoryName" /><div class="row-actions"><button class="button button-subtle" @click="update('categories', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('categories', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
        <section class="panel panel-wide work-week-panel" aria-labelledby="work-week-heading"><div class="panel-heading"><span class="panel-icon green">▤</span><div><h2 id="work-week-heading">{{ copy.workWeek }}</h2><p>{{ copy.workWeekHint }}</p></div></div><form class="work-week-form" @submit.prevent="saveWorkWeek"><fieldset><legend class="sr-only">{{ copy.workDays }}</legend><label v-for="day in weekdays" :key="day" class="check-card" :title="workingDays.length === 1 && workingDays.includes(day) ? copy.workWeekRequired : undefined"><input v-model="workingDays" type="checkbox" :value="day" :disabled="workingDays.length === 1 && workingDays.includes(day)" /><span class="check-mark"></span><span>{{ weekdayName(day) }}</span></label></fieldset><div class="work-hours"><label><span>{{ copy.workStart }}</span><select v-model="workHours.startTime" :aria-invalid="!hoursValid"><option v-for="time in quarterHours" :key="time" :value="time">{{ formatTime(time, formats) }}</option></select></label><span class="work-hours-dash" aria-hidden="true">–</span><label><span>{{ copy.workEnd }}</span><select v-model="workHours.endTime" :aria-invalid="!hoursValid" :aria-describedby="hoursValid ? undefined : 'work-hours-error'"><option v-for="time in quarterHours" :key="time" :value="time">{{ formatTime(time, formats) }}</option></select></label><p v-if="!hoursValid" id="work-hours-error" class="field-error" role="alert">{{ copy.hoursOrder }}</p></div><div class="work-hours break-hours"><label class="check-card break-toggle"><input v-model="hasBreak" type="checkbox" /><span class="check-mark"></span><span>{{ copy.hasBreak }}</span></label><template v-if="hasBreak"><label><span>{{ copy.breakStart }}</span><select v-model="breakTime.start" :aria-invalid="!breakValid"><option v-for="time in quarterHours" :key="time" :value="time">{{ formatTime(time, formats) }}</option></select></label><span class="work-hours-dash" aria-hidden="true">–</span><label><span>{{ copy.breakEnd }}</span><select v-model="breakTime.end" :aria-invalid="!breakValid" :aria-describedby="breakValid ? undefined : 'break-error'"><option v-for="time in quarterHours" :key="time" :value="time">{{ formatTime(time, formats) }}</option></select></label></template><p v-if="!breakValid" id="break-error" class="field-error" role="alert">{{ copy.breakOrder }}</p><p v-else-if="hoursValid" class="workday-summary">{{ copy.workdayLength.replace('{hours}', (workdayMinutes / 60).toFixed(2).replace(/\.?0+$/, '')) }}</p></div><button class="button button-primary" :disabled="isSavingWorkWeek || !hoursValid || !breakValid">{{ isSavingWorkWeek ? copy.loading : copy.saveWorkWeek }}</button></form></section>
        <section class="panel panel-wide"><div class="panel-heading"><span class="panel-icon orange">▣</span><div><h2>{{ copy.holidays }}</h2><p>{{ copy.holidayHint }}</p></div></div><form class="add-form holiday-form" @submit.prevent="add('holidays')"><label><span>{{ copy.date }}</span><DateField v-model="holidayDate" required :formats="formats" :locale="locale" :label="copy.date" :invalid-message="copy.invalidDate" :pick-label="copy.pickDate" /></label><label class="holiday-name"><span>{{ copy.holidayName }}</span><input v-model="holidayName" required maxlength="200" :placeholder="copy.holidayName" /></label><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.holidays.length" class="item-list"><li v-for="item in catalog.holidays" :key="item.id"><form class="item-row holiday-row" @submit.prevent="update('holidays', item)"><DateField v-model="item.date" required :formats="formats" :locale="locale" :label="`${copy.date}: ${item.name}`" :invalid-message="copy.invalidDate" :pick-label="`${copy.pickDate}: ${item.name}`" /><input v-model="item.name" required maxlength="200" :aria-label="copy.holidayName" /><div class="row-actions"><button class="button button-subtle">{{ copy.save }}</button><button type="button" class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('holidays', item.id)">×</button></div></form></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
      </div>
      <div class="settings-grid">
        <section class="panel policy-panel"><div class="panel-heading"><span class="panel-icon green">⌑</span><div><h2>{{ copy.policy }}</h2><p>{{ copy.policyHint }}</p></div></div><form class="policy-form" @submit.prevent="savePolicy"><div class="policy-fields"><label>{{ copy.minimumLength }}<span class="input-suffix"><input v-model.number="policy.minimumLength" type="number" min="12" max="128" required /><span>{{ copy.lengthUnit }}</span></span></label><label>{{ copy.expiryDays }}<span class="input-suffix"><input v-model.number="policy.expiryDays" type="number" min="1" max="3650" required /><span>{{ copy.expiryUnit }}</span></span></label></div><fieldset><legend>{{ copy.passwordRequirements }}</legend><label class="check-card"><input v-model="policy.requireUppercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.uppercase }}</span></label><label class="check-card"><input v-model="policy.requireLowercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.lowercase }}</span></label><label class="check-card"><input v-model="policy.requireDigit" type="checkbox" /><span class="check-mark"></span><span>{{ copy.digit }}</span></label><label class="check-card"><input v-model="policy.requireSymbol" type="checkbox" /><span class="check-mark"></span><span>{{ copy.symbol }}</span></label></fieldset><button class="button button-primary" :disabled="isSavingPolicy">{{ isSavingPolicy ? copy.loading : copy.savePolicy }}</button></form></section>
        <section class="panel close-panel"><span class="panel-icon orange">◷</span><h2>{{ copy.lockMonth }}</h2><p>{{ copy.lockHint }}</p><form class="close-form" @submit.prevent="lock"><label :aria-label="copy.lockMonth"><input v-model="lockMonth" type="month" required /></label><button class="button button-warning" :disabled="isLocking">{{ isLocking ? copy.loading : copy.lock }}</button></form></section>
      </div>
    </template>
  </main>
</template>
<style>
:root{font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:var(--color-ink,#17233a);background:var(--color-canvas,#f5f7fb)}*{box-sizing:border-box}.admin-page{max-width:76rem;margin:0 auto;padding:clamp(1rem,3vw,2.5rem)}.page-heading{display:flex;justify-content:space-between;align-items:flex-end;gap:1.5rem;margin-bottom:1.75rem}.page-heading h1{font-size:clamp(1.7rem,3vw,2.35rem);letter-spacing:-.04em;line-height:1.15;margin:.2rem 0 .6rem}.eyebrow{font-size:.68rem;font-weight:750;letter-spacing:.14em;color:#74819a;margin:0}.subtitle{color:#6b7890;margin:0;max-width:40rem;line-height:1.55}.tenant-pill{display:flex;align-items:center;gap:.55rem;border:1px solid #e1e7f0;background:#fff;border-radius:999px;padding:.55rem .9rem;color:#74819a;font-size:.78rem;white-space:nowrap}.tenant-pill strong{color:#263753;font-size:.82rem}.tenant-dot,.success-dot{width:.5rem;height:.5rem;border-radius:50%;background:#39a77b}.stats-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem;margin-bottom:1.25rem}.stat-card,.panel{background:#fff;border:1px solid #e6eaf1;border-radius:1rem;box-shadow:0 5px 18px #253d6509}.stat-card{display:flex;align-items:center;gap:.9rem;padding:1rem 1.15rem}.stat-card div{display:grid;gap:.15rem}.stat-card strong{font-size:1.45rem;line-height:1.2;color:#1c2c47}.stat-card div span{font-size:.78rem;color:#758198}.stat-icon,.panel-icon{display:grid;place-items:center;flex:0 0 auto;width:2.4rem;height:2.4rem;border-radius:.75rem;font-weight:700;font-size:1.05rem}.violet{background:#f0ebff;color:#7751c8}.blue{background:#e8f2ff;color:#377ac8}.orange{background:#fff1e6;color:#ca762c}.green{background:#e8f7f0;color:#348c67}.catalog-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.panel{padding:1.25rem;min-width:0}.panel-wide{grid-column:1/-1}.panel-heading{display:flex;align-items:flex-start;gap:.8rem;margin-bottom:1.1rem}.panel-heading h2,.close-panel h2{font-size:1rem;line-height:1.3;margin:.05rem 0 .3rem;color:#263753}.panel-heading p,.close-panel p{font-size:.78rem;line-height:1.5;color:#8490a4;margin:0}.add-form{display:flex;gap:.55rem;margin-bottom:.85rem}.add-form>input,.add-form label input,.item-row>input,.item-row .date-field input[type="text"],.policy-fields input,.close-form input{min-width:0;width:100%;height:2.55rem;border:1px solid #dfe5ee;border-radius:.65rem;background:#fbfcfe;padding:0 .75rem;color:#263753;font:inherit;font-size:.83rem;outline:none}.add-form input:focus,.item-row input:focus,.policy-fields input:focus,.close-form input:focus{border-color:#8da9ed;box-shadow:0 0 0 3px #4977e51c}.button{border:0;border-radius:.62rem;padding:.62rem .85rem;font:inherit;font-size:.78rem;font-weight:650;cursor:pointer;white-space:nowrap;transition:background .15s ease,transform .15s ease}.button:hover:not(:disabled){transform:translateY(-1px)}.button:disabled{opacity:.58;cursor:wait}.button-primary{background:#355fd4;color:#fff}.button-primary:hover:not(:disabled){background:#294db8}.button-subtle{background:#f0f3f9;color:#475978}.button-danger{background:#fff1f0;color:#be514b;padding:.42rem .72rem;font-size:1.05rem}.button-warning{background:#a6483f;color:#fff}.item-list{list-style:none;margin:0;padding:0;display:grid;gap:.5rem}.item-row{display:flex;align-items:center;gap:.5rem;border:1px solid #edf0f5;background:#fff;border-radius:.7rem;padding:.4rem}.item-row>input,.item-row .date-field input[type="text"]{height:2.25rem;background:transparent;border-color:transparent}.row-actions{display:flex;align-items:center;gap:.35rem}.empty-list{margin:.6rem 0 0;padding:.85rem;border-radius:.65rem;background:#f8f9fc;color:#95a0b2;text-align:center;font-size:.8rem}.holiday-form{align-items:flex-end}.holiday-form label{display:grid;gap:.35rem;min-width:9rem;font-size:.72rem;font-weight:600;color:#64718a}.holiday-form .holiday-name{flex:1}.holiday-row>.date-field{max-width:11rem}.settings-grid{display:grid;grid-template-columns:minmax(0,1.4fr) minmax(16rem,.8fr);gap:1rem;margin-top:1rem}.policy-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.8rem;margin-bottom:1rem}.policy-fields>label{display:grid;gap:.45rem;color:#64718a;font-size:.75rem;font-weight:600}.input-suffix{display:flex;align-items:center;gap:.5rem;border:1px solid #dfe5ee;border-radius:.65rem;padding-right:.75rem;background:#fbfcfe;color:#8893a5;font-size:.7rem}.input-suffix input{border:0;background:transparent;box-shadow:none!important}.policy-form fieldset{padding:0;border:0;margin:0 0 1rem}.policy-form legend{margin-bottom:.6rem;color:#64718a;font-size:.75rem;font-weight:650}.policy-form fieldset{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.5rem}.check-card{position:relative;display:flex;align-items:center;gap:.55rem;border:1px solid #e8ecf2;border-radius:.65rem;padding:.65rem .7rem;color:#52617c;font-size:.75rem;cursor:pointer}.check-card input{position:absolute;opacity:0}.check-mark{flex:0 0 auto;width:1rem;height:1rem;border:1px solid #c9d1df;border-radius:.3rem;background:#fff}.check-card input:checked+.check-mark{border-color:#4266d5;background:#4266d5;box-shadow:inset 0 0 0 3px #fff}.check-card:focus-within{outline:3px solid #4977e51c}.work-week-form{display:flex;align-items:flex-end;gap:.75rem;flex-wrap:wrap}.work-week-form fieldset{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));gap:.5rem;flex:1 1 100%;min-width:0;padding:0;border:0;margin:0}.work-hours{display:flex;align-items:flex-end;gap:.5rem;flex-wrap:wrap;flex:1;min-width:0}.work-hours label{display:grid;gap:.35rem;font-size:.72rem;font-weight:600;color:#64718a}.work-hours select{height:2.55rem;min-width:8rem;border:1px solid #dfe5ee;border-radius:.65rem;background:#fbfcfe;padding:0 .6rem;color:#263753;font:inherit;font-size:.83rem}.work-hours select[aria-invalid="true"]{border-color:#d0645d}.work-hours-dash{padding-bottom:.7rem;color:#8490a4}.break-hours{flex:1 1 100%}.break-hours .break-toggle{display:flex;align-items:center;gap:.55rem;flex:1 1 100%;max-width:22rem;color:#52617c;font-size:.75rem}.workday-summary{flex-basis:100%;margin:0;color:#64718a;font-size:.75rem}.field-error{flex-basis:100%;margin:0;color:#a53d38;font-size:.75rem}.work-week-form .check-card input:disabled+.check-mark{opacity:.6}.close-panel{align-self:start}.close-panel>.panel-icon{margin-bottom:.8rem}.close-form{display:flex;align-items:center;gap:.55rem;margin-top:1.15rem}.close-form label{flex:1}.feedback{display:flex;align-items:center;justify-content:space-between;gap:.75rem;padding:.8rem 1rem;border-radius:.7rem;margin:0 0 1rem;font-size:.82rem}.feedback-error{background:#fff0ef;color:#a53d38}.feedback-success{justify-content:flex-start;background:#eaf8f1;color:#287751}.loading-line{color:#77849a;font-size:.8rem;margin:.2rem 0 .75rem}.empty-state{display:grid;place-items:center;min-height:15rem;border:1px dashed #d9dfeb;border-radius:1rem;background:#fff;color:#74819a;text-align:center}.empty-state h2{font-size:1rem;font-weight:550}.empty-icon{font-size:2rem;color:#a8b2c4}.sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}.members-panel{margin-bottom:1rem}.brand-panel{margin-bottom:1rem}.formats-panel{margin-bottom:1rem}.formats-form{align-items:flex-end;flex-wrap:wrap}.formats-form label{display:grid;gap:.35rem;font-size:.72rem;font-weight:600;color:#64718a}.formats-form select{height:2.55rem;min-width:12rem;border:1px solid #dfe5ee;border-radius:.65rem;background:#fbfcfe;padding:0 .6rem;color:#263753;font:inherit;font-size:.83rem}.format-preview{display:grid;gap:.35rem;margin:0;flex:1;min-width:10rem;font-size:.72rem;font-weight:600;color:#64718a}.format-preview strong{height:2.55rem;display:flex;align-items:center;color:#263753;font-size:.95rem}.brand-grid{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:1.25rem}.brand-name-form{align-items:flex-end}.brand-name-form label{display:grid;gap:.35rem;font-size:.72rem;font-weight:600;color:#64718a}.field-label{display:block;font-size:.72rem;font-weight:600;color:#64718a;margin-bottom:.35rem}.logo-row{display:flex;align-items:center;gap:.75rem;flex-wrap:wrap}.logo-preview{display:grid;place-items:center;width:7rem;height:3.5rem;border:1px dashed #d9dfeb;border-radius:.65rem;background:#fbfcfe;color:#95a0b2;font-size:.72rem;text-align:center;padding:.3rem}.logo-preview img{display:block;max-width:6.4rem;max-height:2.9rem;object-fit:contain}.logo-actions{display:flex;gap:.45rem;flex-wrap:wrap}.file-button{cursor:pointer}.field-hint{margin:.5rem 0 0;color:#8490a4;font-size:.72rem}.invite-form{align-items:flex-end}.invite-form label{display:grid;gap:.35rem;font-size:.72rem;font-weight:600;color:#64718a}.invite-form .invite-email{flex:1}.invite-form select{height:2.55rem;border:1px solid #dfe5ee;border-radius:.65rem;background:#fbfcfe;padding:0 .6rem;color:#263753;font:inherit;font-size:.83rem}.member-row{display:flex;align-items:center;gap:.5rem;border:1px solid #edf0f5;border-radius:.7rem;padding:.6rem .75rem}.member-identity{display:grid;gap:.15rem;flex:1;min-width:0}.member-identity strong{display:flex;align-items:center;gap:.4rem;color:#263753;font-size:.85rem;overflow-wrap:anywhere}.member-identity span{color:#8490a4;font-size:.75rem;overflow-wrap:anywhere}.badge{border-radius:999px;background:#f0f3f9;color:#475978;font-size:.7rem;font-weight:650;padding:.2rem .55rem;white-space:nowrap}.member-identity .badge-self{background:#e8f2ff;color:#377ac8;font-size:.65rem}.status-Active{background:#e8f7f0;color:#287751}.status-PendingActivation{background:#fff1e6;color:#a8611f}.status-Deactivated{background:#fff0ef;color:#a53d38}.member-actions{flex-wrap:wrap;justify-content:flex-end}.button-danger-text{background:#fff1f0;color:#be514b}.subheading{font-size:.85rem;color:#263753;margin:1.2rem 0 .6rem}.group-hint{margin:-.3rem 0 .7rem}.group-select select{height:2rem;max-width:11rem;border:1px solid #dfe5ee;border-radius:.55rem;background:#fbfcfe;padding:0 .45rem;color:#263753;font:inherit;font-size:.75rem}@media(max-width:760px){.brand-grid{grid-template-columns:1fr}.work-week-form fieldset{grid-template-columns:repeat(4,minmax(0,1fr))}.admin-page{padding:1rem}.page-heading{align-items:flex-start;flex-direction:column;margin-bottom:1.2rem}.stats-grid{gap:.6rem}.stat-card{padding:.8rem;gap:.55rem}.stat-icon{width:2rem;height:2rem}.stat-card strong{font-size:1.2rem}.stat-card div span{font-size:.68rem}.settings-grid{grid-template-columns:1fr}}@media(max-width:560px){.work-hours{flex:1 1 100%}.work-hours label{flex:1;min-width:0}.work-hours select{min-width:0;width:100%}.work-week-form>.button{flex:1 1 100%}.formats-form{display:grid;grid-template-columns:1fr}.formats-form select{min-width:0;width:100%}.catalog-grid{grid-template-columns:1fr}.work-week-form fieldset{grid-template-columns:repeat(2,minmax(0,1fr));flex-basis:100%}.panel-wide{grid-column:auto}.stats-grid{grid-template-columns:1fr}.stat-card{padding:.7rem .9rem}.stat-card div{display:flex;align-items:baseline;justify-content:space-between;flex:1}.holiday-form{display:grid;grid-template-columns:1fr}.holiday-form label,.holiday-form .holiday-name{min-width:0}.holiday-row{flex-wrap:wrap}.holiday-row>.date-field{max-width:none;width:100%}.holiday-row>input:nth-child(2){flex:1}.policy-fields{grid-template-columns:1fr}.invite-form{display:grid;grid-template-columns:1fr}.close-form{flex-wrap:wrap}.member-row{flex-wrap:wrap}.member-identity{flex-basis:100%}.member-actions{flex-basis:100%;justify-content:flex-start}.tenant-pill{white-space:normal}.page-heading h1{font-size:1.8rem}}
</style>
