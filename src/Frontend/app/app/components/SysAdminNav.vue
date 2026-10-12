<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: { label: 'ผู้ดูแลระบบ', overview: 'ภาพรวม', organizations: 'องค์กร', audit: 'ประวัติการดำเนินการ' },
  en: { label: 'System administration', overview: 'Overview', organizations: 'Organizations', audit: 'Audit log' }
})
</script>

<template>
  <nav class="sysadmin-nav" :aria-label="text.label">
    <NuxtLink to="/sys-admin" exact-active-class="is-active">{{ text.overview }}</NuxtLink>
    <NuxtLink to="/sys-admin/organizations" active-class="is-active">{{ text.organizations }}</NuxtLink>
    <NuxtLink to="/sys-admin/audit" active-class="is-active">{{ text.audit }}</NuxtLink>
  </nav>
</template>
