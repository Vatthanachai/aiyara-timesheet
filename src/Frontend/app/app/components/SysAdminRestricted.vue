<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'ไม่มีสิทธิ์เข้าถึง',
    body: 'หน้านี้สำหรับผู้ดูแลระบบเท่านั้น กรุณาเข้าสู่ระบบด้วยบัญชี sys-admin',
    signIn: 'กลับหน้าเข้าสู่ระบบ'
  },
  en: {
    title: 'Access restricted',
    body: 'This page is limited to system administrators. Sign in with a sys-admin account.',
    signIn: 'Back to sign in'
  }
})
</script>

<template>
  <section class="sysadmin-card" role="alert">
    <h2>{{ text.title }}</h2>
    <p>{{ text.body }}</p>
    <NuxtLink class="button" to="/sys-admin/login">{{ text.signIn }}</NuxtLink>
  </section>
</template>
