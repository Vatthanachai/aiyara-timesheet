export type RemoteId = 'identity' | 'timesheet' | 'reporting' | 'administration'

export interface RemoteNavigationItem {
  id: RemoteId
  label: { th: string; en: string }
  path: `/remote/${RemoteId}`
  description: { th: string; en: string }
  /** Roles allowed to open the remote; omitted means every signed-in role. */
  roles?: readonly string[]
}

const tenantRoles = ['TenantAdmin', 'Employee'] as const

const navigation: readonly RemoteNavigationItem[] = [
  {
    id: 'identity',
    label: { th: 'โปรไฟล์', en: 'Profile' },
    path: '/remote/identity',
    description: { th: 'จัดการข้อมูลส่วนตัว', en: 'Manage your profile.' }
  },
  {
    id: 'timesheet',
    label: { th: 'บันทึกเวลา', en: 'Timesheet' },
    path: '/remote/timesheet',
    description: { th: 'บันทึกเวลาทำงานและจัดการงานส่วนตัว', en: 'Record time and manage personal tasks.' },
    roles: tenantRoles
  },
  {
    id: 'reporting',
    label: { th: 'รายงาน', en: 'Reports' },
    path: '/remote/reporting',
    description: { th: 'ดูรายงานและเอกสารที่ลงนาม', en: 'View reports and signed documents.' },
    roles: tenantRoles
  },
  {
    id: 'administration',
    label: { th: 'ตั้งค่า', en: 'Administration' },
    path: '/remote/administration',
    description: { th: 'จัดการข้อมูลองค์กรและนโยบาย', en: 'Manage tenant settings and policies.' },
    roles: ['TenantAdmin']
  }
]

export function useRemoteNavigation() {
  const { role } = useSession()
  // Signed-out visitors (no role yet) see every entry; each remote then asks them to sign in.
  const canOpen = (item: RemoteNavigationItem) => !role.value || !item.roles || item.roles.includes(role.value)
  const visibleNavigation = computed(() => navigation.filter(canOpen))
  return { navigation, visibleNavigation, canOpen }
}
