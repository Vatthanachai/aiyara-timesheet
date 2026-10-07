<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../../shared/api-response'
type OrganizationAdmin = { email: string; status: 'Active' | 'PendingActivation' }
type Organization = {
  tenantId: string
  organizationCode: string
  name: string
  slug: string
  createdAtUtc: string
  admins: OrganizationAdmin[]
}
type OrganizationResponse = {
  totalCount: number
  page: number
  pageSize: number
  items: Organization[]
}

const { locale } = useLocale()
const { token, role, renew, restore } = useSession()
const config = useRuntimeConfig()
const organizations = ref<Organization[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = 25
const searchInput = ref('')
const search = ref('')
const loading = ref(false)
const loadError = ref('')
const showCreate = ref(false)
const createPending = ref(false)
const createError = ref('')
const createSuccess = ref('')
const resending = ref('')
const resendMessages = ref<Record<string, string>>({})
const organizationName = ref('')
const organizationSlug = ref('')
const adminEmail = ref('')

const isPlatformAdmin = computed(() => Boolean(token.value) && role.value === 'PlatformAdmin')
const pageCount = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

async function loadOrganizations() {
  if (!isPlatformAdmin.value) return
  loading.value = true
  loadError.value = ''
  try {
    const response = await $fetch<OrganizationResponse>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations`, {
        headers: { Authorization: `Bearer ${token.value}` },
        query: { search: search.value || undefined, page: page.value, pageSize }
      })
    organizations.value = response.items
    totalCount.value = response.totalCount
  } catch {
    loadError.value = locale.value === 'th'
      ? 'โหลดรายการองค์กรไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'
      : 'Could not load organizations. Please try again.'
  } finally {
    loading.value = false
  }
}

function applySearch() {
  page.value = 1
  void loadOrganizations()
}

async function createOrganization() {
  createError.value = ''
  createSuccess.value = ''
  createPending.value = true
  try {
    const created = unwrapResponse(await $fetch<ApiEnvelope<{ organizationCode: string }>>(
      `${config.public.gatewayUrl}/api/v1/tenants`, {
        method: 'POST',
        body: {
          name: organizationName.value.trim(),
          slug: organizationSlug.value.trim().toLowerCase(),
          adminEmail: adminEmail.value.trim()
        }
      }))
    createSuccess.value = locale.value === 'th'
      ? `สร้างองค์กรแล้ว รหัสองค์กร ${created.organizationCode} ระบบกำลังส่งอีเมลเปิดใช้งานให้ admin`
      : `Organization created. Code: ${created.organizationCode}. An activation email is being sent to the administrator.`
    organizationName.value = ''
    organizationSlug.value = ''
    adminEmail.value = ''
    showCreate.value = false
    page.value = 1
    search.value = ''
    searchInput.value = ''
    await loadOrganizations()
  } catch (cause: unknown) {
    const status = (cause as { statusCode?: number; status?: number })?.statusCode
      ?? (cause as { status?: number })?.status
    createError.value = status === 409
      ? (locale.value === 'th' ? 'ชื่อ URL องค์กรนี้ถูกใช้แล้ว' : 'That organization URL is already in use.')
      : (locale.value === 'th' ? 'สร้างองค์กรไม่สำเร็จ กรุณาตรวจสอบข้อมูล' : 'Could not create the organization. Check the details and try again.')
  } finally {
    createPending.value = false
  }
}

async function resendActivation(organization: Organization, admin: OrganizationAdmin) {
  const key = `${organization.tenantId}:${admin.email}`
  resending.value = key
  resendMessages.value = { ...resendMessages.value, [key]: '' }
  try {
    await $fetch(`${config.public.gatewayUrl}/api/v1/auth/activation/request`, {
      method: 'POST',
      body: { tenantId: organization.tenantId, email: admin.email }
    })
    resendMessages.value = {
      ...resendMessages.value,
      [key]: locale.value === 'th'
        ? 'ระบบรับคำขอแล้ว หากบัญชียังรอเปิดใช้งานจะส่งรหัสใหม่ให้'
        : 'Request accepted. If the account is pending activation, a new code will be sent.'
    }
  } catch {
    resendMessages.value = {
      ...resendMessages.value,
      [key]: locale.value === 'th' ? 'ส่งคำขอไม่สำเร็จ กรุณาลองอีกครั้งภายหลัง' : 'Request failed. Please try again later.'
    }
  } finally {
    resending.value = ''
  }
}

onMounted(async () => {
  restore()
  await nextTick()
  if (!await renew()) return
  if (!isPlatformAdmin.value) return
  await loadOrganizations()
})
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ locale === 'th' ? 'ผู้ดูแลระบบ' : 'SYSTEM ADMINISTRATION' }}</p>
    <h1>{{ locale === 'th' ? 'จัดการองค์กร' : 'Organizations' }}</h1>
    <p>{{ locale === 'th'
      ? 'ค้นหาองค์กร ตรวจสอบสถานะผู้ดูแล และจัดการการเริ่มต้นใช้งาน'
      : 'Find organizations, review administrator activation, and manage onboarding.' }}</p>
  </section>

  <section v-if="!isPlatformAdmin" class="management-card" role="alert">
    <h2>{{ locale === 'th' ? 'ไม่มีสิทธิ์เข้าถึง' : 'Access restricted' }}</h2>
    <p>{{ locale === 'th'
      ? 'หน้านี้สำหรับผู้ดูแลระบบเท่านั้น กรุณาเข้าสู่ระบบด้วยบัญชี sys-admin'
      : 'This page is limited to system administrators. Sign in with a sys-admin account.' }}</p>
    <NuxtLink class="button" to="/">{{ locale === 'th' ? 'กลับหน้าเข้าสู่ระบบ' : 'Back to sign in' }}</NuxtLink>
  </section>

  <template v-else>
    <section class="management-toolbar">
      <form class="search-form" role="search" @submit.prevent="applySearch">
        <label class="visually-hidden" for="organization-search">
          {{ locale === 'th' ? 'ค้นหาองค์กร' : 'Search organizations' }}
        </label>
        <input id="organization-search" v-model="searchInput"
          :placeholder="locale === 'th' ? 'ค้นหาชื่อ รหัสองค์กร หรืออีเมล admin' : 'Search name, organization code, or admin email'" />
        <button class="button button-secondary" type="submit">
          {{ locale === 'th' ? 'ค้นหา' : 'Search' }}
        </button>
      </form>
      <button class="button" type="button" @click="showCreate = !showCreate">
        {{ showCreate
          ? (locale === 'th' ? 'ปิดแบบฟอร์ม' : 'Close form')
          : (locale === 'th' ? 'เพิ่มองค์กร' : 'Add organization') }}
      </button>
    </section>

    <p class="result-count">{{ locale === 'th' ? `พบ ${totalCount} องค์กร` : `${totalCount} organizations` }}</p>
    <p v-if="createSuccess" class="notice" role="status">{{ createSuccess }}</p>

    <form v-if="showCreate" class="management-card create-form" @submit.prevent="createOrganization">
      <h2>{{ locale === 'th' ? 'สร้างองค์กรใหม่' : 'Create organization' }}</h2>
      <label>
        {{ locale === 'th' ? 'ชื่อองค์กร' : 'Organization name' }}
        <input v-model="organizationName" required maxlength="200" />
      </label>
      <label>
        {{ locale === 'th' ? 'ชื่อ URL องค์กร' : 'Organization URL name' }}
        <input v-model="organizationSlug" required minlength="3" maxlength="80"
          pattern="[a-z0-9][a-z0-9-]{2,79}" autocapitalize="none" placeholder="acme-studio" />
      </label>
      <label>
        {{ locale === 'th' ? 'อีเมลผู้ดูแลองค์กร' : 'Organization administrator email' }}
        <input v-model="adminEmail" required type="email" autocomplete="email" />
      </label>
      <p v-if="createError" role="alert">{{ createError }}</p>
      <button class="button" type="submit" :disabled="createPending">
        {{ createPending
          ? (locale === 'th' ? 'กำลังสร้าง…' : 'Creating…')
          : (locale === 'th' ? 'สร้างองค์กรและส่งอีเมลเปิดใช้งาน' : 'Create organization and send activation email') }}
      </button>
    </form>

    <p v-if="loadError" role="alert" class="notice notice-error">{{ loadError }}</p>
    <p v-else-if="loading" class="notice" role="status">
      {{ locale === 'th' ? 'กำลังโหลดองค์กร…' : 'Loading organizations…' }}
    </p>
    <div v-else-if="organizations.length === 0" class="management-card empty-state">
      <h2>{{ locale === 'th' ? 'ยังไม่พบองค์กร' : 'No organizations found' }}</h2>
      <p>{{ locale === 'th' ? 'ลองเปลี่ยนคำค้นหา หรือสร้างองค์กรใหม่' : 'Try another search or create a new organization.' }}</p>
    </div>
    <section v-else class="organization-list" aria-label="Organizations">
      <article v-for="organization in organizations" :key="organization.tenantId" class="organization-card">
        <header class="organization-heading">
          <div>
            <h2>{{ organization.name }}</h2>
            <p>{{ organization.slug }}</p>
          </div>
          <span class="organization-code">{{ organization.organizationCode }}</span>
        </header>
        <p class="created-date">
          {{ locale === 'th' ? 'สร้างเมื่อ' : 'Created' }}
          {{ new Intl.DateTimeFormat(locale === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium' }).format(new Date(organization.createdAtUtc)) }}
        </p>
        <div v-if="organization.admins.length" class="admin-list">
          <div v-for="admin in organization.admins" :key="admin.email" class="admin-row">
            <span>{{ admin.email }}</span>
            <span class="status-pill" :class="admin.status === 'Active' ? 'status-active' : 'status-pending'">
              {{ admin.status === 'Active'
                ? (locale === 'th' ? 'เปิดใช้งานแล้ว' : 'Active')
                : (locale === 'th' ? 'รอเปิดใช้งาน' : 'Pending activation') }}
            </span>
            <button v-if="admin.status === 'PendingActivation'" class="text-button" type="button"
              :disabled="resending === `${organization.tenantId}:${admin.email}`"
              @click="resendActivation(organization, admin)">
              {{ resending === `${organization.tenantId}:${admin.email}`
                ? (locale === 'th' ? 'กำลังส่ง…' : 'Sending…')
                : (locale === 'th' ? 'ส่งรหัสใหม่' : 'Resend code') }}
            </button>
            <small v-if="resendMessages[`${organization.tenantId}:${admin.email}`]">
              {{ resendMessages[`${organization.tenantId}:${admin.email}`] }}
            </small>
          </div>
        </div>
        <p v-else class="empty-admin">{{ locale === 'th' ? 'ไม่พบผู้ดูแลองค์กร' : 'No organization administrator found' }}</p>
      </article>
    </section>

    <nav v-if="pageCount > 1" class="pagination" :aria-label="locale === 'th' ? 'แบ่งหน้า' : 'Pagination'">
      <button class="button button-secondary" type="button" :disabled="page <= 1" @click="page--; loadOrganizations()">
        {{ locale === 'th' ? 'ก่อนหน้า' : 'Previous' }}
      </button>
      <span>{{ page }} / {{ pageCount }}</span>
      <button class="button button-secondary" type="button" :disabled="page >= pageCount" @click="page++; loadOrganizations()">
        {{ locale === 'th' ? 'ถัดไป' : 'Next' }}
      </button>
    </nav>
  </template>
</template>

<style scoped>
.management-toolbar { align-items: center; display: flex; flex-wrap: wrap; gap: 1rem; justify-content: space-between; margin: 2rem 0 0; }
.search-form { display: flex; flex: 1 1 30rem; gap: .6rem; }
.search-form input, .create-form input { border: 1px solid var(--color-border); border-radius: .45rem; font: inherit; min-width: 0; padding: .7rem .8rem; }
.search-form input { flex: 1; }
.button-secondary { background: #edf2f8; border: 1px solid var(--color-border); color: var(--color-ink); cursor: pointer; font: inherit; }
.result-count { font-size: .9rem; margin: .75rem 0; }
.management-card, .organization-card { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-card); padding: var(--space-6); }
.create-form { display: grid; gap: .85rem; margin: 1rem 0 2rem; max-width: 42rem; }
.create-form label { display: grid; gap: .35rem; }
.organization-list { display: grid; gap: 1rem; }
.organization-card { display: grid; gap: .65rem; }
.organization-heading { align-items: flex-start; display: flex; gap: 1rem; justify-content: space-between; }
.organization-heading h2 { margin: 0; }
.organization-heading p, .created-date, .empty-admin { margin: .25rem 0 0; }
.organization-code { background: #edf3ff; border-radius: .4rem; color: var(--color-brand); font-weight: 700; letter-spacing: .08em; padding: .45rem .65rem; white-space: nowrap; }
.admin-list { border-top: 1px solid var(--color-border); display: grid; margin-top: .5rem; padding-top: .5rem; }
.admin-row { align-items: center; display: flex; flex-wrap: wrap; gap: .65rem; min-height: 2.5rem; }
.status-pill { border-radius: 999px; font-size: .78rem; font-weight: 700; padding: .25rem .55rem; }
.status-active { background: #e8f6ef; color: #17663b; }
.status-pending { background: #fff4dc; color: #795400; }
.text-button { background: transparent; border: 0; color: var(--color-brand); cursor: pointer; font: inherit; text-decoration: underline; }
.admin-row small { color: var(--color-muted); flex-basis: 100%; }
.empty-state { margin-top: 1rem; }
.notice-error { background: #fff0f0; color: #8a2424; }
.pagination { align-items: center; display: flex; gap: 1rem; justify-content: center; margin: 1.5rem 0; }
.visually-hidden { clip: rect(0, 0, 0, 0); clip-path: inset(50%); height: 1px; overflow: hidden; position: absolute; white-space: nowrap; width: 1px; }
@media (max-width: 40rem) { .management-toolbar { align-items: stretch; } .search-form { flex-basis: 100%; } }
</style>
