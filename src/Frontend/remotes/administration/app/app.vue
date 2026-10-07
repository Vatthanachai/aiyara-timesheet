<script setup lang="ts">
type Catalog = { projects: { id: string; name: string }[]; categories: { id: string; name: string }[]; holidays: { id: string; date: string; name: string }[] }
const token = ref('')
const config = useRuntimeConfig()
const gateway = ref('http://localhost:8081')
const locale = ref<'th' | 'en'>('th')
const catalog = ref<Catalog>({ projects: [], categories: [], holidays: [] })
const projectName = ref('')
const categoryName = ref('')
const holidayName = ref('')
const holidayDate = ref('')
const error = ref('')
const notice = ref('')
const policy = reactive({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
const tenantId = ref('')
const lockMonth = ref(new Date().toISOString().slice(0, 7))
onMounted(() => {
  window.addEventListener('message', (event: MessageEvent) => {
    if (event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
    token.value = event.data.token || ''
    gateway.value = event.data.gatewayUrl || gateway.value
    locale.value = event.data.locale === 'en' ? 'en' : 'th'
    tenantId.value = event.data.tenantId || ''
    if (token.value) load()
  })
})
async function api<T>(path: string, options: { method?: string; body?: object } = {}) {
  return await $fetch<T>(`${gateway.value}/api/v1/timesheets${path}`, { ...options,
    headers: { Authorization: `Bearer ${token.value}` } })
}
async function load() {
  try {
    catalog.value = await api<Catalog>('/catalog')
    if (tenantId.value) Object.assign(policy, await $fetch(
      `${gateway.value}/api/v1/identity/tenants/${tenantId.value}/password-policy`,
      { headers: { Authorization: `Bearer ${token.value}` } }))
  }
  catch { error.value = locale.value === 'th' ? 'โหลดข้อมูลไม่สำเร็จ' : 'Could not load settings' }
}
async function add(kind: 'projects' | 'categories' | 'holidays') {
  try {
    const body = kind === 'projects' ? { name: projectName.value } : kind === 'categories' ? { name: categoryName.value } : { date: holidayDate.value, name: holidayName.value }
    await api(`/${kind}`, { method: 'POST', body })
    projectName.value = ''; categoryName.value = ''; holidayName.value = ''
    await load()
  } catch { error.value = locale.value === 'th' ? 'บันทึกไม่สำเร็จ (ต้องเป็นผู้ดูแล)' : 'Save failed (admin role required)' }
}
async function remove(kind: 'projects' | 'categories' | 'holidays', id: string) {
  try { await api(`/${kind}/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = locale.value === 'th' ? 'ลบไม่สำเร็จ' : 'Delete failed' }
}
async function update(kind: 'projects' | 'categories' | 'holidays', item: { id: string; name: string; date?: string }) {
  try { await api(`/${kind}/${item.id}`, { method: 'PUT', body: kind === 'holidays' ? { name: item.name, date: item.date } : { name: item.name } }); await load() }
  catch { error.value = locale.value === 'th' ? 'แก้ไขไม่สำเร็จ' : 'Update failed' }
}
async function savePolicy() {
  try {
    await $fetch(`${gateway.value}/api/v1/auth/tenants/${tenantId.value}/password-policy`, {
      method: 'PUT', body: policy, headers: { Authorization: `Bearer ${token.value}` }
    })
    notice.value = locale.value === 'th' ? 'บันทึกนโยบายแล้ว' : 'Policy saved'
  } catch { error.value = locale.value === 'th' ? 'บันทึกนโยบายไม่สำเร็จ' : 'Could not save policy' }
}
async function lock() {
  try {
    await api(`/months/${lockMonth.value.slice(0, 4)}/${Number(lockMonth.value.slice(5))}/lock`, { method: 'POST' })
    error.value = ''
    notice.value = locale.value === 'th' ? 'ล็อกเดือนแล้ว' : 'Month locked'
  } catch { error.value = locale.value === 'th' ? 'ล็อกเดือนไม่สำเร็จ' : 'Could not lock month' }
}
</script>
<template>
  <main>
    <h2>{{ locale === 'th' ? 'ตั้งค่าองค์กร' : 'Tenant settings' }}</h2>
    <p v-if="!token">{{ locale === 'th' ? 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell' : 'Please sign in through Aiyara Shell' }}</p>
    <template v-else>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <div class="columns">
        <section><h3>{{ locale === 'th' ? 'โครงการ' : 'Projects' }}</h3><form @submit.prevent="add('projects')"><input v-model="projectName" required maxlength="200" :aria-label="locale === 'th' ? 'ชื่อโครงการ' : 'Project name'" /><button>{{ locale === 'th' ? 'เพิ่ม' : 'Add' }}</button></form><ul><li v-for="item in catalog.projects" :key="item.id"><input v-model="item.name" :aria-label="locale === 'th' ? 'ชื่อโครงการ' : 'Project name'" /><button @click="update('projects', item)">{{ locale === 'th' ? 'บันทึก' : 'Save' }}</button><button @click="remove('projects', item.id)">×</button></li></ul></section>
        <section><h3>{{ locale === 'th' ? 'หมวดหมู่' : 'Categories' }}</h3><form @submit.prevent="add('categories')"><input v-model="categoryName" required maxlength="200" :aria-label="locale === 'th' ? 'ชื่อหมวดหมู่' : 'Category name'" /><button>{{ locale === 'th' ? 'เพิ่ม' : 'Add' }}</button></form><ul><li v-for="item in catalog.categories" :key="item.id"><input v-model="item.name" :aria-label="locale === 'th' ? 'ชื่อหมวดหมู่' : 'Category name'" /><button @click="update('categories', item)">{{ locale === 'th' ? 'บันทึก' : 'Save' }}</button><button @click="remove('categories', item.id)">×</button></li></ul></section>
        <section><h3>{{ locale === 'th' ? 'วันหยุด' : 'Holidays' }}</h3><form @submit.prevent="add('holidays')"><input v-model="holidayDate" required type="date" :aria-label="locale === 'th' ? 'วันที่' : 'Date'" /><input v-model="holidayName" required maxlength="200" :aria-label="locale === 'th' ? 'ชื่อวันหยุด' : 'Holiday name'" /><button>{{ locale === 'th' ? 'เพิ่ม' : 'Add' }}</button></form><ul><li v-for="item in catalog.holidays" :key="item.id"><input v-model="item.date" type="date" :aria-label="locale === 'th' ? 'วันที่' : 'Date'" /><input v-model="item.name" :aria-label="locale === 'th' ? 'ชื่อวันหยุด' : 'Holiday name'" /><button @click="update('holidays', item)">{{ locale === 'th' ? 'บันทึก' : 'Save' }}</button><button @click="remove('holidays', item.id)">×</button></li></ul></section>
      </div>
      <section><h3>{{ locale === 'th' ? 'นโยบายรหัสผ่าน' : 'Password policy' }}</h3><form @submit.prevent="savePolicy"><label>Tenant ID <input v-model="tenantId" required /></label><label>{{ locale === 'th' ? 'ความยาวขั้นต่ำ' : 'Minimum length' }} <input v-model.number="policy.minimumLength" type="number" min="8" /></label><label>{{ locale === 'th' ? 'อายุรหัสผ่าน (วัน)' : 'Expiry days' }} <input v-model.number="policy.expiryDays" type="number" min="1" /></label><label><input v-model="policy.requireUppercase" type="checkbox" /> Uppercase</label><label><input v-model="policy.requireLowercase" type="checkbox" /> Lowercase</label><label><input v-model="policy.requireDigit" type="checkbox" /> Digit</label><label><input v-model="policy.requireSymbol" type="checkbox" /> Symbol</label><button>{{ locale === 'th' ? 'บันทึก' : 'Save' }}</button></form></section>
      <section><h3>{{ locale === 'th' ? 'ล็อกเดือน' : 'Lock month' }}</h3><form @submit.prevent="lock"><input v-model="lockMonth" type="month" required /><button>{{ locale === 'th' ? 'ล็อก' : 'Lock' }}</button></form></section>
    </template>
  </main>
</template>
<style>
:root{font-family:system-ui,sans-serif;color:#172033;background:#f8fafc}main{max-width:80rem;margin:auto;padding:1rem}.columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(17rem,1fr));gap:1rem}section{background:white;border:1px solid #d8dee9;border-radius:.75rem;padding:1rem;margin:1rem 0}form{display:flex;flex-wrap:wrap;gap:.5rem}input,button{font:inherit;padding:.5rem;border:1px solid #abb5c5;border-radius:.4rem}button{cursor:pointer;background:#eaf1ff;color:#1241a1}li{margin:.5rem 0}
</style>
