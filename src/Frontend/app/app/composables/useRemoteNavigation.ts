export type RemoteId = 'identity' | 'timesheet' | 'reporting' | 'administration'

export interface RemoteNavigationItem {
  id: RemoteId
  label: { th: string; en: string }
  path: `/remote/${RemoteId}`
  description: { th: string; en: string }
}

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
    description: { th: 'บันทึกเวลาทำงานและจัดการงานส่วนตัว', en: 'Record time and manage personal tasks.' }
  },
  {
    id: 'reporting',
    label: { th: 'รายงาน', en: 'Reports' },
    path: '/remote/reporting',
    description: { th: 'ดูรายงานและเอกสารที่ลงนาม', en: 'View reports and signed documents.' }
  },
  {
    id: 'administration',
    label: { th: 'ตั้งค่า', en: 'Administration' },
    path: '/remote/administration',
    description: { th: 'จัดการข้อมูลองค์กรและนโยบาย', en: 'Manage tenant settings and policies.' }
  }
]

export function useRemoteNavigation() {
  return { navigation }
}
