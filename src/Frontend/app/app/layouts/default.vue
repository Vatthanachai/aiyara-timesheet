<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
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
const { token, role, logout, restore } = useSession()
const authorizedFetch = useAuthorizedFetch()
const config = useRuntimeConfig()
const text = useLocaleText(locale, {
  th: { profile: 'โปรไฟล์', organizations: 'ผู้ดูแลระบบ', profileSettings: 'ตั้งค่าโปรไฟล์', signOut: 'ออกจากระบบ' },
  en: { profile: 'Profile', organizations: 'System admin', profileSettings: 'Profile settings', signOut: 'Sign out' }
})
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
  profile.value = null
  await logout()
  await navigateTo('/signed-out')
}

onMounted(() => {
  restore()
  window.addEventListener('message', onProfileUpdated)
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
  window.removeEventListener('message', onProfileUpdated)
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
    <main class="shell-main">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.shell-header { position: relative; }
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
.profile-avatar-fallback { align-items: center; background: #eaf1ff; color: var(--color-brand); display: inline-flex; font-size: .9rem; font-weight: 700; justify-content: center; }
.profile-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.profile-chevron { fill: none; flex: 0 0 1rem; height: 1rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.7; transition: transform 150ms ease; width: 1rem; }
.profile-menu[open] .profile-chevron { transform: rotate(180deg); }
.profile-dropdown { background: white; border: 1px solid var(--color-border); border-radius: .75rem; box-shadow: 0 0.75rem 2rem rgb(20 32 52 / 14%); min-width: 13rem; padding: .4rem; position: absolute; right: 0; top: calc(100% + .5rem); z-index: 20; }
.profile-dropdown a, .sign-out-button { background: transparent; border: 0; border-radius: .45rem; color: inherit; display: block; font: inherit; padding: .7rem .75rem; text-align: left; text-decoration: none; width: 100%; }
.profile-dropdown a:hover, .sign-out-button:hover { background: #f3f6fa; }
.sign-out-button { color: #a62828; }
@media (max-width: 40rem) {
  .shell-header { align-items: center; flex-direction: row; flex-wrap: wrap; }
  .navigation { order: 3; width: 100%; }
  .header-actions { margin-left: auto; }
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
