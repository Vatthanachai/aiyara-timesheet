<script setup lang="ts">
type ProfileSummary = {
  email: string
  firstName: string
  lastName: string
  photoUrl: string | null
}

const { navigation } = useRemoteNavigation()
const { locale, setLocale } = useLocale()
const { token, role, logout, restore } = useSession()
const config = useRuntimeConfig()
const profile = ref<ProfileSummary | null>(null)
const profileMenu = ref<HTMLDetailsElement | null>(null)

const profileName = computed(() => {
  const fullName = [profile.value?.firstName, profile.value?.lastName]
    .filter((part) => part?.trim())
    .join(' ')
  return fullName || profile.value?.email || (locale.value === 'th' ? 'โปรไฟล์' : 'Profile')
})

const profileInitials = computed(() => {
  const initials = [profile.value?.firstName, profile.value?.lastName]
    .filter((part) => part?.trim())
    .map((part) => part!.trim()[0])
    .join('')
  return initials || profile.value?.email?.[0]?.toUpperCase() || '•'
})

async function loadProfile() {
  if (!token.value) {
    profile.value = null
    return
  }

  try {
    profile.value = await $fetch<ProfileSummary>(`${config.public.gatewayUrl}/api/v1/identity/profile/me`, {
      headers: { Authorization: `Bearer ${token.value}` }
    })
  } catch {
    profile.value = null
  }
}

function closeProfileMenu() {
  profileMenu.value?.removeAttribute('open')
}

async function signOut() {
  closeProfileMenu()
  profile.value = null
  await logout()
}

onMounted(() => {
  restore()
})

watch(token, (value) => {
  if (value) void loadProfile()
  else profile.value = null
})
</script>

<template>
  <div class="shell">
    <header class="shell-header">
      <NuxtLink class="brand" to="/">Aiyara Timesheet</NuxtLink>
      <nav aria-label="Primary navigation" class="navigation">
        <NuxtLink v-for="item in navigation.filter((item) => item.id !== 'identity')" :key="item.id" :to="item.path">
          {{ locale === 'th' ? item.label : item.labelEn }}
        </NuxtLink>
        <NuxtLink v-if="token && role === 'PlatformAdmin'" to="/sys-admin/organizations">
          {{ locale === 'th' ? 'จัดการองค์กร' : 'Organizations' }}
        </NuxtLink>
      </nav>
      <div class="header-actions">
        <button class="locale-button" type="button" @click="setLocale(locale === 'th' ? 'en' : 'th')">{{ locale === 'th' ? 'EN' : 'TH' }}</button>
        <details v-if="token" ref="profileMenu" class="profile-menu">
          <summary class="profile-trigger" :aria-label="profileName">
            <img v-if="profile?.photoUrl" class="profile-avatar" :src="profile.photoUrl" alt="" />
            <span v-else class="profile-avatar profile-avatar-fallback" aria-hidden="true">{{ profileInitials }}</span>
            <span class="profile-name">{{ profileName }}</span>
            <svg class="profile-chevron" viewBox="0 0 20 20" aria-hidden="true"><path d="m5 7.5 5 5 5-5" /></svg>
          </summary>
          <div class="profile-dropdown">
            <NuxtLink to="/remote/identity" @click="closeProfileMenu">
              {{ locale === 'th' ? 'ตั้งค่าโปรไฟล์' : 'Profile settings' }}
            </NuxtLink>
            <button type="button" class="sign-out-button" @click="signOut">
              {{ locale === 'th' ? 'ออกจากระบบ' : 'Sign out' }}
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
.header-actions { align-items: center; }
.header-actions .locale-button { border: 1px solid var(--color-border); border-radius: .5rem; background: white; padding: .5rem .7rem; }
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
  .profile-name { max-width: 8rem; }
}
</style>
