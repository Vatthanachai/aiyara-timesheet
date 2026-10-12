<script setup lang="ts">
import { useLocaleText } from '../../../../shared/i18n'
import type { PlatformAuditPage } from '../../composables/usePlatformApi'

const actions = [
  'organization.created', 'organization.updated', 'organization.suspended', 'organization.reactivated',
  'administrator.invited', 'administrator.activation_resent',
  'platform_admin.signed_in', 'platform_admin.sign_in_failed'
] as const

const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', title: 'ประวัติการดำเนินการ',
    subtitle: 'ใครทำอะไร กับองค์กรไหน เมื่อไร และด้วยเหตุผลอะไร',
    actor: 'อีเมลผู้ดำเนินการ', action: 'ประเภท', anyAction: 'ทุกประเภท', from: 'ตั้งแต่วันที่', to: 'ถึงวันที่',
    apply: 'กรอง', clear: 'ล้างตัวกรอง', organizationFilter: 'เฉพาะองค์กรนี้', results: 'พบ {n} รายการ',
    loading: 'กำลังโหลด…', loadError: 'โหลดประวัติไม่สำเร็จ กรุณาตรวจสอบตัวกรองแล้วลองใหม่',
    previous: 'ก่อนหน้า', next: 'ถัดไป', pagination: 'แบ่งหน้า', filters: 'ตัวกรองประวัติ',
    'organization.created': 'สร้างองค์กร', 'organization.updated': 'แก้ไขข้อมูลองค์กร',
    'organization.suspended': 'ระงับองค์กร', 'organization.reactivated': 'เปิดใช้งานองค์กรอีกครั้ง',
    'administrator.invited': 'เชิญผู้ดูแลองค์กร', 'administrator.activation_resent': 'ส่งรหัสเปิดใช้งานใหม่',
    'platform_admin.signed_in': 'ผู้ดูแลระบบเข้าสู่ระบบ', 'platform_admin.sign_in_failed': 'เข้าสู่ระบบผู้ดูแลระบบไม่สำเร็จ'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', title: 'Audit log',
    subtitle: 'Who did what, to which organization, when, and why.',
    actor: 'Actor email', action: 'Action', anyAction: 'Any action', from: 'From', to: 'To',
    apply: 'Filter', clear: 'Clear filters', organizationFilter: 'This organization only', results: '{n} entries',
    loading: 'Loading…', loadError: 'The audit log could not be loaded. Check the filters and try again.',
    previous: 'Previous', next: 'Next', pagination: 'Pagination', filters: 'Audit filters',
    'organization.created': 'Organization created', 'organization.updated': 'Organization details changed',
    'organization.suspended': 'Organization suspended', 'organization.reactivated': 'Organization reactivated',
    'administrator.invited': 'Administrator invited', 'administrator.activation_resent': 'Activation code re-sent',
    'platform_admin.signed_in': 'System administrator signed in', 'platform_admin.sign_in_failed': 'System administrator sign-in failed'
  }
})
const route = useRoute()
const { isPlatformAdmin, platformGet, ready } = usePlatformApi()
const pageSize = 25
// Opened from an organization page: offer to keep the log limited to that organization.
const organizationId = typeof route.query.tenantId === 'string' ? route.query.tenantId : ''
const onlyThisOrganization = ref(Boolean(organizationId))
const actor = ref('')
const action = ref('')
const from = ref('')
const to = ref('')
const page = ref(1)
const result = ref<PlatformAuditPage | null>(null)
const loading = ref(false)
const loadError = ref('')
const pageCount = computed(() => Math.max(1, Math.ceil((result.value?.totalCount ?? 0) / pageSize)))

// Date inputs are local calendar days; send the whole day as a UTC range.
const startOfDay = (day: string) => day ? new Date(`${day}T00:00:00`).toISOString() : undefined
const endOfDay = (day: string) => day ? new Date(`${day}T23:59:59.999`).toISOString() : undefined

async function load() {
  loading.value = true
  loadError.value = ''
  try {
    result.value = await platformGet<PlatformAuditPage>('/audit', {
      tenantId: onlyThisOrganization.value && organizationId ? organizationId : undefined, actor: actor.value.trim() || undefined,
      action: action.value || undefined, fromUtc: startOfDay(from.value), toUtc: endOfDay(to.value),
      page: page.value, pageSize
    })
  } catch { loadError.value = text.value.loadError }
  finally { loading.value = false }
}

function applyFilters() {
  page.value = 1
  return load()
}

function clearFilters() {
  onlyThisOrganization.value = false; actor.value = ''; action.value = ''; from.value = ''; to.value = ''
  return applyFilters()
}

function goTo(target: number) {
  page.value = target
  return load()
}

onMounted(async () => { if (await ready()) await load() })
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
    <form class="sysadmin-card sysadmin-filters" :aria-label="text.filters" @submit.prevent="applyFilters">
      <label>{{ text.actor }}<input v-model="actor" type="search" maxlength="320" /></label>
      <label>{{ text.action }}
        <select v-model="action">
          <option value="">{{ text.anyAction }}</option>
          <option v-for="item in actions" :key="item" :value="item">{{ text[item] }}</option>
        </select>
      </label>
      <label>{{ text.from }}<input v-model="from" type="date" /></label>
      <label>{{ text.to }}<input v-model="to" type="date" :min="from || undefined" /></label>
      <label v-if="organizationId" class="sysadmin-check"><input v-model="onlyThisOrganization" type="checkbox" />{{ text.organizationFilter }}</label>
      <div class="sysadmin-filter-actions">
        <button class="button" type="submit" :disabled="loading">{{ text.apply }}</button>
        <button class="button button-secondary" type="button" @click="clearFilters">{{ text.clear }}</button>
      </div>
    </form>

    <p v-if="loadError" class="notice notice-error" role="alert">{{ loadError }}</p>
    <p v-else-if="loading" role="status">{{ text.loading }}</p>
    <section v-else-if="result" class="sysadmin-card" :aria-label="text.title">
      <p class="sysadmin-muted">{{ text.results.replace('{n}', String(result.totalCount)) }}</p>
      <PlatformAuditList :entries="result.items" />
      <nav v-if="pageCount > 1" class="pagination" :aria-label="text.pagination">
        <button class="button button-secondary" type="button" :disabled="page <= 1" @click="goTo(page - 1)">{{ text.previous }}</button>
        <span>{{ page }} / {{ pageCount }}</span>
        <button class="button button-secondary" type="button" :disabled="page >= pageCount" @click="goTo(page + 1)">{{ text.next }}</button>
      </nav>
    </section>
  </template>
</template>
