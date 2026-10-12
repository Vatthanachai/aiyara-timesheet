<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
import type { PlatformAuditEntry } from '../composables/usePlatformApi'

const props = withDefaults(defineProps<{ entries: PlatformAuditEntry[]; showOrganization?: boolean }>(), {
  showOrganization: true
})
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    empty: 'ยังไม่มีประวัติการดำเนินการ', unknownActor: 'ไม่ทราบบัญชี', reason: 'เหตุผล',
    'organization.created': 'สร้างองค์กร', 'organization.updated': 'แก้ไขข้อมูลองค์กร',
    'organization.suspended': 'ระงับองค์กร', 'organization.reactivated': 'เปิดใช้งานองค์กรอีกครั้ง',
    'administrator.invited': 'เชิญผู้ดูแลองค์กร', 'administrator.activation_resent': 'ส่งรหัสเปิดใช้งานใหม่',
    'platform_admin.signed_in': 'ผู้ดูแลระบบเข้าสู่ระบบ', 'platform_admin.sign_in_failed': 'เข้าสู่ระบบผู้ดูแลระบบไม่สำเร็จ'
  },
  en: {
    empty: 'No administrative activity yet.', unknownActor: 'Unknown account', reason: 'Reason',
    'organization.created': 'Organization created', 'organization.updated': 'Organization details changed',
    'organization.suspended': 'Organization suspended', 'organization.reactivated': 'Organization reactivated',
    'administrator.invited': 'Administrator invited', 'administrator.activation_resent': 'Activation code re-sent',
    'platform_admin.signed_in': 'System administrator signed in', 'platform_admin.sign_in_failed': 'System administrator sign-in failed'
  }
})

function parse(json: string | null): Record<string, unknown> {
  if (!json) return {}
  try {
    const value = JSON.parse(json)
    return value && typeof value === 'object' ? value as Record<string, unknown> : {}
  } catch { return {} }
}

/** Lists each field the action touched as "Field: before → after" (or just the new value). */
function changes(entry: PlatformAuditEntry) {
  const before = parse(entry.beforeJson)
  const after = parse(entry.afterJson)
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].map(key => {
    const from = before[key]
    const to = after[key]
    const show = (value: unknown) => value === null || value === undefined ? '—' : String(value)
    return key in before && from !== to ? `${key}: ${show(from)} → ${show(to)}` : `${key}: ${show(to ?? from)}`
  })
}

const rows = computed(() => props.entries.map(entry => ({
  ...entry,
  label: (text.value as Record<string, string>)[entry.action] ?? entry.action,
  when: new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium', timeStyle: 'short' })
    .format(new Date(entry.createdAtUtc)),
  changes: changes(entry),
  failed: entry.action.endsWith('_failed')
})))
</script>

<template>
  <p v-if="!rows.length" class="sysadmin-empty">{{ text.empty }}</p>
  <ol v-else class="audit-list">
    <li v-for="row in rows" :key="row.id" class="audit-row" :class="{ 'audit-failed': row.failed }">
      <div class="audit-heading">
        <strong>{{ row.label }}</strong>
        <time :datetime="row.createdAtUtc">{{ row.when }}</time>
      </div>
      <p class="audit-meta">
        <span>{{ row.actorEmail ?? text.unknownActor }}</span>
        <NuxtLink v-if="showOrganization && row.organizationCode && row.organizationCode !== 'AIYA001'"
          :to="`/sys-admin/organizations/${row.tenantId}`">
          {{ row.organizationName }} ({{ row.organizationCode }})
        </NuxtLink>
      </p>
      <ul v-if="row.changes.length" class="audit-changes">
        <li v-for="change in row.changes" :key="change">{{ change }}</li>
      </ul>
      <p v-if="row.reason" class="audit-reason">{{ text.reason }}: {{ row.reason }}</p>
    </li>
  </ol>
</template>
