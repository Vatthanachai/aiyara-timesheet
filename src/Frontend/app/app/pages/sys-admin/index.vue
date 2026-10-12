<script setup lang="ts">
import { useLocaleText } from '../../../../shared/i18n'
import type { PlatformAuditEntry, PlatformOrganizationSummary } from '../../composables/usePlatformApi'

type Overview = {
  organizationCount: number
  activeCount: number
  suspendedCount: number
  awaitingAdministratorCount: number
  newestOrganizations: PlatformOrganizationSummary[]
  recentActivity: PlatformAuditEntry[]
}

const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', title: 'ภาพรวมระบบ', subtitle: 'สถานะองค์กรทั้งหมดและการดำเนินการล่าสุดของผู้ดูแลระบบ',
    organizations: 'องค์กรทั้งหมด', active: 'ใช้งานอยู่', suspended: 'ถูกระงับ',
    awaiting: 'ยังไม่มีผู้ดูแลที่เปิดใช้งาน', newest: 'องค์กรล่าสุด', activity: 'การดำเนินการล่าสุด',
    viewAll: 'ดูทั้งหมด', loading: 'กำลังโหลด…', loadError: 'โหลดภาพรวมไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
    noOrganizations: 'ยังไม่มีองค์กร'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', title: 'Overview', subtitle: 'Every organization at a glance, and the latest administrative actions.',
    organizations: 'Organizations', active: 'Active', suspended: 'Suspended',
    awaiting: 'Without an active administrator', newest: 'Newest organizations', activity: 'Recent activity',
    viewAll: 'View all', loading: 'Loading…', loadError: 'The overview could not be loaded. Please try again.',
    noOrganizations: 'No organizations yet.'
  }
})
const { isPlatformAdmin, platformGet, ready } = usePlatformApi()
const overview = ref<Overview | null>(null)
const loading = ref(false)
const loadError = ref('')

const stats = computed(() => overview.value ? [
  { label: text.value.organizations, value: overview.value.organizationCount },
  { label: text.value.active, value: overview.value.activeCount },
  { label: text.value.suspended, value: overview.value.suspendedCount },
  { label: text.value.awaiting, value: overview.value.awaitingAdministratorCount }
] : [])

onMounted(async () => {
  if (!await ready()) return
  loading.value = true
  try { overview.value = await platformGet<Overview>('/overview') }
  catch { loadError.value = text.value.loadError }
  finally { loading.value = false }
})
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <SysAdminRestricted v-if="!isPlatformAdmin" />
  <template v-else>
    <SysAdminNav />
    <p v-if="loading" role="status">{{ text.loading }}</p>
    <p v-else-if="loadError" class="notice notice-error" role="alert">{{ text.loadError }}</p>
    <template v-else-if="overview">
      <dl class="sysadmin-stats">
        <div v-for="stat in stats" :key="stat.label" class="sysadmin-stat">
          <dt>{{ stat.label }}</dt>
          <dd>{{ stat.value }}</dd>
        </div>
      </dl>
      <div class="sysadmin-columns">
        <section class="sysadmin-card" aria-labelledby="newest-heading">
          <header class="sysadmin-card-heading">
            <h2 id="newest-heading">{{ text.newest }}</h2>
            <NuxtLink to="/sys-admin/organizations">{{ text.viewAll }}</NuxtLink>
          </header>
          <p v-if="!overview.newestOrganizations.length" class="sysadmin-empty">{{ text.noOrganizations }}</p>
          <ul v-else class="sysadmin-org-list">
            <li v-for="organization in overview.newestOrganizations" :key="organization.tenantId">
              <NuxtLink :to="`/sys-admin/organizations/${organization.tenantId}`">{{ organization.name }}</NuxtLink>
              <span class="sysadmin-code">{{ organization.organizationCode }}</span>
            </li>
          </ul>
        </section>
        <section class="sysadmin-card" aria-labelledby="activity-heading">
          <header class="sysadmin-card-heading">
            <h2 id="activity-heading">{{ text.activity }}</h2>
            <NuxtLink to="/sys-admin/audit">{{ text.viewAll }}</NuxtLink>
          </header>
          <PlatformAuditList :entries="overview.recentActivity" />
        </section>
      </div>
    </template>
  </template>
</template>
