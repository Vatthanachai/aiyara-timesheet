<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../../../shared/api-response'
import { useLocaleText } from '../../../../../shared/i18n'
type OrganizationAdmin = { email: string; status: 'Active' | 'PendingActivation' | 'Deactivated' }
type Organization = {
  tenantId: string
  organizationCode: string
  name: string
  slug: string
  status: 'Active' | 'Suspended'
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
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', title: 'จัดการองค์กร',
    subtitle: 'ค้นหาองค์กร ตรวจสอบสถานะผู้ดูแล และจัดการการเริ่มต้นใช้งาน',
    accessRestrictedTitle: 'ไม่มีสิทธิ์เข้าถึง',
    accessRestrictedBody: 'หน้านี้สำหรับผู้ดูแลระบบเท่านั้น กรุณาเข้าสู่ระบบด้วยบัญชี sys-admin',
    backToSignIn: 'กลับหน้าเข้าสู่ระบบ', searchLabel: 'ค้นหาองค์กร',
    searchPlaceholder: 'ค้นหาชื่อ รหัสองค์กร หรืออีเมล admin', search: 'ค้นหา',
    closeForm: 'ปิดแบบฟอร์ม', addOrganization: 'เพิ่มองค์กร',
    resultCount: 'พบ {n} องค์กร', createTitle: 'สร้างองค์กรใหม่',
    organizationName: 'ชื่อองค์กร', organizationUrlName: 'ชื่อ URL องค์กร',
    organizationAdminEmail: 'อีเมลผู้ดูแลองค์กร', creating: 'กำลังสร้าง…',
    createAndSend: 'สร้างองค์กรและส่งอีเมลเปิดใช้งาน', loadingOrgs: 'กำลังโหลดองค์กร…',
    noOrgsTitle: 'ยังไม่พบองค์กร', noOrgsBody: 'ลองเปลี่ยนคำค้นหา หรือสร้างองค์กรใหม่',
    statusActive: 'ใช้งาน', statusSuspended: 'ระงับ', save: 'บันทึก', cancel: 'ยกเลิก',
    editOrganization: 'แก้ไของค์กร', reasonForChange: 'เหตุผลการเปลี่ยนแปลง',
    suspendOrganization: 'ระงับองค์กร', reactivateOrganization: 'เปิดใช้งานองค์กร',
    created: 'สร้างเมื่อ', adminActive: 'เปิดใช้งานแล้ว', adminPending: 'รอเปิดใช้งาน', adminRemoved: 'นำออกแล้ว',
    sending: 'กำลังส่ง…', resendCode: 'ส่งรหัสใหม่', noAdminFound: 'ไม่พบผู้ดูแลองค์กร',
    inviteAnother: 'เชิญผู้ดูแลเพิ่มเติม', sendInvitation: 'ส่งคำเชิญ', pagination: 'แบ่งหน้า',
    previous: 'ก่อนหน้า', next: 'ถัดไป',
    confirmSuspend: 'ระงับองค์กรนี้หรือไม่? ผู้ใช้ในองค์กรจะเข้าใช้งานไม่ได้',
    confirmReactivate: 'เปิดใช้งานองค์กรนี้อีกครั้งหรือไม่?',
    loadError: 'โหลดรายการองค์กรไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
    orgSaved: 'บันทึกข้อมูลองค์กรแล้ว',
    orgSaveError: 'บันทึกไม่สำเร็จ กรุณาตรวจสอบชื่อ URL และลองอีกครั้ง',
    statusUpdated: 'เปลี่ยนสถานะองค์กรแล้ว', statusUpdateError: 'เปลี่ยนสถานะไม่สำเร็จ กรุณาลองอีกครั้ง',
    inviteSent: 'ส่งคำเชิญผู้ดูแลองค์กรแล้ว',
    inviteError: 'ส่งคำเชิญไม่สำเร็จ กรุณาตรวจสอบอีเมลและสถานะองค์กร',
    createdTemplate: 'สร้างองค์กรแล้ว รหัสองค์กร {code} ระบบกำลังส่งอีเมลเปิดใช้งานให้ admin',
    createSlugConflict: 'ชื่อ URL องค์กรนี้ถูกใช้แล้ว หรืออีเมลนี้เป็นบัญชีผู้ดูแลระบบซึ่งเป็นผู้ดูแลองค์กรไม่ได้',
    createError: 'สร้างองค์กรไม่สำเร็จ กรุณาตรวจสอบข้อมูล',
    resendAccepted: 'ระบบรับคำขอแล้ว หากบัญชียังรอเปิดใช้งานจะส่งรหัสใหม่ให้',
    resendError: 'ส่งคำขอไม่สำเร็จ กรุณาลองอีกครั้งภายหลัง'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', title: 'Organizations',
    subtitle: 'Find organizations, review administrator activation, and manage onboarding.',
    accessRestrictedTitle: 'Access restricted',
    accessRestrictedBody: 'This page is limited to system administrators. Sign in with a sys-admin account.',
    backToSignIn: 'Back to sign in', searchLabel: 'Search organizations',
    searchPlaceholder: 'Search name, organization code, or admin email', search: 'Search',
    closeForm: 'Close form', addOrganization: 'Add organization',
    resultCount: '{n} organizations', createTitle: 'Create organization',
    organizationName: 'Organization name', organizationUrlName: 'Organization URL name',
    organizationAdminEmail: 'Organization administrator email', creating: 'Creating…',
    createAndSend: 'Create organization and send activation email', loadingOrgs: 'Loading organizations…',
    noOrgsTitle: 'No organizations found', noOrgsBody: 'Try another search or create a new organization.',
    statusActive: 'Active', statusSuspended: 'Suspended', save: 'Save', cancel: 'Cancel',
    editOrganization: 'Edit organization', reasonForChange: 'Reason for change',
    suspendOrganization: 'Suspend organization', reactivateOrganization: 'Reactivate organization',
    created: 'Created', adminActive: 'Active', adminPending: 'Pending activation', adminRemoved: 'Removed',
    sending: 'Sending…', resendCode: 'Resend code', noAdminFound: 'No organization administrator found',
    inviteAnother: 'Invite another administrator', sendInvitation: 'Send invitation', pagination: 'Pagination',
    previous: 'Previous', next: 'Next',
    confirmSuspend: 'Suspend this organization? Its users will lose access.',
    confirmReactivate: 'Reactivate this organization?',
    loadError: 'Could not load organizations. Please try again.',
    orgSaved: 'Organization details saved.',
    orgSaveError: 'Could not save. Check the organization URL and try again.',
    statusUpdated: 'Organization status updated.', statusUpdateError: 'Could not update status. Please try again.',
    inviteSent: 'Administrator invitation sent.',
    inviteError: 'Could not send invitation. Check the email and organization status.',
    createdTemplate: 'Organization created. Code: {code}. An activation email is being sent to the administrator.',
    createSlugConflict: 'This organization URL name is taken, or the email belongs to a system administrator, who cannot administer an organization.',
    createError: 'Could not create the organization. Check the details and try again.',
    resendAccepted: 'Request accepted. If the account is pending activation, a new code will be sent.',
    resendError: 'Request failed. Please try again later.'
  }
})
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
const editingId = ref('')
const editName = ref('')
const editSlug = ref('')
const organizationReasons = reactive<Record<string, string>>({})
const inviteEmails = reactive<Record<string, string>>({})
const managementMessages = reactive<Record<string, string>>({})
const managementPending = ref('')

const isPlatformAdmin = computed(() => Boolean(token.value) && role.value === 'PlatformAdmin')
const pageCount = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

async function loadOrganizations() {
  if (!isPlatformAdmin.value) return
  loading.value = true
  loadError.value = ''
  try {
    const response = unwrapResponse(await authorizedFetch<ApiEnvelope<OrganizationResponse>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations`, {
        query: { search: search.value || undefined, page: page.value, pageSize }
      }))
    organizations.value = response.items
    totalCount.value = response.totalCount
  } catch {
    loadError.value = text.value.loadError
  } finally {
    loading.value = false
  }
}

async function authorizedFetch<T>(url: string, options: Record<string, unknown> = {}): Promise<T> {
  const send = () => $fetch<T>(url, {
    ...options,
    headers: { ...(options.headers as Record<string, string> | undefined), Authorization: `Bearer ${token.value}` }
  })
  try { return await send() }
  catch (cause) {
    const status = (cause as { status?: number })?.status
    if (status !== 401 || !(await renew(true))) throw cause
    return await send()
  }
}

function beginEdit(organization: Organization) {
  editingId.value = organization.tenantId
  editName.value = organization.name
  editSlug.value = organization.slug
}

async function updateOrganization(organization: Organization) {
  const key = organization.tenantId
  managementPending.value = key
  managementMessages[key] = ''
  try {
    unwrapResponse(await authorizedFetch<ApiEnvelope<unknown>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations/${key}`, {
        method: 'PUT', body: { name: editName.value.trim(), slug: editSlug.value.trim().toLowerCase(), reason: organizationReasons[key] || null }
      }))
    editingId.value = ''
    managementMessages[key] = text.value.orgSaved
    await loadOrganizations()
  } catch {
    managementMessages[key] = text.value.orgSaveError
  } finally { managementPending.value = '' }
}

async function setOrganizationStatus(organization: Organization) {
  const key = organization.tenantId
  const action = organization.status === 'Active' ? text.value.confirmSuspend : text.value.confirmReactivate
  if (!window.confirm(action)) return
  managementPending.value = key
  managementMessages[key] = ''
  try {
    unwrapResponse(await authorizedFetch<ApiEnvelope<unknown>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations/${key}/status`, {
        method: 'PUT', body: { suspended: organization.status === 'Active', reason: organizationReasons[key] || null }
      }))
    managementMessages[key] = text.value.statusUpdated
    await loadOrganizations()
  } catch {
    managementMessages[key] = text.value.statusUpdateError
  } finally { managementPending.value = '' }
}

async function inviteAdministrator(organization: Organization) {
  const key = organization.tenantId
  managementPending.value = key
  managementMessages[key] = ''
  try {
    unwrapResponse(await authorizedFetch<ApiEnvelope<unknown>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations/${key}/administrators`, {
        method: 'POST', body: { email: inviteEmails[key]?.trim() }
      }))
    inviteEmails[key] = ''
    managementMessages[key] = text.value.inviteSent
    await loadOrganizations()
  } catch {
    managementMessages[key] = text.value.inviteError
  } finally { managementPending.value = '' }
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
    const created = unwrapResponse(await authorizedFetch<ApiEnvelope<{ organizationCode: string }>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations`, {
        method: 'POST',
        body: {
          name: organizationName.value.trim(),
          slug: organizationSlug.value.trim().toLowerCase(),
          adminEmail: adminEmail.value.trim()
        }
      }))
    createSuccess.value = text.value.createdTemplate.replace('{code}', created.organizationCode)
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
    createError.value = status === 409 ? text.value.createSlugConflict : text.value.createError
  } finally {
    createPending.value = false
  }
}

async function resendActivation(organization: Organization, admin: OrganizationAdmin) {
  const key = `${organization.tenantId}:${admin.email}`
  resending.value = key
  resendMessages.value = { ...resendMessages.value, [key]: '' }
  try {
    unwrapResponse(await authorizedFetch<ApiEnvelope<unknown>>(
      `${config.public.gatewayUrl}/api/v1/platform/organizations/${organization.tenantId}/administrators/activation`, {
      method: 'POST',
      body: { email: admin.email }
    }))
    resendMessages.value = { ...resendMessages.value, [key]: text.value.resendAccepted }
  } catch {
    resendMessages.value = { ...resendMessages.value, [key]: text.value.resendError }
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
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <section v-if="!isPlatformAdmin" class="management-card" role="alert">
    <h2>{{ text.accessRestrictedTitle }}</h2>
    <p>{{ text.accessRestrictedBody }}</p>
    <NuxtLink class="button" to="/sys-admin/login">{{ text.backToSignIn }}</NuxtLink>
  </section>

  <template v-else>
    <SysAdminNav />
    <section class="management-toolbar">
      <form class="search-form" role="search" @submit.prevent="applySearch">
        <label class="visually-hidden" for="organization-search">
          {{ text.searchLabel }}
        </label>
        <input id="organization-search" v-model="searchInput" :placeholder="text.searchPlaceholder" />
        <button class="button button-secondary" type="submit">
          {{ text.search }}
        </button>
      </form>
      <button class="button" type="button" @click="showCreate = !showCreate">
        {{ showCreate ? text.closeForm : text.addOrganization }}
      </button>
    </section>

    <p class="result-count">{{ text.resultCount.replace('{n}', String(totalCount)) }}</p>
    <p v-if="createSuccess" class="notice" role="status">{{ createSuccess }}</p>

    <form v-if="showCreate" class="management-card create-form" @submit.prevent="createOrganization">
      <h2>{{ text.createTitle }}</h2>
      <label>
        {{ text.organizationName }}
        <input v-model="organizationName" required maxlength="200" />
      </label>
      <label>
        {{ text.organizationUrlName }}
        <input v-model="organizationSlug" required minlength="3" maxlength="80"
          pattern="[a-z0-9][a-z0-9-]{2,79}" autocapitalize="none" placeholder="acme-studio" />
      </label>
      <label>
        {{ text.organizationAdminEmail }}
        <input v-model="adminEmail" required type="email" autocomplete="email" />
      </label>
      <p v-if="createError" role="alert">{{ createError }}</p>
      <button class="button" type="submit" :disabled="createPending">
        {{ createPending ? text.creating : text.createAndSend }}
      </button>
    </form>

    <p v-if="loadError" role="alert" class="notice notice-error">{{ loadError }}</p>
    <p v-else-if="loading" class="notice" role="status">
      {{ text.loadingOrgs }}
    </p>
    <div v-else-if="organizations.length === 0" class="management-card empty-state">
      <h2>{{ text.noOrgsTitle }}</h2>
      <p>{{ text.noOrgsBody }}</p>
    </div>
    <section v-else class="organization-list" aria-label="Organizations">
      <article v-for="organization in organizations" :key="organization.tenantId" class="organization-card">
        <header class="organization-heading">
          <div>
            <h2><NuxtLink :to="`/sys-admin/organizations/${organization.tenantId}`">{{ organization.name }}</NuxtLink></h2>
            <p>{{ organization.slug }}</p>
          </div>
          <div class="organization-badges">
            <span class="status-pill" :class="organization.status === 'Active' ? 'status-active' : 'status-pending'">
              {{ organization.status === 'Active' ? text.statusActive : text.statusSuspended }}
            </span>
            <span class="organization-code">{{ organization.organizationCode }}</span>
          </div>
        </header>
        <form v-if="editingId === organization.tenantId" class="organization-edit" @submit.prevent="updateOrganization(organization)">
          <label>{{ text.organizationName }}<input v-model="editName" required maxlength="200" /></label>
          <label>{{ text.organizationUrlName }}<input v-model="editSlug" required minlength="3" maxlength="80" pattern="[a-z0-9][a-z0-9-]{2,79}" /></label>
          <button class="button" type="submit" :disabled="managementPending === organization.tenantId">{{ text.save }}</button>
          <button class="button button-secondary" type="button" @click="editingId = ''">{{ text.cancel }}</button>
        </form>
        <div class="organization-actions">
          <button v-if="editingId !== organization.tenantId" class="text-button" type="button" @click="beginEdit(organization)">{{ text.editOrganization }}</button>
          <label class="reason-field">{{ text.reasonForChange }}<input v-model="organizationReasons[organization.tenantId]" maxlength="500" /></label>
          <button class="button button-secondary" type="button" :disabled="managementPending === organization.tenantId" @click="setOrganizationStatus(organization)">
            {{ organization.status === 'Active' ? text.suspendOrganization : text.reactivateOrganization }}
          </button>
        </div>
        <p class="created-date">
          {{ text.created }}
          {{ new Intl.DateTimeFormat(locale === 'th' ? 'th-TH' : 'en', { dateStyle: 'medium' }).format(new Date(organization.createdAtUtc)) }}
        </p>
        <div v-if="organization.admins.length" class="admin-list">
          <div v-for="admin in organization.admins" :key="admin.email" class="admin-row">
            <span>{{ admin.email }}</span>
            <span class="status-pill" :class="admin.status === 'Active' ? 'status-active' : admin.status === 'Deactivated' ? 'status-off' : 'status-pending'">
              {{ admin.status === 'Active' ? text.adminActive : admin.status === 'Deactivated' ? text.adminRemoved : text.adminPending }}
            </span>
            <button v-if="admin.status === 'PendingActivation'" class="text-button" type="button"
              :disabled="resending === `${organization.tenantId}:${admin.email}`"
              @click="resendActivation(organization, admin)">
              {{ resending === `${organization.tenantId}:${admin.email}` ? text.sending : text.resendCode }}
            </button>
            <small v-if="resendMessages[`${organization.tenantId}:${admin.email}`]">
              {{ resendMessages[`${organization.tenantId}:${admin.email}`] }}
            </small>
          </div>
        </div>
        <p v-else class="empty-admin">{{ text.noAdminFound }}</p>
        <form class="invite-form" @submit.prevent="inviteAdministrator(organization)">
          <label>{{ text.inviteAnother }}<input v-model="inviteEmails[organization.tenantId]" type="email" required autocomplete="email" /></label>
          <button class="button button-secondary" type="submit" :disabled="managementPending === organization.tenantId">{{ text.sendInvitation }}</button>
        </form>
        <p v-if="managementMessages[organization.tenantId]" class="notice" role="status">{{ managementMessages[organization.tenantId] }}</p>
      </article>
    </section>

    <nav v-if="pageCount > 1" class="pagination" :aria-label="text.pagination">
      <button class="button button-secondary" type="button" :disabled="page <= 1" @click="page--; loadOrganizations()">
        {{ text.previous }}
      </button>
      <span>{{ page }} / {{ pageCount }}</span>
      <button class="button button-secondary" type="button" :disabled="page >= pageCount" @click="page++; loadOrganizations()">
        {{ text.next }}
      </button>
    </nav>
  </template>
</template>

<style scoped>
.management-toolbar { align-items: center; display: flex; flex-wrap: wrap; gap: 1rem; justify-content: space-between; margin: 2rem 0 0; }
.search-form { display: flex; flex: 1 1 30rem; gap: .6rem; }
.search-form input, .create-form input { border: 1px solid var(--color-border); border-radius: .45rem; font: inherit; min-width: 0; padding: .7rem .8rem; }
.search-form input { flex: 1; }
.button-secondary { background: var(--color-surface-muted); border: 1px solid var(--color-border); color: var(--color-ink); cursor: pointer; font: inherit; }
.result-count { font-size: .9rem; margin: .75rem 0; }
.management-card, .organization-card { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-card); padding: var(--space-6); }
.create-form, .organization-edit { display: grid; gap: .85rem; margin: 1rem 0; }
.create-form { max-width: 42rem; }
.create-form label, .organization-edit label, .reason-field, .invite-form label { display: grid; gap: .35rem; }
.organization-edit input, .reason-field input, .invite-form input { border: 1px solid var(--color-border); border-radius: .45rem; font: inherit; min-width: 0; padding: .6rem .7rem; }
.organization-badges, .organization-actions, .invite-form { align-items: center; display: flex; flex-wrap: wrap; gap: .65rem; }
.organization-actions, .invite-form { border-top: 1px solid var(--color-border); padding-top: .75rem; }
.reason-field { flex: 1 1 15rem; }
.organization-list { display: grid; gap: 1rem; }
.organization-card { display: grid; gap: .65rem; }
.organization-heading { align-items: flex-start; display: flex; gap: 1rem; justify-content: space-between; }
.organization-heading h2 { margin: 0; }
.organization-heading p, .created-date, .empty-admin { margin: .25rem 0 0; }
.organization-code { background: var(--color-brand-soft); border-radius: .4rem; color: var(--color-brand); font-weight: 700; letter-spacing: .08em; padding: .45rem .65rem; white-space: nowrap; }
.admin-list { border-top: 1px solid var(--color-border); display: grid; margin-top: .5rem; padding-top: .5rem; }
.admin-row { align-items: center; display: flex; flex-wrap: wrap; gap: .65rem; min-height: 2.5rem; }
.status-pill { border-radius: 999px; font-size: .78rem; font-weight: 700; padding: .25rem .55rem; }
.status-active { background: var(--color-success-soft); color: var(--color-success); }
.status-pending { background: var(--color-warn-soft); color: var(--color-warn); }
.status-off { background: var(--color-surface-muted); color: var(--color-ink-soft); }
.text-button { background: transparent; border: 0; color: var(--color-brand); cursor: pointer; font: inherit; text-decoration: underline; }
.admin-row small { color: var(--color-muted); flex-basis: 100%; }
.empty-state { margin-top: 1rem; }
.notice-error { background: var(--color-danger-soft); color: var(--color-danger); }
.pagination { align-items: center; display: flex; gap: 1rem; justify-content: center; margin: 1.5rem 0; }
.visually-hidden { clip: rect(0, 0, 0, 0); clip-path: inset(50%); height: 1px; overflow: hidden; position: absolute; white-space: nowrap; width: 1px; }
@media (max-width: 40rem) { .management-toolbar { align-items: stretch; } .search-form { flex-basis: 100%; } }
</style>
