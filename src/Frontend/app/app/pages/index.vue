<script setup lang="ts">
import { safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
import type { SignInOrganization } from '../composables/useSession'
const { visibleNavigation } = useRemoteNavigation()
const { token, role, login, signIn } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    heroTitle: 'พื้นที่ทำงานของคุณ', heroSubtitle: 'บันทึกเวลาทำงาน จัดการงานและข้อมูลส่วนตัว',
    signIn: 'เข้าสู่ระบบ', email: 'อีเมล', password: 'รหัสผ่าน',
    chooseOrganization: 'เลือกองค์กร', chooseOrganizationHint: 'บัญชีนี้อยู่ในหลายองค์กร เลือกองค์กรที่ต้องการเข้าใช้งาน',
    back: 'ย้อนกลับ', organizations: 'ผู้ดูแลระบบ', organizationsHint: 'ภาพรวม จัดการองค์กร และประวัติการดำเนินการของระบบ',
    signUp: 'สมัครใช้งานองค์กร', activateAccount: 'เปิดใช้งานบัญชี', adminSignIn: 'เข้าสู่ระบบผู้ดูแลระบบ'
  },
  en: {
    heroTitle: 'Your workspace', heroSubtitle: 'Record time, manage tasks and your profile.',
    signIn: 'Sign in', email: 'Email', password: 'Password',
    chooseOrganization: 'Choose an organization', chooseOrganizationHint: 'This account belongs to several organizations. Choose the one to open.',
    back: 'Back', organizations: 'System admin', organizationsHint: 'Overview, organization management, and the platform audit log.',
    signUp: 'Sign up your organization', activateAccount: 'Activate account', adminSignIn: 'System administrator sign in'
  }
})
const email = ref('')
const password = ref('')
const organizations = ref<SignInOrganization[]>([])
const error = ref('')
const pending = ref(false)

async function submit(action: () => Promise<unknown>) {
  pending.value = true
  error.value = ''
  try { await action() }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { pending.value = false }
}

function signInByEmail() {
  return submit(async () => {
    const result = await signIn(email.value, password.value)
    organizations.value = result.session ? [] : result.organizations
    if (result.session) password.value = ''
  })
}

function chooseOrganization(organization: SignInOrganization) {
  return submit(async () => {
    await login(organization.organizationCode, email.value, password.value)
    password.value = ''
    organizations.value = []
  })
}

function backToSignIn() {
  organizations.value = []
  password.value = ''
  error.value = ''
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ text.heroTitle }}</h1>
    <p>{{ text.heroSubtitle }}</p>
  </section>

  <section v-if="!token && organizations.length" class="login-card" aria-labelledby="choose-organization">
    <h2 id="choose-organization">{{ text.chooseOrganization }}</h2>
    <p>{{ text.chooseOrganizationHint }}</p>
    <button v-for="organization in organizations" :key="organization.organizationCode"
      class="button" type="button" :disabled="pending" @click="chooseOrganization(organization)">
      {{ organization.name }} ({{ organization.organizationCode }})
    </button>
    <p v-if="error" role="alert">{{ error }}</p>
    <button type="button" class="link-button" @click="backToSignIn">{{ text.back }}</button>
  </section>

  <form v-else-if="!token" class="login-card" @submit.prevent="signInByEmail">
    <h2>{{ text.signIn }}</h2>
    <label>{{ text.email }}<input v-model="email" required type="email" autocomplete="username" /></label>
    <label>{{ text.password }}<input v-model="password" required type="password" autocomplete="current-password" /></label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">{{ text.signIn }}</button>
    <NuxtLink to="/signup">{{ text.signUp }}</NuxtLink>
    <NuxtLink to="/activate">{{ text.activateAccount }}</NuxtLink>
    <NuxtLink to="/sys-admin/login">{{ text.adminSignIn }}</NuxtLink>
  </form>

  <section v-if="token" aria-label="Available applications" class="remote-grid">
    <NuxtLink v-for="remote in visibleNavigation" :key="remote.id" class="remote-card" :to="remote.path">
      <h2>{{ remote.label[locale] }}</h2>
      <p>{{ remote.description[locale] }}</p>
    </NuxtLink>
    <NuxtLink v-if="role === 'PlatformAdmin'" class="remote-card" to="/sys-admin">
      <h2>{{ text.organizations }}</h2>
      <p>{{ text.organizationsHint }}</p>
    </NuxtLink>
  </section>
</template>
