<script setup lang="ts">
import { safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'ลืมรหัสผ่าน', subtitle: 'กรอกอีเมลของบัญชี เราจะส่งรหัสสำหรับตั้งรหัสผ่านใหม่ให้',
    email: 'อีเมล', send: 'ส่งรหัสรีเซ็ต', sending: 'กำลังส่ง…',
    sentTitle: 'ตรวจสอบอีเมลของคุณ',
    sentBody: 'หากมีบัญชีที่เปิดใช้งานแล้วด้วยอีเมลนี้ เราได้ส่งรหัสรีเซ็ตไปแล้ว รหัสใช้ได้ 24 ชั่วโมง',
    haveCode: 'ฉันมีรหัสแล้ว', backToSignIn: 'กลับไปเข้าสู่ระบบ'
  },
  en: {
    title: 'Forgot your password?', subtitle: 'Enter your account email and we will send you a code to set a new password.',
    email: 'Email', send: 'Send reset code', sending: 'Sending…',
    sentTitle: 'Check your email',
    sentBody: 'If an activated account uses this email, a reset code is on its way. The code is valid for 24 hours.',
    haveCode: 'I have a code', backToSignIn: 'Back to sign in'
  }
})
const config = useRuntimeConfig()
const email = ref('')
const error = ref('')
const sent = ref(false)
const pending = ref(false)

async function requestReset() {
  error.value = ''
  pending.value = true
  try {
    // Identity answers 202 whether or not the email has an account, so the page does too.
    await $fetch(`${config.public.gatewayUrl}/api/v1/auth/password/forgot`, {
      method: 'POST',
      body: { email: email.value.trim() }
    })
    sent.value = true
  } catch (cause) {
    error.value = safeError(cause, locale.value)
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

  <section v-if="sent" class="login-card" role="status">
    <h2>{{ text.sentTitle }}</h2>
    <p>{{ text.sentBody }}</p>
    <NuxtLink class="button" to="/reset-password">{{ text.haveCode }}</NuxtLink>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="requestReset">
    <label>
      {{ text.email }}
      <input v-model="email" required type="email" autocomplete="username" />
    </label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending ? text.sending : text.send }}
    </button>
    <NuxtLink to="/reset-password">{{ text.haveCode }}</NuxtLink>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </form>
</template>
