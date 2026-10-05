export type RemoteId = 'identity' | 'timesheet' | 'reporting' | 'administration'

export interface RemoteNavigationItem {
  id: RemoteId
  label: string
  path: `/remote/${RemoteId}`
  description: string
}

const navigation: readonly RemoteNavigationItem[] = [
  {
    id: 'timesheet',
    label: 'Timesheet',
    path: '/remote/timesheet',
    description: 'Record working time and manage personal tasks.'
  },
  {
    id: 'reporting',
    label: 'Reports',
    path: '/remote/reporting',
    description: 'View generated reports and signed documents.'
  },
  {
    id: 'administration',
    label: 'Administration',
    path: '/remote/administration',
    description: 'Manage tenant settings, people, and policies.'
  }
]

export function useRemoteNavigation() {
  return { navigation }
}
