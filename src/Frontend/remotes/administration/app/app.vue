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
const copy = computed(() => locale.value === 'th' ? {
  title: 'ตั้งค่าองค์กร', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
  projects: 'โครงการ', projectName: 'ชื่อโครงการ', categories: 'หมวดหมู่',
  categoryName: 'ชื่อหมวดหมู่', holidays: 'วันหยุด', holidayName: 'ชื่อวันหยุด',
  date: 'วันที่', add: 'เพิ่ม', save: 'บันทึก', delete: 'ลบ', policy: 'นโยบายรหัสผ่าน',
  tenantId: 'รหัสองค์กร', minimumLength: 'ความยาวขั้นต่ำ', expiryDays: 'อายุรหัสผ่าน (วัน)',
  uppercase: 'ตัวพิมพ์ใหญ่', lowercase: 'ตัวพิมพ์เล็ก', digit: 'ตัวเลข', symbol: 'สัญลักษณ์',
  lockMonth: 'ล็อกเดือน', lock: 'ล็อก', loadedError: 'โหลดข้อมูลไม่สำเร็จ',
  saveError: 'บันทึกไม่สำเร็จ (ต้องเป็นผู้ดูแล)', deleteError: 'ลบไม่สำเร็จ',
  updateError: 'แก้ไขไม่สำเร็จ', policySaved: 'บันทึกนโยบายแล้ว',
  policyError: 'บันทึกนโยบายไม่สำเร็จ', locked: 'ล็อกเดือนแล้ว', lockError: 'ล็อกเดือนไม่สำเร็จ'
} : {
  title: 'Tenant settings', signIn: 'Please sign in through Aiyara Shell',
  projects: 'Projects', projectName: 'Project name', categories: 'Categories',
  categoryName: 'Category name', holidays: 'Holidays', holidayName: 'Holiday name',
  date: 'Date', add: 'Add', save: 'Save', delete: 'Delete', policy: 'Password policy',
  tenantId: 'Tenant ID', minimumLength: 'Minimum length', expiryDays: 'Expiry days',
  uppercase: 'Uppercase', lowercase: 'Lowercase', digit: 'Digit', symbol: 'Symbol',
  lockMonth: 'Lock month', lock: 'Lock', loadedError: 'Could not load settings',
  saveError: 'Save failed (admin role required)', deleteError: 'Delete failed',
  updateError: 'Update failed', policySaved: 'Policy saved',
  policyError: 'Could not save policy', locked: 'Month locked', lockError: 'Could not lock month'
})
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
  catch { error.value = copy.value.loadedError }
}
async function add(kind: 'projects' | 'categories' | 'holidays') {
  try {
    const body = kind === 'projects' ? { name: projectName.value } : kind === 'categories' ? { name: categoryName.value } : { date: holidayDate.value, name: holidayName.value }
    await api(`/${kind}`, { method: 'POST', body })
    projectName.value = ''; categoryName.value = ''; holidayName.value = ''
    await load()
  } catch { error.value = copy.value.saveError }
}
async function remove(kind: 'projects' | 'categories' | 'holidays', id: string) {
  try { await api(`/${kind}/${id}`, { method: 'DELETE' }); await load() }
  catch { error.value = copy.value.deleteError }
}
async function update(kind: 'projects' | 'categories' | 'holidays', item: { id: string; name: string; date?: string }) {
  try { await api(`/${kind}/${item.id}`, { method: 'PUT', body: kind === 'holidays' ? { name: item.name, date: item.date } : { name: item.name } }); await load() }
  catch { error.value = copy.value.updateError }
}
async function savePolicy() {
  try {
    await $fetch(`${gateway.value}/api/v1/auth/tenants/${tenantId.value}/password-policy`, {
      method: 'PUT', body: policy, headers: { Authorization: `Bearer ${token.value}` }
    })
    notice.value = copy.value.policySaved
  } catch { error.value = copy.value.policyError }
}
async function lock() {
  try {
    await api(`/months/${lockMonth.value.slice(0, 4)}/${Number(lockMonth.value.slice(5))}/lock`, { method: 'POST' })
    error.value = ''
    notice.value = copy.value.locked
  } catch { error.value = copy.value.lockError }
}
</script>
<template>
  <main>
    <h2>{{ copy.title }}</h2>
    <p v-if="!token">{{ copy.signIn }}</p>
    <template v-else>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <div class="columns">
        <section><h3>{{ copy.projects }}</h3><form @submit.prevent="add('projects')"><input v-model="projectName" required maxlength="200" :aria-label="copy.projectName" /><button>{{ copy.add }}</button></form><ul><li v-for="item in catalog.projects" :key="item.id"><input v-model="item.name" :aria-label="copy.projectName" /><button @click="update('projects', item)">{{ copy.save }}</button><button :aria-label="`${copy.delete}: ${item.name}`" @click="remove('projects', item.id)">×</button></li></ul></section>
        <section><h3>{{ copy.categories }}</h3><form @submit.prevent="add('categories')"><input v-model="categoryName" required maxlength="200" :aria-label="copy.categoryName" /><button>{{ copy.add }}</button></form><ul><li v-for="item in catalog.categories" :key="item.id"><input v-model="item.name" :aria-label="copy.categoryName" /><button @click="update('categories', item)">{{ copy.save }}</button><button :aria-label="`${copy.delete}: ${item.name}`" @click="remove('categories', item.id)">×</button></li></ul></section>
        <section><h3>{{ copy.holidays }}</h3><form @submit.prevent="add('holidays')"><input v-model="holidayDate" required type="date" :aria-label="copy.date" /><input v-model="holidayName" required maxlength="200" :aria-label="copy.holidayName" /><button>{{ copy.add }}</button></form><ul><li v-for="item in catalog.holidays" :key="item.id"><input v-model="item.date" type="date" :aria-label="copy.date" /><input v-model="item.name" :aria-label="copy.holidayName" /><button @click="update('holidays', item)">{{ copy.save }}</button><button :aria-label="`${copy.delete}: ${item.name}`" @click="remove('holidays', item.id)">×</button></li></ul></section>
      </div>
      <section><h3>{{ copy.policy }}</h3><form @submit.prevent="savePolicy"><label>{{ copy.tenantId }} <input v-model="tenantId" required /></label><label>{{ copy.minimumLength }} <input v-model.number="policy.minimumLength" type="number" min="8" /></label><label>{{ copy.expiryDays }} <input v-model.number="policy.expiryDays" type="number" min="1" /></label><label><input v-model="policy.requireUppercase" type="checkbox" /> {{ copy.uppercase }}</label><label><input v-model="policy.requireLowercase" type="checkbox" /> {{ copy.lowercase }}</label><label><input v-model="policy.requireDigit" type="checkbox" /> {{ copy.digit }}</label><label><input v-model="policy.requireSymbol" type="checkbox" /> {{ copy.symbol }}</label><button>{{ copy.save }}</button></form></section>
      <section><h3>{{ copy.lockMonth }}</h3><form @submit.prevent="lock"><input v-model="lockMonth" type="month" required :aria-label="copy.lockMonth" /><button>{{ copy.lock }}</button></form></section>
    </template>
  </main>
</template>
<style>
:root{font-family:system-ui,sans-serif;color:var(--color-ink);background:var(--color-canvas)}main{max-width:80rem;margin:auto;padding:1rem}.columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(17rem,1fr));gap:1rem}section{background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-card);padding:1rem;margin:1rem 0}form{display:flex;flex-wrap:wrap;gap:.5rem}input,button{font:inherit;padding:.5rem;border:1px solid #abb5c5;border-radius:.4rem}button{cursor:pointer;background:#eaf1ff;color:#1241a1}li{margin:.5rem 0}
</style>
