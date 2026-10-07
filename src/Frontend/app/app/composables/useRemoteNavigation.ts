export type RemoteId = 'identity' | 'timesheet' | 'reporting' | 'administration'

export interface RemoteNavigationItem {
  id: RemoteId
  label: string
  path: `/remote/${RemoteId}`
  description: string
}

const navigation: readonly RemoteNavigationItem[] = [
  {
    id: 'identity',
    label: 'โปรไฟล์',
    path: '/remote/identity',
    description: 'จัดการข้อมูลส่วนตัว'
  },
  {
    id: 'timesheet',
    label: 'บันทึกเวลา',
    path: '/remote/timesheet',
    description: 'Record working time and manage personal tasks.'
  },
  {
    id: 'reporting',
    label: 'รายงาน',
    path: '/remote/reporting',
    description: 'View generated reports and signed documents.'
  },
  {
    id: 'administration',
    label: 'ตั้งค่า',
    path: '/remote/administration',
    description: 'Manage tenant settings, people, and policies.'
  }
]

export function useRemoteNavigation() {
  return { navigation }
}
