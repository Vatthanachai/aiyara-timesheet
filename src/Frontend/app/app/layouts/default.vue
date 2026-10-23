<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
import { fieldLocaleKey, useLocaleText } from '../../../shared/i18n'
import { DEFAULT_FORMATS, readFormats, type Formats } from '../../../shared/date-format'
import { nameInitials } from '../../../shared/initials'
type ProfileSummary = {
  email: string
  firstName: string
  lastName: string
  photoUrl: string | null
}
type OrganizationSummary = { name: string; logoUrl: string | null; dateFormat?: string; timeFormat?: string }

const { visibleNavigation } = useRemoteNavigation()
const { locale } = useLocale()
provide(fieldLocaleKey, locale)
const { start: startTheme } = useTheme()
let stopTheme: (() => void) | undefined
const { token, role, logout, restore } = useSession()
const authorizedFetch = useAuthorizedFetch()
const config = useRuntimeConfig()
const text = useLocaleText(locale, {
  th: { profile: 'โปรไฟล์', organizations: 'ผู้ดูแลระบบ', profileSettings: 'ตั้งค่าโปรไฟล์', signOut: 'ออกจากระบบ',
    offline: 'ไม่มีการเชื่อมต่อ หน้าบันทึกเวลายังใช้งานได้ ส่วนอื่นต้องออนไลน์', update: 'มีเวอร์ชันใหม่', reload: 'โหลดใหม่',
    signOutPending: 'รายการที่รอซิงก์ ({count}) จะหายไปเมื่อออกจากระบบ ออกจากระบบต่อหรือไม่?' },
  en: { profile: 'Profile', organizations: 'System admin', profileSettings: 'Profile settings', signOut: 'Sign out',
    offline: 'You are offline. Record time still works; the rest needs a connection.', update: 'A new version is ready.', reload: 'Reload',
    signOutPending: 'Changes waiting to sync ({count}) will be lost if you sign out. Sign out anyway?' }
})
const online = useOnline()
const { pending: pendingChanges } = useOfflineData()
// The installed app's service worker (absent in development): a new version waits until the member chooses to reload.
const pwa = useNuxtApp().$pwa as { needRefresh?: Ref<boolean>; updateServiceWorker?: (reload?: boolean) => Promise<void> } | undefined
const updateReady = computed(() => Boolean(pwa?.needRefresh?.value))
function setOnline() { online.value = navigator.onLine }
// Shared with the home page, which greets the member by name.
const profile = useState<ProfileSummary | null>('profile-summary', () => null)
const profileMenu = ref<HTMLDetailsElement | null>(null)
let avatarObjectUrl = ''
// The signed-in organization's name and logo replace the product name in the header.
const organization = ref<OrganizationSummary | null>(null)
const organizationLogo = ref('')
// The organization's date and time formats, which the remote page passes to every remote.
const formats = useState<Formats>('organization-formats', () => DEFAULT_FORMATS)

const profileName = computed(() => {
  const fullName = [profile.value?.firstName, profile.value?.lastName]
    .filter((part) => part?.trim())
    .join(' ')
  return fullName || profile.value?.email || text.value.profile
})

const profileInitials = computed(() =>
  nameInitials(profile.value?.firstName, profile.value?.lastName) || profile.value?.email?.[0]?.toUpperCase() || '•')

async function loadProfile() {
  if (!token.value) {
    profile.value = null
    return
  }

  try {
    const url = `${config.public.gatewayUrl}/api/v1/identity/profile/me`
    const value = unwrapResponse(await authorizedFetch<ApiEnvelope<ProfileSummary>>(url))
    if (value.photoUrl?.startsWith('/')) {
      const photo = await authorizedFetch<Blob>(`${config.public.gatewayUrl}${value.photoUrl}`, true)
      if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl)
      avatarObjectUrl = URL.createObjectURL(photo)
      value.photoUrl = avatarObjectUrl
    } else if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl)
      avatarObjectUrl = ''
    }
    profile.value = value
  } catch {
    profile.value = null
    if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl)
    avatarObjectUrl = ''
  }
}

function onProfileUpdated(event: MessageEvent) {
  if (event.origin === new URL(config.public.remotes.administration).origin &&
      event.data?.type === 'aiyara.organization.updated') {
    void loadOrganization()
    return
  }
  if (event.origin !== new URL(config.public.remotes.identity).origin ||
      event.data?.type !== 'aiyara.profile.updated') return
  void loadProfile()
}

function clearOrganization() {
  organization.value = null
  formats.value = DEFAULT_FORMATS
  if (organizationLogo.value) URL.revokeObjectURL(organizationLogo.value)
  organizationLogo.value = ''
}

async function loadOrganization() {
  // Platform Admins sign in to the platform tenant, which has no customer branding.
  if (!token.value || role.value === 'PlatformAdmin') {
    clearOrganization()
    return
  }
  try {
    const value = unwrapResponse(await authorizedFetch<ApiEnvelope<OrganizationSummary>>(
      `${config.public.gatewayUrl}/api/v1/identity/organization`))
    const logo = value.logoUrl
      ? URL.createObjectURL(await authorizedFetch<Blob>(`${config.public.gatewayUrl}${value.logoUrl}`, true))
      : ''
    if (organizationLogo.value) URL.revokeObjectURL(organizationLogo.value)
    organizationLogo.value = logo
    organization.value = value
    formats.value = readFormats(value)
  } catch { clearOrganization() } // The product name stays when branding cannot load.
}

function closeProfileMenu() {
  profileMenu.value?.removeAttribute('open')
}

async function signOut() {
  closeProfileMenu()
  if (pendingChanges.value > 0 && !confirm(text.value.signOutPending.replace('{count}', String(pendingChanges.value)))) return
  profile.value = null
  await logout()
  await navigateTo('/signed-out')
}

onMounted(() => {
  stopTheme = startTheme()
  restore()
  window.addEventListener('message', onProfileUpdated)
  setOnline()
  window.addEventListener('online', setOnline)
  window.addEventListener('offline', setOnline)
})

watch(token, (value) => {
  void loadOrganization()
  if (value) void loadProfile()
  else {
    profile.value = null
    if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl)
    avatarObjectUrl = ''
  }
})

onUnmounted(() => {
  stopTheme?.()
  window.removeEventListener('message', onProfileUpdated)
  window.removeEventListener('online', setOnline)
  window.removeEventListener('offline', setOnline)
  if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl)
  clearOrganization()
})
</script>

<template>
  <div class="shell">
    <header class="shell-header">
      <NuxtLink class="brand" to="/">
        <img v-if="organization && organizationLogo" class="brand-logo" :src="organizationLogo" alt="" />
        <span class="brand-name">{{ organization?.name || 'Aiyara Timesheet' }}</span>
      </NuxtLink>
      <nav v-if="token" aria-label="Primary navigation" class="navigation">
        <NuxtLink v-for="item in visibleNavigation.filter((item) => item.id !== 'identity')" :key="item.id" :to="item.path">
          {{ item.label[locale] }}
        </NuxtLink>
        <NuxtLink v-if="role === 'PlatformAdmin'" to="/sys-admin">
          {{ text.organizations }}
        </NuxtLink>
      </nav>
      <div class="header-actions">
        <ThemeSwitch />
        <LocaleSwitch />
        <details v-if="token" ref="profileMenu" class="profile-menu">
          <summary class="profile-trigger" :aria-label="profileName">
            <img v-if="profile?.photoUrl" class="profile-avatar" :src="profile.photoUrl" alt="" />
            <span v-else class="profile-avatar profile-avatar-fallback" aria-hidden="true">{{ profileInitials }}</span>
            <span class="profile-name">{{ profileName }}</span>
            <svg class="profile-chevron" viewBox="0 0 20 20" aria-hidden="true"><path d="m5 7.5 5 5 5-5" /></svg>
          </summary>
          <div class="profile-dropdown">
            <NuxtLink to="/remote/identity" @click="closeProfileMenu">
              {{ text.profileSettings }}
            </NuxtLink>
            <button type="button" class="sign-out-button" @click="signOut">
              {{ text.signOut }}
            </button>
          </div>
        </details>
      </div>
    </header>
    <p v-if="!online" class="shell-banner shell-offline" role="status">{{ text.offline }}</p>
    <p v-if="updateReady" class="shell-banner" role="status">{{ text.update }}
      <button type="button" class="banner-button" @click="pwa?.updateServiceWorker?.(true)">{{ text.reload }}</button></p>
    <main class="shell-main">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.shell-banner { background: var(--color-warn-soft); border-bottom: 1px solid var(--color-warn-line); color: var(--color-warn); margin: 0; padding: .6rem clamp(var(--space-4), 4vw, var(--space-8)); text-align: center; }
.banner-button { background: transparent; border: 1px solid currentColor; border-radius: .45rem; color: inherit; cursor: pointer; font: inherit; margin-left: .5rem; padding: .15rem .6rem; }
/* Sticky on wider screens (main.css); on phones the two-line header scrolls away with the page. */
.shell-header { position: sticky; }
.brand { align-items: center; color: inherit; display: inline-flex; gap: .6rem; min-width: 0; }
.brand-logo { flex: 0 0 auto; height: 2.25rem; max-width: 7rem; object-fit: contain; }
.brand-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: min(22rem, 45vw); }
.header-actions { align-items: center; }
.profile-menu { position: relative; }
.profile-menu summary { list-style: none; }
.profile-menu summary::-webkit-details-marker { display: none; }
.profile-trigger { align-items: center; border: 1px solid var(--color-border); border-radius: 999px; cursor: pointer; display: flex; gap: .6rem; max-width: min(20rem, 55vw); padding: .25rem .75rem .25rem .3rem; }
.profile-trigger:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.profile-avatar { border-radius: 50%; flex: 0 0 2.25rem; height: 2.25rem; object-fit: cover; width: 2.25rem; }
.profile-avatar-fallback { align-items: center; background: var(--color-brand-soft); color: var(--color-brand); display: inline-flex; font-size: .9rem; font-weight: 700; justify-content: center; }
.profile-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.profile-chevron { fill: none; flex: 0 0 1rem; height: 1rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.7; transition: transform 150ms ease; width: 1rem; }
.profile-menu[open] .profile-chevron { transform: rotate(180deg); }
.profile-dropdown { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .75rem; box-shadow: var(--elev-2); min-width: 13rem; padding: .4rem; position: absolute; right: 0; top: calc(100% + .5rem); z-index: 20; }
.profile-dropdown a, .sign-out-button { background: transparent; border: 0; border-radius: .45rem; color: inherit; display: block; font: inherit; padding: .7rem .75rem; text-align: left; text-decoration: none; width: 100%; }
.profile-dropdown a:hover, .sign-out-button:hover { background: var(--color-surface-muted); }
.sign-out-button { color: var(--color-danger); }
@media (max-width: 40rem) {
  .shell-header { align-items: center; flex-direction: row; flex-wrap: wrap; position: relative; }
  .navigation { order: 3; width: 100%; }
  /* The profile stays beside the brand; when the switches do not fit they wrap under it, to the right. */
  .header-actions { flex: 1 1 0; flex-wrap: wrap; justify-content: flex-end; margin-left: auto; min-width: 0; row-gap: .5rem; }
  .profile-menu { order: -1; }
  .profile-trigger { gap: .4rem; max-width: min(55vw, 15rem); }
  .profile-name { max-width: clamp(3.5rem, 25vw, 8rem); }
  .profile-dropdown { max-width: calc(100vw - 2rem); min-width: min(13rem, calc(100vw - 2rem)); }
}
@media (max-width: 22rem) {
  .header-actions { gap: .4rem; }
  .profile-trigger { padding-right: .5rem; }
  .profile-avatar { flex-basis: 2rem; height: 2rem; width: 2rem; }
  .profile-name { max-width: 5.5rem; }
}
</style>
