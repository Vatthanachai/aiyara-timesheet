<script setup lang="ts">
import { safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
const { navigation } = useRemoteNavigation()
const { token, login } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    heroTitle: 'พื้นที่ทำงานของคุณ', heroSubtitle: 'บันทึกเวลาทำงาน จัดการงานและข้อมูลส่วนตัว',
    signIn: 'เข้าสู่ระบบ', organizationCode: 'รหัสองค์กร', email: 'อีเมล', password: 'รหัสผ่าน',
    signUp: 'สมัครใช้งานองค์กร', activateAccount: 'เปิดใช้งานบัญชี'
  },
  en: {
    heroTitle: 'Your workspace', heroSubtitle: 'Record time, manage tasks and your profile.',
    signIn: 'Sign in', organizationCode: 'Organization code', email: 'Email', password: 'Password',
    signUp: 'Sign up your organization', activateAccount: 'Activate account'
  }
})
const organizationCode = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const pending = ref(false)
async function signIn() {
  pending.value = true
  error.value = ''
  try { await login(organizationCode.value.trim().toUpperCase(), email.value, password.value) }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { pending.value = false }
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ text.heroTitle }}</h1>
    <p>{{ text.heroSubtitle }}</p>
  </section>

  <form v-if="!token" class="login-card" @submit.prevent="signIn">
    <h2>{{ text.signIn }}</h2>
    <label>{{ text.organizationCode }}<input v-model="organizationCode" required minlength="7" maxlength="7" autocapitalize="characters" autocomplete="organization" /></label>
    <label>{{ text.email }}<input v-model="email" required type="email" autocomplete="username" /></label>
    <label>{{ text.password }}<input v-model="password" required type="password" autocomplete="current-password" /></label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">{{ text.signIn }}</button>
    <NuxtLink to="/signup">{{ text.signUp }}</NuxtLink>
    <NuxtLink to="/activate">{{ text.activateAccount }}</NuxtLink>
  </form>

  <section v-if="token" aria-label="Available applications" class="remote-grid">
    <NuxtLink v-for="remote in navigation" :key="remote.id" class="remote-card" :to="remote.path">
      <h2>{{ remote.label[locale] }}</h2>
      <p>{{ remote.description[locale] }}</p>
    </NuxtLink>
  </section>
</template>
