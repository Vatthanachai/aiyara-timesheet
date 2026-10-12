<script setup lang="ts">
import { safeError } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'
const { token, role, loginPlatformAdmin } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', heroTitle: 'เข้าสู่ระบบผู้ดูแลระบบ',
    heroSubtitle: 'สำหรับบัญชี sys-admin ไม่ต้องกรอกรหัสองค์กร',
    signIn: 'เข้าสู่ระบบ', email: 'อีเมล', password: 'รหัสผ่าน',
    organizationSignIn: 'เข้าสู่ระบบด้วยรหัสองค์กร'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', heroTitle: 'System administrator sign in',
    heroSubtitle: 'For sys-admin accounts. No organization code required.',
    signIn: 'Sign in', email: 'Email', password: 'Password',
    organizationSignIn: 'Sign in with an organization code'
  }
})
const email = ref('')
const password = ref('')
const error = ref('')
const pending = ref(false)

watchEffect(() => {
  if (token.value && role.value === 'PlatformAdmin') navigateTo('/sys-admin')
})

async function signIn() {
  pending.value = true
  error.value = ''
  try { await loginPlatformAdmin(email.value, password.value) }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { pending.value = false }
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.heroTitle }}</h1>
    <p>{{ text.heroSubtitle }}</p>
  </section>

  <form class="login-card" @submit.prevent="signIn">
    <h2>{{ text.signIn }}</h2>
    <label>{{ text.email }}<input v-model="email" required type="email" autocomplete="username" /></label>
    <label>{{ text.password }}<input v-model="password" required type="password" autocomplete="current-password" /></label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">{{ text.signIn }}</button>
    <NuxtLink to="/">{{ text.organizationSignIn }}</NuxtLink>
  </form>
</template>
