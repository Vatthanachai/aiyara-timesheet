<script setup lang="ts">
const { locale } = useLocale()
const config = useRuntimeConfig()
const code = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref('')
const activated = ref(false)
const pending = ref(false)

async function activate() {
  error.value = ''
  if (password.value !== confirmPassword.value) {
    error.value = locale.value === 'th'
      ? 'รหัสผ่านใหม่ไม่ตรงกัน'
      : 'The passwords do not match.'
    return
  }

  pending.value = true
  try {
    await $fetch(`${config.public.gatewayUrl}/api/v1/auth/activate`, {
      method: 'POST',
      body: { code: code.value.trim(), password: password.value }
    })
    activated.value = true
    code.value = ''
    password.value = ''
    confirmPassword.value = ''
  } catch {
    error.value = locale.value === 'th'
      ? 'เปิดใช้งานไม่สำเร็จ กรุณาตรวจสอบรหัสและข้อกำหนดรหัสผ่านขององค์กร'
      : 'Activation failed. Check the code and your organization’s password requirements.'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ locale === 'th' ? 'เปิดใช้งานบัญชี' : 'Activate your account' }}</h1>
    <p>{{ locale === 'th'
      ? 'ใช้รหัสชั่วคราวจากอีเมลเพื่อตั้งรหัสผ่านสำหรับบัญชีของคุณ'
      : 'Use the one-time code from your email to set your account password.' }}</p>
  </section>

  <section v-if="activated" class="login-card" role="status">
    <h2>{{ locale === 'th' ? 'เปิดใช้งานสำเร็จ' : 'Account activated' }}</h2>
    <p>{{ locale === 'th'
      ? 'เข้าสู่ระบบด้วยรหัสองค์กร อีเมล และรหัสผ่านใหม่ของคุณ'
      : 'Sign in with your organization code, email, and new password.' }}</p>
    <NuxtLink class="button" to="/">{{ locale === 'th' ? 'ไปหน้าเข้าสู่ระบบ' : 'Go to sign in' }}</NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="activate">
    <label>
      {{ locale === 'th' ? 'รหัสเปิดใช้งานจากอีเมล' : 'Activation code from email' }}
      <input v-model="code" required autocomplete="one-time-code" />
    </label>
    <label>
      {{ locale === 'th' ? 'ตั้งรหัสผ่านใหม่' : 'New password' }}
      <input v-model="password" required type="password" autocomplete="new-password" />
    </label>
    <label>
      {{ locale === 'th' ? 'ยืนยันรหัสผ่านใหม่' : 'Confirm new password' }}
      <input v-model="confirmPassword" required type="password" autocomplete="new-password" />
    </label>
    <p>{{ locale === 'th'
      ? 'รหัสผ่านต้องเป็นไปตามข้อกำหนดขององค์กร'
      : 'Your password must meet your organization’s requirements.' }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending
        ? (locale === 'th' ? 'กำลังเปิดใช้งาน…' : 'Activating…')
        : (locale === 'th' ? 'เปิดใช้งานบัญชี' : 'Activate account') }}
    </button>
    <NuxtLink to="/">{{ locale === 'th' ? 'กลับไปเข้าสู่ระบบ' : 'Back to sign in' }}</NuxtLink>
  </form>
</template>
