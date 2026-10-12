<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'เปิดใช้งานบัญชี', subtitle: 'ใช้รหัสชั่วคราวจากอีเมลเพื่อตั้งรหัสผ่านสำหรับบัญชีของคุณ',
    activatedTitle: 'เปิดใช้งานสำเร็จ', activatedBody: 'เข้าสู่ระบบด้วยรหัสองค์กร อีเมล และรหัสผ่านใหม่ของคุณ',
    goToSignIn: 'ไปหน้าเข้าสู่ระบบ', code: 'รหัสเปิดใช้งานจากอีเมล', newPassword: 'ตั้งรหัสผ่านใหม่',
    confirmPassword: 'ยืนยันรหัสผ่านใหม่', passwordHint: 'รหัสผ่านต้องเป็นไปตามข้อกำหนดขององค์กร',
    activating: 'กำลังเปิดใช้งาน…', activateAccount: 'เปิดใช้งานบัญชี', backToSignIn: 'กลับไปเข้าสู่ระบบ',
    passwordMismatch: 'รหัสผ่านใหม่ไม่ตรงกัน',
    activateError: 'เปิดใช้งานไม่สำเร็จ กรุณาตรวจสอบรหัสและข้อกำหนดรหัสผ่านขององค์กร'
  },
  en: {
    title: 'Activate your account', subtitle: 'Use the one-time code from your email to set your account password.',
    activatedTitle: 'Account activated', activatedBody: 'Sign in with your organization code, email, and new password.',
    goToSignIn: 'Go to sign in', code: 'Activation code from email', newPassword: 'New password',
    confirmPassword: 'Confirm new password', passwordHint: 'Your password must meet your organization’s requirements.',
    activating: 'Activating…', activateAccount: 'Activate account', backToSignIn: 'Back to sign in',
    passwordMismatch: 'The passwords do not match.',
    activateError: 'Activation failed. Check the code and your organization’s password requirements.'
  }
})
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
    error.value = text.value.passwordMismatch
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
    error.value = text.value.activateError
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <section v-if="activated" class="login-card" role="status">
    <h2>{{ text.activatedTitle }}</h2>
    <p>{{ text.activatedBody }}</p>
    <NuxtLink class="button" to="/">{{ text.goToSignIn }}</NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="activate">
    <label>
      {{ text.code }}
      <input v-model="code" required autocomplete="one-time-code" />
    </label>
    <label>
      {{ text.newPassword }}
      <input v-model="password" required type="password" autocomplete="new-password" />
    </label>
    <label>
      {{ text.confirmPassword }}
      <input v-model="confirmPassword" required type="password" autocomplete="new-password" />
    </label>
    <p>{{ text.passwordHint }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending ? text.activating : text.activateAccount }}
    </button>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </form>
</template>
