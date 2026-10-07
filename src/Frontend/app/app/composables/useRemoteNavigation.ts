export type RemoteId = 'identity' | 'timesheet' | 'reporting' | 'administration'

export interface RemoteNavigationItem {
  id: RemoteId
  label: string
  labelEn: string
  path: `/remote/${RemoteId}`
  description: string
  descriptionEn: string
}

const navigation: readonly RemoteNavigationItem[] = [
  {
    id: 'identity',
    label: 'โปรไฟล์',
    labelEn: 'Profile',
    path: '/remote/identity',
    description: 'จัดการข้อมูลส่วนตัว',
    descriptionEn: 'Manage your profile.'
  },
  {
    id: 'timesheet',
    label: 'บันทึกเวลา',
    labelEn: 'Timesheet',
    path: '/remote/timesheet',
    description: 'บันทึกเวลาทำงานและจัดการงานส่วนตัว',
    descriptionEn: 'Record time and manage personal tasks.'
  },
  {
    id: 'reporting',
    label: 'รายงาน',
    labelEn: 'Reports',
    path: '/remote/reporting',
    description: 'ดูรายงานและเอกสารที่ลงนาม',
    descriptionEn: 'View reports and signed documents.'
  },
  {
    id: 'administration',
    label: 'ตั้งค่า',
    labelEn: 'Administration',
    path: '/remote/administration',
    description: 'จัดการข้อมูลองค์กรและนโยบาย',
    descriptionEn: 'Manage tenant settings and policies.'
  }
]

export function useRemoteNavigation() {
  return { navigation }
}
