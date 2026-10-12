<script setup lang="ts">
import { useLocaleText } from '../../../../../shared/i18n'
import type { PlatformAdminSummary, PlatformAuditPage } from '../../../composables/usePlatformApi'

type OrganizationDetail = {
  tenantId: string
  organizationCode: string
  name: string
  slug: string
  status: 'Active' | 'Suspended'
  createdAtUtc: string
  activeMemberCount: number
  pendingMemberCount: number
  admins: PlatformAdminSummary[]
}

const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', back: 'กลับไปรายการองค์กร', code: 'รหัสองค์กร', urlName: 'ชื่อ URL',
    status: 'สถานะ', created: 'สร้างเมื่อ', statusActive: 'ใช้งาน', statusSuspended: 'ระงับ',
    activeMembers: 'สมาชิกที่ใช้งาน', pendingMembers: 'รอเปิดใช้งาน', admins: 'ผู้ดูแลองค์กร',
    adminActive: 'เปิดใช้งานแล้ว', adminPending: 'รอเปิดใช้งาน', noAdmins: 'ไม่พบผู้ดูแลองค์กร',
    history: 'ประวัติการดำเนินการ', fullHistory: 'ดูประวัติทั้งหมด', details: 'ข้อมูลองค์กร',
    loading: 'กำลังโหลด…', notFound: 'ไม่พบองค์กรนี้', loadError: 'โหลดข้อมูลองค์กรไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', back: 'Back to organizations', code: 'Organization code', urlName: 'URL name',
    status: 'Status', created: 'Created', statusActive: 'Active', statusSuspended: 'Suspended',
    activeMembers: 'Active members', pendingMembers: 'Pending activation', admins: 'Administrators',
    adminActive: 'Active', adminPending: 'Pending activation', noAdmins: 'No administrator found.',
    history: 'History', fullHistory: 'Full history', details: 'Organization details',
    loading: 'Loading…', notFound: 'This organization was not found.', loadError: 'The organization could not be loaded. Please try again.'
  }
})
const route = useRoute()
const tenantId = computed(() => String(route.params.tenantId))
const { isPlatformAdmin, platformGet, ready } = usePlatformApi()
const organization = ref<OrganizationDetail | null>(null)
const history = ref<PlatformAuditPage | null>(null)
const loading = ref(false)
const loadError = ref('')

const formatDate = (value: string) =>
  new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium' }).format(new Date(value))

onMounted(async () => {
  if (!await ready()) return
  loading.value = true
  try {
    ;[organization.value, history.value] = await Promise.all([
      platformGet<OrganizationDetail>(`/organizations/${encodeURIComponent(tenantId.value)}`),
      platformGet<PlatformAuditPage>('/audit', { tenantId: tenantId.value, pageSize: 10 })
    ])
  } catch (cause) {
    const status = (cause as { status?: number })?.status
    loadError.value = status === 404 || status === 400 ? text.value.notFound : text.value.loadError
  } finally { loading.value = false }
})
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ organization?.name ?? text.details }}</h1>
    <NuxtLink to="/sys-admin/organizations">{{ text.back }}</NuxtLink>
  </section>

  <SysAdminRestricted v-if="!isPlatformAdmin" />
  <template v-else>
    <SysAdminNav />
    <p v-if="loading" role="status">{{ text.loading }}</p>
    <p v-else-if="loadError" class="notice notice-error" role="alert">{{ loadError }}</p>
    <template v-else-if="organization">
      <dl class="sysadmin-stats">
        <div class="sysadmin-stat"><dt>{{ text.activeMembers }}</dt><dd>{{ organization.activeMemberCount }}</dd></div>
        <div class="sysadmin-stat"><dt>{{ text.pendingMembers }}</dt><dd>{{ organization.pendingMemberCount }}</dd></div>
      </dl>
      <div class="sysadmin-columns">
        <section class="sysadmin-card" aria-labelledby="details-heading">
          <h2 id="details-heading">{{ text.details }}</h2>
          <dl class="sysadmin-facts">
            <dt>{{ text.code }}</dt><dd><span class="sysadmin-code">{{ organization.organizationCode }}</span></dd>
            <dt>{{ text.urlName }}</dt><dd>{{ organization.slug }}</dd>
            <dt>{{ text.status }}</dt>
            <dd>
              <span class="sysadmin-pill" :class="organization.status === 'Active' ? 'is-ok' : 'is-warn'">
                {{ organization.status === 'Active' ? text.statusActive : text.statusSuspended }}
              </span>
            </dd>
            <dt>{{ text.created }}</dt><dd>{{ formatDate(organization.createdAtUtc) }}</dd>
          </dl>
          <h3>{{ text.admins }}</h3>
          <p v-if="!organization.admins.length" class="sysadmin-empty">{{ text.noAdmins }}</p>
          <ul v-else class="sysadmin-org-list">
            <li v-for="admin in organization.admins" :key="admin.email">
              <span>{{ admin.email }}</span>
              <span class="sysadmin-pill" :class="admin.status === 'Active' ? 'is-ok' : 'is-warn'">
                {{ admin.status === 'Active' ? text.adminActive : text.adminPending }}
              </span>
            </li>
          </ul>
        </section>
        <section class="sysadmin-card" aria-labelledby="history-heading">
          <header class="sysadmin-card-heading">
            <h2 id="history-heading">{{ text.history }}</h2>
            <NuxtLink :to="{ path: '/sys-admin/audit', query: { tenantId: organization.tenantId } }">{{ text.fullHistory }}</NuxtLink>
          </header>
          <PlatformAuditList :entries="history?.items ?? []" :show-organization="false" />
        </section>
      </div>
    </template>
  </template>
</template>
