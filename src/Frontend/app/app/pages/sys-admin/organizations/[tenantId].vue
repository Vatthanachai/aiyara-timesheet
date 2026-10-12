<script setup lang="ts">
import { useLocaleText } from '../../../../../shared/i18n'
import type { MembershipStatus, PlatformAdminSummary, PlatformAuditPage } from '../../../composables/usePlatformApi'

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
    adminActive: 'เปิดใช้งานแล้ว', adminPending: 'รอเปิดใช้งาน', adminRemoved: 'นำออกแล้ว', noAdmins: 'ไม่พบผู้ดูแลองค์กร',
    demote: 'ลดเป็นพนักงาน', remove: 'นำออกจากองค์กร', reason: 'เหตุผล (ไม่บังคับ)',
    confirmDemote: 'ลดสิทธิ์ {email} เป็นพนักงาน? เซสชันที่เปิดอยู่ของบัญชีนี้จะสิ้นสุด',
    confirmRemove: 'นำ {email} ออกจากองค์กรนี้? บัญชีนี้จะเข้าใช้งานองค์กรนี้ไม่ได้อีก',
    demoted: 'ลดสิทธิ์แล้ว', removed: 'นำออกแล้ว',
    LAST_ADMINISTRATOR: 'องค์กรต้องมีผู้ดูแลที่ใช้งานอยู่อย่างน้อยหนึ่งคน เชิญผู้ดูแลคนใหม่ก่อน',
    changeError: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
    history: 'ประวัติการดำเนินการ', fullHistory: 'ดูประวัติทั้งหมด', details: 'ข้อมูลองค์กร',
    loading: 'กำลังโหลด…', notFound: 'ไม่พบองค์กรนี้', loadError: 'โหลดข้อมูลองค์กรไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', back: 'Back to organizations', code: 'Organization code', urlName: 'URL name',
    status: 'Status', created: 'Created', statusActive: 'Active', statusSuspended: 'Suspended',
    activeMembers: 'Active members', pendingMembers: 'Pending activation', admins: 'Administrators',
    adminActive: 'Active', adminPending: 'Pending activation', adminRemoved: 'Removed', noAdmins: 'No administrator found.',
    demote: 'Make employee', remove: 'Remove from organization', reason: 'Reason (optional)',
    confirmDemote: 'Make {email} an employee? Their open sessions end.',
    confirmRemove: 'Remove {email} from this organization? They will no longer be able to use it.',
    demoted: 'Administrator demoted.', removed: 'Administrator removed.',
    LAST_ADMINISTRATOR: 'An organization needs at least one active administrator. Invite a new one first.',
    changeError: 'The change could not be made. Please try again.',
    history: 'History', fullHistory: 'Full history', details: 'Organization details',
    loading: 'Loading…', notFound: 'This organization was not found.', loadError: 'The organization could not be loaded. Please try again.'
  }
})
const route = useRoute()
const tenantId = computed(() => String(route.params.tenantId))
const { isPlatformAdmin, platformGet, platformSend, ready } = usePlatformApi()
const organization = ref<OrganizationDetail | null>(null)
const history = ref<PlatformAuditPage | null>(null)
const loading = ref(false)
const loadError = ref('')
const pending = ref('')
const notice = ref('')
const failure = ref('')
const reasons = reactive<Record<string, string>>({})

const formatDate = (value: string) =>
  new Intl.DateTimeFormat(locale.value === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium' }).format(new Date(value))
const adminPill = (status: MembershipStatus) =>
  status === 'Active' ? 'is-ok' : status === 'Deactivated' ? 'is-off' : 'is-warn'
const adminLabel = (status: MembershipStatus) =>
  status === 'Active' ? text.value.adminActive : status === 'Deactivated' ? text.value.adminRemoved : text.value.adminPending

async function load() {
  ;[organization.value, history.value] = await Promise.all([
    platformGet<OrganizationDetail>(`/organizations/${encodeURIComponent(tenantId.value)}`),
    platformGet<PlatformAuditPage>('/audit', { tenantId: tenantId.value, pageSize: 10 })
  ])
}

async function changeAdministrator(admin: PlatformAdminSummary, kind: 'demote' | 'remove') {
  const confirmText = kind === 'demote' ? text.value.confirmDemote : text.value.confirmRemove
  if (!window.confirm(confirmText.replace('{email}', admin.email))) return
  pending.value = admin.email
  notice.value = ''
  failure.value = ''
  try {
    await platformSend(`/organizations/${encodeURIComponent(tenantId.value)}/administrators/${kind}`, {
      method: 'POST', body: { email: admin.email, reason: reasons[admin.email]?.trim() || null }
    })
    notice.value = kind === 'demote' ? text.value.demoted : text.value.removed
    await load()
  } catch (cause) {
    const code = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    failure.value = code === 'LAST_ADMINISTRATOR' ? text.value.LAST_ADMINISTRATOR : text.value.changeError
  } finally { pending.value = '' }
}

onMounted(async () => {
  if (!await ready()) return
  loading.value = true
  try { await load() }
  catch (cause) {
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
          <p v-if="notice" class="notice" role="status">{{ notice }}</p>
          <p v-if="failure" class="notice notice-error" role="alert">{{ failure }}</p>
          <ul v-if="organization.admins.length" class="audit-list">
            <li v-for="admin in organization.admins" :key="admin.email" class="audit-row">
              <div class="audit-heading">
                <span class="sysadmin-person">{{ admin.email }}</span>
                <span class="sysadmin-pill" :class="adminPill(admin.status)">{{ adminLabel(admin.status) }}</span>
              </div>
              <div v-if="admin.status !== 'Deactivated'" class="sysadmin-row-actions">
                <input v-model="reasons[admin.email]" class="sysadmin-reason" :placeholder="text.reason" maxlength="500"
                  :aria-label="`${text.reason} — ${admin.email}`" />
                <button class="button button-secondary" type="button" :disabled="pending === admin.email"
                  @click="changeAdministrator(admin, 'demote')">{{ text.demote }}</button>
                <button class="button button-secondary" type="button" :disabled="pending === admin.email"
                  @click="changeAdministrator(admin, 'remove')">{{ text.remove }}</button>
              </div>
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
