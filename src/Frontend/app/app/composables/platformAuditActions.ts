/** Every audit action the SysAdmin console can filter by, with its label in each language. */
export const platformAuditActions = [
  'organization.created', 'organization.updated', 'organization.suspended', 'organization.reactivated',
  'administrator.invited', 'administrator.activation_resent', 'administrator.demoted', 'administrator.removed',
  'platform_admin.invited', 'platform_admin.activation_resent', 'platform_admin.deactivated', 'platform_admin.reactivated',
  'platform_admin.signed_in', 'platform_admin.sign_in_failed'
] as const

export type PlatformAuditAction = typeof platformAuditActions[number]

export const platformAuditActionLabels: Record<'th' | 'en', Record<PlatformAuditAction, string>> = {
  th: {
    'organization.created': 'สร้างองค์กร', 'organization.updated': 'แก้ไขข้อมูลองค์กร',
    'organization.suspended': 'ระงับองค์กร', 'organization.reactivated': 'เปิดใช้งานองค์กรอีกครั้ง',
    'administrator.invited': 'เชิญผู้ดูแลองค์กร', 'administrator.activation_resent': 'ส่งรหัสเปิดใช้งานใหม่',
    'administrator.demoted': 'ลดสิทธิ์ผู้ดูแลองค์กร', 'administrator.removed': 'นำผู้ดูแลองค์กรออก',
    'platform_admin.invited': 'เชิญผู้ดูแลระบบ', 'platform_admin.activation_resent': 'ส่งรหัสเปิดใช้งานผู้ดูแลระบบใหม่',
    'platform_admin.deactivated': 'ปิดใช้งานผู้ดูแลระบบ', 'platform_admin.reactivated': 'เปิดใช้งานผู้ดูแลระบบอีกครั้ง',
    'platform_admin.signed_in': 'ผู้ดูแลระบบเข้าสู่ระบบ', 'platform_admin.sign_in_failed': 'เข้าสู่ระบบผู้ดูแลระบบไม่สำเร็จ'
  },
  en: {
    'organization.created': 'Organization created', 'organization.updated': 'Organization details changed',
    'organization.suspended': 'Organization suspended', 'organization.reactivated': 'Organization reactivated',
    'administrator.invited': 'Administrator invited', 'administrator.activation_resent': 'Activation code re-sent',
    'administrator.demoted': 'Administrator demoted', 'administrator.removed': 'Administrator removed',
    'platform_admin.invited': 'System administrator invited', 'platform_admin.activation_resent': 'System administrator activation re-sent',
    'platform_admin.deactivated': 'System administrator deactivated', 'platform_admin.reactivated': 'System administrator reactivated',
    'platform_admin.signed_in': 'System administrator signed in', 'platform_admin.sign_in_failed': 'System administrator sign-in failed'
  }
}
