<script setup lang="ts">
import { safeError, unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
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
const isLoading = ref(false)
const isSavingPolicy = ref(false)
const isLocking = ref(false)
const policy = reactive({ minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true })
const tenantId = ref('')
const lockMonth = ref(new Date().toISOString().slice(0, 7))
const copy = computed(() => locale.value === 'th' ? {
  title: 'จัดการองค์กร', subtitle: 'จัดระเบียบโครงการ หมวดหมู่ วันหยุด และข้อกำหนดการใช้งานในที่เดียว', signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell',
  projects: 'โครงการ', projectName: 'ชื่อโครงการ', categories: 'หมวดหมู่',
  categoryName: 'ชื่อหมวดหมู่', holidays: 'วันหยุด', holidayName: 'ชื่อวันหยุด',
  date: 'วันที่', add: 'เพิ่มรายการ', save: 'บันทึก', delete: 'ลบ', policy: 'นโยบายรหัสผ่าน',
  tenantId: 'รหัสองค์กร', minimumLength: 'ความยาวขั้นต่ำ', expiryDays: 'อายุรหัสผ่าน (วัน)',
  uppercase: 'ตัวพิมพ์ใหญ่', lowercase: 'ตัวพิมพ์เล็ก', digit: 'ตัวเลข', symbol: 'สัญลักษณ์',
  lockMonth: 'ปิดรอบเดือน', lock: 'ปิดรอบเดือน', loadedError: 'โหลดข้อมูลไม่สำเร็จ',
  saveError: 'บันทึกไม่สำเร็จ (ต้องเป็นผู้ดูแล)', deleteError: 'ลบไม่สำเร็จ',
  updateError: 'แก้ไขไม่สำเร็จ', policySaved: 'บันทึกนโยบายแล้ว',
  policyError: 'บันทึกนโยบายไม่สำเร็จ', locked: 'ปิดรอบเดือนแล้ว', lockError: 'ปิดรอบเดือนไม่สำเร็จ',
  projectHint: 'จัดการโครงการที่ทีมใช้บันทึกเวลาทำงาน', categoryHint: 'กำหนดประเภทงานให้เลือกใน timesheet', holidayHint: 'วันหยุดจะแสดงในปฏิทินของทีม',
  empty: 'ยังไม่มีรายการ', loading: 'กำลังโหลดข้อมูล…', policyHint: 'กำหนดมาตรฐานรหัสผ่านสำหรับสมาชิกในองค์กร',
  lockHint: 'เมื่อปิดรอบแล้ว สมาชิกจะไม่สามารถแก้ไข timesheet ของเดือนนั้นได้',
  countProjects: 'โครงการทั้งหมด', countCategories: 'หมวดหมู่ทั้งหมด', countHolidays: 'วันหยุดที่กำหนด',
  profile: 'องค์กรปัจจุบัน', passwordRequirements: 'ข้อกำหนดรหัสผ่าน', passwordLength: 'จำนวนตัวอักษร',
  savePolicy: 'บันทึกนโยบาย', retry: 'ลองโหลดอีกครั้ง'
} : {
  title: 'Administration', subtitle: 'Manage projects, categories, holidays and access requirements in one place.', signIn: 'Please sign in through Aiyara Shell',
  projects: 'Projects', projectName: 'Project name', categories: 'Categories',
  categoryName: 'Category name', holidays: 'Holidays', holidayName: 'Holiday name',
  date: 'Date', add: 'Add item', save: 'Save', delete: 'Delete', policy: 'Password policy',
  tenantId: 'Tenant ID', minimumLength: 'Minimum length', expiryDays: 'Expiry days',
  uppercase: 'Uppercase', lowercase: 'Lowercase', digit: 'Digit', symbol: 'Symbol',
  lockMonth: 'Close timesheet month', lock: 'Close month', loadedError: 'Could not load settings',
  saveError: 'Save failed (admin role required)', deleteError: 'Delete failed',
  updateError: 'Update failed', policySaved: 'Policy saved',
  policyError: 'Could not save policy', locked: 'Month closed', lockError: 'Could not close month',
  projectHint: 'Manage projects available for time entry', categoryHint: 'Set the work types available in timesheets', holidayHint: 'Holidays appear on your team calendar',
  empty: 'No items yet', loading: 'Loading settings…', policyHint: 'Set password standards for members of this organization',
  lockHint: 'Closing a month prevents members from editing timesheets for that month.',
  countProjects: 'Projects', countCategories: 'Categories', countHolidays: 'Scheduled holidays',
  profile: 'Current organization', passwordRequirements: 'Password requirements', passwordLength: 'Character count',
  savePolicy: 'Save policy', retry: 'Try loading again'
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
  return unwrapResponse(await withRemoteRefresh(accessToken =>
    $fetch<ApiEnvelope<T>>(`${gateway.value}/api/v1/timesheets${path}`, { ...options,
      headers: { Authorization: `Bearer ${accessToken}` } }), token, config.public.shellOrigin))
}
async function load() {
  isLoading.value = true
  error.value = ''
  try {
    catalog.value = await api<Catalog>('/catalog')
    if (tenantId.value) Object.assign(policy, unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<typeof policy>>(`${gateway.value}/api/v1/identity/tenants/${tenantId.value}/password-policy`,
        { headers: { Authorization: `Bearer ${accessToken}` } }), token, config.public.shellOrigin)))
  }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isLoading.value = false }
}
async function add(kind: 'projects' | 'categories' | 'holidays') {
  try {
    const body = kind === 'projects' ? { name: projectName.value } : kind === 'categories' ? { name: categoryName.value } : { date: holidayDate.value, name: holidayName.value }
    await api(`/${kind}`, { method: 'POST', body })
    projectName.value = ''; categoryName.value = ''; holidayName.value = ''
    await load()
  } catch (cause) { error.value = safeError(cause, locale.value) }
}
async function remove(kind: 'projects' | 'categories' | 'holidays', id: string) {
  try { await api(`/${kind}/${id}`, { method: 'DELETE' }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function update(kind: 'projects' | 'categories' | 'holidays', item: { id: string; name: string; date?: string }) {
  try { await api(`/${kind}/${item.id}`, { method: 'PUT', body: kind === 'holidays' ? { name: item.name, date: item.date } : { name: item.name } }); await load() }
  catch (cause) { error.value = safeError(cause, locale.value) }
}
async function savePolicy() {
  isSavingPolicy.value = true
  try {
    await withRemoteRefresh(accessToken =>
      $fetch(`${gateway.value}/api/v1/auth/tenants/${tenantId.value}/password-policy`, {
        method: 'PUT', body: policy, headers: { Authorization: `Bearer ${accessToken}` }
      }), token, config.public.shellOrigin)
    notice.value = copy.value.policySaved
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isSavingPolicy.value = false }
}
async function lock() {
  isLocking.value = true
  try {
    await api(`/months/${lockMonth.value.slice(0, 4)}/${Number(lockMonth.value.slice(5))}/lock`, { method: 'POST' })
    error.value = ''
    notice.value = copy.value.locked
  } catch (cause) { error.value = safeError(cause, locale.value) }
  finally { isLocking.value = false }
}
</script>
<template>
  <main class="admin-page">
    <header class="page-heading">
      <div><p class="eyebrow">{{ locale === 'th' ? 'WORKSPACE SETTINGS' : 'WORKSPACE SETTINGS' }}</p><h1>{{ copy.title }}</h1><p class="subtitle">{{ copy.subtitle }}</p></div>
      <div v-if="token && tenantId" class="tenant-pill"><span class="tenant-dot"></span><span>{{ copy.profile }}</span><strong>{{ tenantId }}</strong></div>
    </header>
    <div v-if="!token" class="empty-state"><span class="empty-icon">⌑</span><h2>{{ copy.signIn }}</h2></div>
    <template v-else>
      <p v-if="error" class="feedback feedback-error" role="alert"><span>{{ error }}</span><button class="button button-subtle" type="button" @click="load">{{ copy.retry }}</button></p>
      <p v-if="notice" class="feedback feedback-success" role="status"><span class="success-dot"></span>{{ notice }}</p>
      <div class="stats-grid" :aria-label="copy.title">
        <article class="stat-card"><span class="stat-icon violet">▦</span><div><strong>{{ catalog.projects.length }}</strong><span>{{ copy.countProjects }}</span></div></article>
        <article class="stat-card"><span class="stat-icon blue">◇</span><div><strong>{{ catalog.categories.length }}</strong><span>{{ copy.countCategories }}</span></div></article>
        <article class="stat-card"><span class="stat-icon orange">▣</span><div><strong>{{ catalog.holidays.length }}</strong><span>{{ copy.countHolidays }}</span></div></article>
      </div>
      <p v-if="isLoading" class="loading-line" role="status">{{ copy.loading }}</p>
      <div class="catalog-grid">
        <section class="panel"><div class="panel-heading"><span class="panel-icon violet">▦</span><div><h2>{{ copy.projects }}</h2><p>{{ copy.projectHint }}</p></div></div><form class="add-form" @submit.prevent="add('projects')"><label class="sr-only" for="new-project">{{ copy.projectName }}</label><input id="new-project" v-model="projectName" required maxlength="200" :placeholder="copy.projectName" /><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.projects.length" class="item-list"><li v-for="item in catalog.projects" :key="item.id" class="item-row"><input v-model="item.name" :aria-label="copy.projectName" /><div class="row-actions"><button class="button button-subtle" @click="update('projects', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('projects', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
        <section class="panel"><div class="panel-heading"><span class="panel-icon blue">◇</span><div><h2>{{ copy.categories }}</h2><p>{{ copy.categoryHint }}</p></div></div><form class="add-form" @submit.prevent="add('categories')"><label class="sr-only" for="new-category">{{ copy.categoryName }}</label><input id="new-category" v-model="categoryName" required maxlength="200" :placeholder="copy.categoryName" /><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.categories.length" class="item-list"><li v-for="item in catalog.categories" :key="item.id" class="item-row"><input v-model="item.name" :aria-label="copy.categoryName" /><div class="row-actions"><button class="button button-subtle" @click="update('categories', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('categories', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
        <section class="panel panel-wide"><div class="panel-heading"><span class="panel-icon orange">▣</span><div><h2>{{ copy.holidays }}</h2><p>{{ copy.holidayHint }}</p></div></div><form class="add-form holiday-form" @submit.prevent="add('holidays')"><label><span>{{ copy.date }}</span><input v-model="holidayDate" required type="date" :aria-label="copy.date" /></label><label class="holiday-name"><span>{{ copy.holidayName }}</span><input v-model="holidayName" required maxlength="200" :placeholder="copy.holidayName" /></label><button class="button button-primary">{{ copy.add }}</button></form><ul v-if="catalog.holidays.length" class="item-list"><li v-for="item in catalog.holidays" :key="item.id" class="item-row holiday-row"><input v-model="item.date" type="date" :aria-label="copy.date" /><input v-model="item.name" :aria-label="copy.holidayName" /><div class="row-actions"><button class="button button-subtle" @click="update('holidays', item)">{{ copy.save }}</button><button class="button button-danger" :aria-label="`${copy.delete}: ${item.name}`" @click="remove('holidays', item.id)">×</button></div></li></ul><p v-else-if="!isLoading" class="empty-list">{{ copy.empty }}</p></section>
      </div>
      <div class="settings-grid">
        <section class="panel policy-panel"><div class="panel-heading"><span class="panel-icon green">⌑</span><div><h2>{{ copy.policy }}</h2><p>{{ copy.policyHint }}</p></div></div><form class="policy-form" @submit.prevent="savePolicy"><div class="policy-fields"><label>{{ copy.minimumLength }}<span class="input-suffix"><input v-model.number="policy.minimumLength" type="number" min="8" max="128" required /><span>{{ locale === 'th' ? 'ตัวอักษร' : 'characters' }}</span></span></label><label>{{ copy.expiryDays }}<span class="input-suffix"><input v-model.number="policy.expiryDays" type="number" min="1" required /><span>{{ locale === 'th' ? 'วัน' : 'days' }}</span></span></label></div><fieldset><legend>{{ copy.passwordRequirements }}</legend><label class="check-card"><input v-model="policy.requireUppercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.uppercase }}</span></label><label class="check-card"><input v-model="policy.requireLowercase" type="checkbox" /><span class="check-mark"></span><span>{{ copy.lowercase }}</span></label><label class="check-card"><input v-model="policy.requireDigit" type="checkbox" /><span class="check-mark"></span><span>{{ copy.digit }}</span></label><label class="check-card"><input v-model="policy.requireSymbol" type="checkbox" /><span class="check-mark"></span><span>{{ copy.symbol }}</span></label></fieldset><button class="button button-primary" :disabled="isSavingPolicy">{{ isSavingPolicy ? copy.loading : copy.savePolicy }}</button></form></section>
        <section class="panel close-panel"><span class="panel-icon orange">◷</span><h2>{{ copy.lockMonth }}</h2><p>{{ copy.lockHint }}</p><form class="close-form" @submit.prevent="lock"><label :aria-label="copy.lockMonth"><input v-model="lockMonth" type="month" required /></label><button class="button button-warning" :disabled="isLocking">{{ isLocking ? copy.loading : copy.lock }}</button></form></section>
      </div>
    </template>
  </main>
</template>
<style>
:root{font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:var(--color-ink,#17233a);background:var(--color-canvas,#f5f7fb)}*{box-sizing:border-box}.admin-page{max-width:76rem;margin:0 auto;padding:clamp(1rem,3vw,2.5rem)}.page-heading{display:flex;justify-content:space-between;align-items:flex-end;gap:1.5rem;margin-bottom:1.75rem}.page-heading h1{font-size:clamp(1.7rem,3vw,2.35rem);letter-spacing:-.04em;line-height:1.15;margin:.2rem 0 .6rem}.eyebrow{font-size:.68rem;font-weight:750;letter-spacing:.14em;color:#74819a;margin:0}.subtitle{color:#6b7890;margin:0;max-width:40rem;line-height:1.55}.tenant-pill{display:flex;align-items:center;gap:.55rem;border:1px solid #e1e7f0;background:#fff;border-radius:999px;padding:.55rem .9rem;color:#74819a;font-size:.78rem;white-space:nowrap}.tenant-pill strong{color:#263753;font-size:.82rem}.tenant-dot,.success-dot{width:.5rem;height:.5rem;border-radius:50%;background:#39a77b}.stats-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem;margin-bottom:1.25rem}.stat-card,.panel{background:#fff;border:1px solid #e6eaf1;border-radius:1rem;box-shadow:0 5px 18px #253d6509}.stat-card{display:flex;align-items:center;gap:.9rem;padding:1rem 1.15rem}.stat-card div{display:grid;gap:.15rem}.stat-card strong{font-size:1.45rem;line-height:1.2;color:#1c2c47}.stat-card div span{font-size:.78rem;color:#758198}.stat-icon,.panel-icon{display:grid;place-items:center;flex:0 0 auto;width:2.4rem;height:2.4rem;border-radius:.75rem;font-weight:700;font-size:1.05rem}.violet{background:#f0ebff;color:#7751c8}.blue{background:#e8f2ff;color:#377ac8}.orange{background:#fff1e6;color:#ca762c}.green{background:#e8f7f0;color:#348c67}.catalog-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}.panel{padding:1.25rem;min-width:0}.panel-wide{grid-column:1/-1}.panel-heading{display:flex;align-items:flex-start;gap:.8rem;margin-bottom:1.1rem}.panel-heading h2,.close-panel h2{font-size:1rem;line-height:1.3;margin:.05rem 0 .3rem;color:#263753}.panel-heading p,.close-panel p{font-size:.78rem;line-height:1.5;color:#8490a4;margin:0}.add-form{display:flex;gap:.55rem;margin-bottom:.85rem}.add-form>input,.add-form label input,.item-row>input,.policy-fields input,.close-form input{min-width:0;width:100%;height:2.55rem;border:1px solid #dfe5ee;border-radius:.65rem;background:#fbfcfe;padding:0 .75rem;color:#263753;font:inherit;font-size:.83rem;outline:none}.add-form input:focus,.item-row input:focus,.policy-fields input:focus,.close-form input:focus{border-color:#8da9ed;box-shadow:0 0 0 3px #4977e51c}.button{border:0;border-radius:.62rem;padding:.62rem .85rem;font:inherit;font-size:.78rem;font-weight:650;cursor:pointer;white-space:nowrap;transition:background .15s ease,transform .15s ease}.button:hover:not(:disabled){transform:translateY(-1px)}.button:disabled{opacity:.58;cursor:wait}.button-primary{background:#355fd4;color:#fff}.button-primary:hover:not(:disabled){background:#294db8}.button-subtle{background:#f0f3f9;color:#475978}.button-danger{background:#fff1f0;color:#be514b;padding:.42rem .72rem;font-size:1.05rem}.button-warning{background:#a6483f;color:#fff}.item-list{list-style:none;margin:0;padding:0;display:grid;gap:.5rem}.item-row{display:flex;align-items:center;gap:.5rem;border:1px solid #edf0f5;background:#fff;border-radius:.7rem;padding:.4rem}.item-row>input{height:2.25rem;background:transparent;border-color:transparent}.row-actions{display:flex;align-items:center;gap:.35rem}.empty-list{margin:.6rem 0 0;padding:.85rem;border-radius:.65rem;background:#f8f9fc;color:#95a0b2;text-align:center;font-size:.8rem}.holiday-form{align-items:flex-end}.holiday-form label{display:grid;gap:.35rem;min-width:9rem;font-size:.72rem;font-weight:600;color:#64718a}.holiday-form .holiday-name{flex:1}.holiday-row>input:first-child{max-width:11rem}.settings-grid{display:grid;grid-template-columns:minmax(0,1.4fr) minmax(16rem,.8fr);gap:1rem;margin-top:1rem}.policy-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.8rem;margin-bottom:1rem}.policy-fields>label{display:grid;gap:.45rem;color:#64718a;font-size:.75rem;font-weight:600}.input-suffix{display:flex;align-items:center;gap:.5rem;border:1px solid #dfe5ee;border-radius:.65rem;padding-right:.75rem;background:#fbfcfe;color:#8893a5;font-size:.7rem}.input-suffix input{border:0;background:transparent;box-shadow:none!important}.policy-form fieldset{padding:0;border:0;margin:0 0 1rem}.policy-form legend{margin-bottom:.6rem;color:#64718a;font-size:.75rem;font-weight:650}.policy-form fieldset{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.5rem}.check-card{position:relative;display:flex;align-items:center;gap:.55rem;border:1px solid #e8ecf2;border-radius:.65rem;padding:.65rem .7rem;color:#52617c;font-size:.75rem;cursor:pointer}.check-card input{position:absolute;opacity:0}.check-mark{width:1rem;height:1rem;border:1px solid #c9d1df;border-radius:.3rem;background:#fff}.check-card input:checked+.check-mark{border-color:#4266d5;background:#4266d5;box-shadow:inset 0 0 0 3px #fff}.check-card:focus-within{outline:3px solid #4977e51c}.close-panel{align-self:start}.close-panel>.panel-icon{margin-bottom:.8rem}.close-form{display:flex;align-items:center;gap:.55rem;margin-top:1.15rem}.close-form label{flex:1}.feedback{display:flex;align-items:center;justify-content:space-between;gap:.75rem;padding:.8rem 1rem;border-radius:.7rem;margin:0 0 1rem;font-size:.82rem}.feedback-error{background:#fff0ef;color:#a53d38}.feedback-success{justify-content:flex-start;background:#eaf8f1;color:#287751}.loading-line{color:#77849a;font-size:.8rem;margin:.2rem 0 .75rem}.empty-state{display:grid;place-items:center;min-height:15rem;border:1px dashed #d9dfeb;border-radius:1rem;background:#fff;color:#74819a;text-align:center}.empty-state h2{font-size:1rem;font-weight:550}.empty-icon{font-size:2rem;color:#a8b2c4}.sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}@media(max-width:760px){.admin-page{padding:1rem}.page-heading{align-items:flex-start;flex-direction:column;margin-bottom:1.2rem}.stats-grid{gap:.6rem}.stat-card{padding:.8rem;gap:.55rem}.stat-icon{width:2rem;height:2rem}.stat-card strong{font-size:1.2rem}.stat-card div span{font-size:.68rem}.settings-grid{grid-template-columns:1fr}}@media(max-width:560px){.catalog-grid{grid-template-columns:1fr}.panel-wide{grid-column:auto}.stats-grid{grid-template-columns:1fr}.stat-card{padding:.7rem .9rem}.stat-card div{display:flex;align-items:baseline;justify-content:space-between;flex:1}.holiday-form{display:grid;grid-template-columns:1fr}.holiday-form label,.holiday-form .holiday-name{min-width:0}.holiday-row{flex-wrap:wrap}.holiday-row>input:first-child{max-width:none;width:100%}.holiday-row>input:nth-child(2){flex:1}.policy-fields{grid-template-columns:1fr}.tenant-pill{white-space:normal}.page-heading h1{font-size:1.8rem}}
</style>
