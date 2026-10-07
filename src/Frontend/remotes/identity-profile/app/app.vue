<script setup lang="ts">
type Profile = { id: string; email: string; firstName: string; lastName: string; photoUrl: string | null; jobTitle: string | null }
const token = ref('')
const config = useRuntimeConfig()
const gateway = ref('http://localhost:8081')
const locale = ref<'th' | 'en'>('th')
const profile = reactive({ firstName: '', lastName: '', photoUrl: '', jobTitle: '' })
const email = ref('')
const error = ref('')
const notice = ref('')
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
    const value = await $fetch<Profile>(`${gateway.value}/api/v1/identity/profile/me`, { headers: { Authorization: `Bearer ${token.value}` } })
    email.value = value.email
    profile.firstName = value.firstName; profile.lastName = value.lastName
    profile.photoUrl = value.photoUrl || ''; profile.jobTitle = value.jobTitle || ''
  } catch { error.value = locale.value === 'th' ? 'โหลดโปรไฟล์ไม่สำเร็จ' : 'Could not load profile' }
}
async function save() {
  try {
    await $fetch(`${gateway.value}/api/v1/identity/profile/me`, {
      method: 'PUT', headers: { Authorization: `Bearer ${token.value}` },
      body: { ...profile, photoUrl: profile.photoUrl || null, jobTitle: profile.jobTitle || null }
    })
    error.value = ''
    notice.value = locale.value === 'th' ? 'บันทึกแล้ว' : 'Saved'
  } catch { error.value = locale.value === 'th' ? 'บันทึกไม่สำเร็จ' : 'Save failed' }
}
</script>
<template>
  <main>
    <h2>{{ locale === 'th' ? 'ข้อมูลส่วนตัว' : 'My profile' }}</h2>
    <p v-if="!token">{{ locale === 'th' ? 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell' : 'Please sign in through Aiyara Shell' }}</p>
    <form v-else @submit.prevent="save">
      <p>{{ email }}</p>
      <img v-if="profile.photoUrl" :src="profile.photoUrl" alt="" class="photo" />
      <label>{{ locale === 'th' ? 'ชื่อ' : 'First name' }}<input v-model="profile.firstName" required maxlength="100" /></label>
      <label>{{ locale === 'th' ? 'นามสกุล' : 'Last name' }}<input v-model="profile.lastName" required maxlength="100" /></label>
      <label>{{ locale === 'th' ? 'ตำแหน่งงาน' : 'Job title' }}<input v-model="profile.jobTitle" maxlength="160" /></label>
      <label>{{ locale === 'th' ? 'รูปภาพ (HTTPS URL)' : 'Photo (HTTPS URL)' }}<input v-model="profile.photoUrl" type="url" maxlength="2048" /></label>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <button type="submit">{{ locale === 'th' ? 'บันทึก' : 'Save' }}</button>
    </form>
  </main>
</template>
<style>
:root{font-family:system-ui,sans-serif;color:#172033;background:#f8fafc}main{max-width:38rem;margin:auto;padding:1rem}form{display:grid;gap:1rem;background:white;border:1px solid #d8dee9;border-radius:.75rem;padding:1.5rem}label{display:grid;gap:.3rem}input,button{font:inherit;padding:.65rem;border:1px solid #abb5c5;border-radius:.4rem}button{cursor:pointer;background:#155eef;color:white}.photo{width:5rem;height:5rem;object-fit:cover;border-radius:50%}
</style>
