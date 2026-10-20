<script setup lang="ts">
import { safeError, unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'รับคำเชิญเข้าองค์กร', subtitle: 'กรอกรหัสคำเชิญจากอีเมลที่ผู้ดูแลองค์กรส่งให้คุณ',
    code: 'รหัสคำเชิญจากอีเมล', accept: 'รับคำเชิญ', accepting: 'กำลังรับคำเชิญ…',
    acceptedTitle: 'รับคำเชิญแล้ว',
    acceptedBody: 'เราส่งรหัสเปิดใช้งานไปที่อีเมลเดียวกันแล้ว ใช้รหัสนั้นตั้งรหัสผ่านในหน้าเปิดใช้งาน',
    joinedBody: 'บัญชีของคุณเข้าร่วมองค์กรแล้ว เข้าสู่ระบบด้วยอีเมลและรหัสผ่านเดิมของคุณ',
    activate: 'ไปหน้าเปิดใช้งาน', signIn: 'ไปหน้าเข้าสู่ระบบ', backToSignIn: 'กลับไปเข้าสู่ระบบ',
    invalidCode: 'รหัสคำเชิญไม่ถูกต้อง ถูกใช้ไปแล้ว หรือหมดอายุ กรุณาขอคำเชิญใหม่จากผู้ดูแลองค์กร',
    conflict: 'บัญชีนี้เป็นสมาชิกขององค์กรนี้อยู่แล้ว หรือใช้รับคำเชิญนี้ไม่ได้ กรุณาติดต่อผู้ดูแลองค์กร'
  },
  en: {
    title: 'Accept your invitation', subtitle: 'Enter the invitation code from the email your organization administrator sent you.',
    code: 'Invitation code from email', accept: 'Accept invitation', accepting: 'Accepting…',
    acceptedTitle: 'Invitation accepted',
    acceptedBody: 'We sent an activation code to the same email. Use it on the activation page to set your password.',
    joinedBody: 'Your account has joined the organization. Sign in with your email and your existing password.',
    activate: 'Go to activation', signIn: 'Go to sign in', backToSignIn: 'Back to sign in',
    invalidCode: 'This invitation code is invalid, already used, or expired. Ask your organization administrator for a new invitation.',
    conflict: 'This account is already a member of the organization, or cannot accept this invitation. Contact your organization administrator.'
  }
})
const config = useRuntimeConfig()
const code = ref('')
const error = ref('')
// An account that already has a password joins as Active and needs no activation.
const accepted = ref<'PendingActivation' | 'Active' | null>(null)
const pending = ref(false)

async function accept() {
  error.value = ''
  pending.value = true
  try {
    const response = await $fetch<ApiEnvelope<{ status: string }>>(`${config.public.gatewayUrl}/api/v1/invitations/accept`, {
      method: 'POST',
      body: { code: code.value.trim() }
    })
    accepted.value = unwrapResponse(response).status === 'Active' ? 'Active' : 'PendingActivation'
    code.value = ''
  } catch (cause) {
    const errorCode = (cause as { data?: { errorCode?: string } })?.data?.errorCode
    error.value = errorCode === 'INVALIDINVITATION' ? text.value.invalidCode
      : errorCode === 'CONFLICT' ? text.value.conflict
      : safeError(cause, locale.value)
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

  <section v-if="accepted" class="login-card" role="status">
    <h2>{{ text.acceptedTitle }}</h2>
    <template v-if="accepted === 'Active'">
      <p>{{ text.joinedBody }}</p>
      <NuxtLink class="button" to="/">{{ text.signIn }}</NuxtLink>
    </template>
    <template v-else>
      <p>{{ text.acceptedBody }}</p>
      <NuxtLink class="button" to="/activate">{{ text.activate }}</NuxtLink>
      <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
    </template>
  </section>

  <form v-else class="login-card" @submit.prevent="accept">
    <label>
      {{ text.code }}
      <input v-model="code" required autocomplete="one-time-code" maxlength="200" />
    </label>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending ? text.accepting : text.accept }}
    </button>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </form>
</template>
