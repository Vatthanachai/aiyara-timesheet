<script setup lang="ts">
const { navigation } = useRemoteNavigation()
const { token, login } = useSession()
const { locale } = useLocale()
const tenantId = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const pending = ref(false)
async function signIn() {
  pending.value = true
  error.value = ''
  try { await login(tenantId.value, email.value, password.value) }
  catch { error.value = locale.value === 'th' ? 'เข้าสู่ระบบไม่สำเร็จ' : 'Sign in failed' }
  finally { pending.value = false }
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ locale === 'th' ? 'พื้นที่ทำงานของคุณ' : 'Your workspace' }}</h1>
    <p>{{ locale === 'th' ? 'บันทึกเวลาทำงาน จัดการงานและข้อมูลส่วนตัว' : 'Record time, manage tasks and your profile.' }}</p>
  </section>

  <form v-if="!token" class="login-card" @submit.prevent="signIn">
    <h2>{{ locale === 'th' ? 'เข้าสู่ระบบ' : 'Sign in' }}</h2>
    <label>{{ locale === 'th' ? 'รหัสองค์กร' : 'Tenant ID' }}<input v-model="tenantId" required autocomplete="organization" /></label>
    <label>{{ locale === 'th' ? 'อีเมล' : 'Email' }}<input v-model="email" required type="email" autocomplete="username" /></label>
    <label>{{ locale === 'th' ? 'รหัสผ่าน' : 'Password' }}<input v-model="password" required type="password" autocomplete="current-password" /></label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">{{ locale === 'th' ? 'เข้าสู่ระบบ' : 'Sign in' }}</button>
  </form>

  <section v-if="token" aria-label="Available applications" class="remote-grid">
    <NuxtLink v-for="remote in navigation" :key="remote.id" class="remote-card" :to="remote.path">
      <h2>{{ remote.label }}</h2>
      <p>{{ remote.description }}</p>
    </NuxtLink>
  </section>
</template>
