<script setup lang="ts">
import { fieldErrors, safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
type Profile = { id: string; email: string; firstName: string; lastName: string; photoUrl: string | null; jobTitle: string | null }
const token = ref('')
const config = useRuntimeConfig()
const gateway = ref('http://localhost:8081')
const locale = ref<'th' | 'en'>('th')
const profile = reactive({ firstName: '', lastName: '', photoUrl: '', jobTitle: '' })
const email = ref('')
const error = ref('')
const validation = ref<Record<string, string[]>>({})
const notice = ref('')
const copy = computed(() => locale.value === 'th' ? {
  title: 'ข้อมูลส่วนตัว', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
  firstName: 'ชื่อ', lastName: 'นามสกุล', jobTitle: 'ตำแหน่งงาน',
  photo: 'รูปภาพ (HTTPS URL)', save: 'บันทึก', loadedError: 'โหลดโปรไฟล์ไม่สำเร็จ',
  saved: 'บันทึกแล้ว', saveError: 'บันทึกไม่สำเร็จ'
} : {
  title: 'My profile', signIn: 'Please sign in through Aiyara Shell',
  firstName: 'First name', lastName: 'Last name', jobTitle: 'Job title',
  photo: 'Photo (HTTPS URL)', save: 'Save', loadedError: 'Could not load profile',
  saved: 'Saved', saveError: 'Save failed'
})
onMounted(() => {
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = event.data.locale === 'en' ? 'en' : 'th'
    if (token.value) load()
  })
})
async function load() {
  try {
    const value = unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<Profile>>(`${gateway.value}/api/v1/identity/profile/me`,
        { headers: { Authorization: `Bearer ${accessToken}` } }), token, config.public.shellOrigin))
    email.value = value.email
    profile.firstName = value.firstName; profile.lastName = value.lastName
    profile.photoUrl = value.photoUrl || ''; profile.jobTitle = value.jobTitle || ''
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
async function save() {
  validation.value = {}
  try {
    await withRemoteRefresh(accessToken =>
      $fetch(`${gateway.value}/api/v1/identity/profile/me`, {
        method: 'PUT', headers: { Authorization: `Bearer ${accessToken}` },
        body: { ...profile, photoUrl: profile.photoUrl || null, jobTitle: profile.jobTitle || null }
      }), token, config.public.shellOrigin)
    error.value = ''
    notice.value = copy.value.saved
  } catch (cause) {
    error.value = safeError(cause, locale.value)
    validation.value = fieldErrors(cause, locale.value)
  }
}
</script>
<template>
  <main>
    <h2>{{ copy.title }}</h2>
    <p v-if="!token">{{ copy.signIn }}</p>
    <form v-else @submit.prevent="save">
      <p>{{ email }}</p>
      <img v-if="profile.photoUrl" :src="profile.photoUrl" alt="" class="photo" />
      <label>{{ copy.firstName }}<input v-model="profile.firstName" required maxlength="100" /><small v-if="validation.firstName?.length">{{ validation.firstName[0] }}</small></label>
      <label>{{ copy.lastName }}<input v-model="profile.lastName" required maxlength="100" /><small v-if="validation.lastName?.length">{{ validation.lastName[0] }}</small></label>
      <label>{{ copy.jobTitle }}<input v-model="profile.jobTitle" maxlength="160" /><small v-if="validation.jobTitle?.length">{{ validation.jobTitle[0] }}</small></label>
      <label>{{ copy.photo }}<input v-model="profile.photoUrl" type="url" maxlength="2048" /><small v-if="validation.photoUrl?.length">{{ validation.photoUrl[0] }}</small></label>
      <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">{{ locale === 'th' ? 'ลองใหม่' : 'Retry' }}</button></p><p v-if="notice" role="status">{{ notice }}</p>
      <button type="submit">{{ copy.save }}</button>
    </form>
  </main>
</template>
<style>
:root{font-family:system-ui,sans-serif;color:var(--color-ink);background:var(--color-canvas)}main{max-width:38rem;margin:auto;padding:1rem}form{display:grid;gap:1rem;background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-card);padding:1.5rem}label{display:grid;gap:.3rem}input,button{font:inherit;padding:.65rem;border:1px solid #abb5c5;border-radius:.4rem}button{cursor:pointer;background:var(--color-brand);color:white}.photo{width:5rem;height:5rem;object-fit:cover;border-radius:50%}
</style>
