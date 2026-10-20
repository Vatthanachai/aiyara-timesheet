<script setup lang="ts">
import { safeError, type ApiError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'ตั้งรหัสผ่านใหม่', subtitle: 'ใช้รหัสรีเซ็ตจากอีเมลเพื่อตั้งรหัสผ่านใหม่ของบัญชี',
    code: 'รหัสรีเซ็ตจากอีเมล', newPassword: 'รหัสผ่านใหม่', confirmPassword: 'ยืนยันรหัสผ่านใหม่',
    passwordHint: 'หากบัญชีอยู่หลายองค์กร รหัสผ่านต้องเป็นไปตามข้อกำหนดของทุกองค์กร',
    resetting: 'กำลังบันทึก…', reset: 'ตั้งรหัสผ่านใหม่', passwordMismatch: 'รหัสผ่านใหม่ไม่ตรงกัน',
    invalidCode: 'รหัสรีเซ็ตไม่ถูกต้องหรือหมดอายุแล้ว กรุณาขอรหัสใหม่',
    weakPassword: 'รหัสผ่านใหม่ไม่เป็นไปตามข้อกำหนดขององค์กรที่คุณสังกัด',
    doneTitle: 'ตั้งรหัสผ่านใหม่แล้ว', doneBody: 'เข้าสู่ระบบด้วยรหัสผ่านใหม่ เซสชันเดิมทั้งหมดถูกออกจากระบบแล้ว',
    signIn: 'ไปหน้าเข้าสู่ระบบ', adminSignIn: 'เข้าสู่ระบบผู้ดูแลระบบ',
    requestCode: 'ขอรหัสรีเซ็ตใหม่', backToSignIn: 'กลับไปเข้าสู่ระบบ'
  },
  en: {
    title: 'Set a new password', subtitle: 'Use the reset code from your email to set a new password for your account.',
    code: 'Reset code from email', newPassword: 'New password', confirmPassword: 'Confirm new password',
    passwordHint: 'If your account belongs to several organizations, the password must meet every one’s requirements.',
    resetting: 'Saving…', reset: 'Set new password', passwordMismatch: 'The passwords do not match.',
    invalidCode: 'This reset code is not valid or has expired. Request a new one.',
    weakPassword: 'The new password does not meet your organizations’ requirements.',
    doneTitle: 'Password updated', doneBody: 'Sign in with your new password. Every earlier session has been signed out.',
    signIn: 'Go to sign in', adminSignIn: 'System administrator sign in',
    requestCode: 'Request a new code', backToSignIn: 'Back to sign in'
  }
})
const config = useRuntimeConfig()
const code = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref('')
const done = ref(false)
const pending = ref(false)

function resetError(cause: unknown) {
  const errorCode = (cause as { data?: Partial<ApiError> } | null)?.data?.errorCode
  if (errorCode === 'INVALIDCREDENTIALS') return text.value.invalidCode
  if (errorCode === 'INVALIDINPUT') return text.value.weakPassword
  return safeError(cause, locale.value)
}

async function resetPassword() {
  error.value = ''
  if (password.value !== confirmPassword.value) {
    error.value = text.value.passwordMismatch
    return
  }

  pending.value = true
  try {
    await $fetch(`${config.public.gatewayUrl}/api/v1/auth/password/reset`, {
      method: 'POST',
      body: { code: code.value.trim(), password: password.value }
    })
    done.value = true
    code.value = ''
    password.value = ''
    confirmPassword.value = ''
  } catch (cause) {
    error.value = resetError(cause)
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

  <section v-if="done" class="login-card" role="status">
    <h2>{{ text.doneTitle }}</h2>
    <p>{{ text.doneBody }}</p>
    <NuxtLink class="button" to="/">{{ text.signIn }}</NuxtLink>
    <NuxtLink to="/sys-admin/login">{{ text.adminSignIn }}</NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="resetPassword">
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
      {{ pending ? text.resetting : text.reset }}
    </button>
    <NuxtLink to="/forgot-password">{{ text.requestCode }}</NuxtLink>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </form>
</template>
