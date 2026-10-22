<script setup lang="ts">
import { fieldErrors, safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
import type { SignInOrganization } from '../composables/useSession'
import DayRuler, { type RulerBand } from '../../../shared/components/DayRuler.vue'
import { DEFAULT_FORMATS, formatDate } from '../../../shared/date-format'
const { token, role, login, signIn } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    heroTitle: 'วันทำงานของคุณ|ทีละชั่วโมง',
    heroSubtitle: 'บันทึกเวลาตามโครงการ วางแผนวันลา และรับรายงานประจำเดือนที่องค์กรลงนามได้',
    signIn: 'เข้าสู่ระบบ', email: 'อีเมล', password: 'รหัสผ่าน', forgotPassword: 'ลืมรหัสผ่าน?',
    chooseOrganization: 'เลือกองค์กร', chooseOrganizationHint: 'บัญชีนี้อยู่ในหลายองค์กร เลือกองค์กรที่ต้องการเข้าใช้งาน',
    back: 'ย้อนกลับ', newHere: 'เพิ่งเริ่มใช้งาน?', signUp: 'สมัครใช้งานให้องค์กรของคุณ',
    acceptInvitation: 'เข้าร่วมด้วยรหัสคำเชิญ', activateAccount: 'เปิดใช้งานบัญชีที่ได้รับทางอีเมล',
    signUpHint: 'ตั้งค่า Aiyara Timesheet ให้ทีมของคุณ', acceptInvitationHint: 'ผู้ดูแลส่งรหัสคำเชิญให้คุณแล้ว',
    activateAccountHint: 'บัญชีใหม่ของคุณรออยู่ในอีเมล',
    adminSignIn: 'เข้าสู่ระบบผู้ดูแลระบบ', adminHomeTitle: 'ผู้ดูแลระบบ',
    adminHomeHint: 'ดูภาพรวมทุกองค์กร จัดการองค์กรและผู้ดูแล และตรวจประวัติการดำเนินการของระบบ', openAdmin: 'เปิดหน้าผู้ดูแลระบบ',
    sampleStandup: 'ประชุมเช้า', sampleReview: 'รีวิวงานออกแบบ', sampleWorkshop: 'เวิร์กช็อปกับลูกค้า', samplePlanning: 'วางแผนสปรินต์'
  },
  en: {
    heroTitle: 'Your working day,|hour by hour.',
    heroSubtitle: 'Record time against projects, plan leave, and get monthly reports your organization can sign.',
    signIn: 'Sign in', email: 'Email', password: 'Password', forgotPassword: 'Forgot password?',
    chooseOrganization: 'Choose an organization', chooseOrganizationHint: 'This account belongs to several organizations. Choose the one to open.',
    back: 'Back', newHere: 'New here?', signUp: 'Sign up your organization',
    acceptInvitation: 'Join with an invitation code', activateAccount: 'Activate an account from your email',
    signUpHint: 'Set up Aiyara Timesheet for your team.', acceptInvitationHint: 'Your administrator sent you a code.',
    activateAccountHint: 'Your new account is waiting in your email.',
    adminSignIn: 'System administrator sign in', adminHomeTitle: 'System admin',
    adminHomeHint: 'See every organization at a glance, manage organizations and administrators, and review the platform audit log.', openAdmin: 'Open System admin',
    sampleStandup: 'Standup', sampleReview: 'Design review', sampleWorkshop: 'Client workshop', samplePlanning: 'Sprint planning'
  }
})
// An illustrative day for the signed-out page; times are minutes after midnight.
const sampleBands = computed<RulerBand[]>(() => [
  { start: 540, end: 600, label: text.value.sampleStandup },
  { start: 600, end: 720, label: text.value.sampleReview },
  { start: 780, end: 930, label: text.value.sampleWorkshop },
  { start: 930, end: 1050, label: text.value.samplePlanning }
])
const sampleHour = (hour: number) => String(hour).padStart(2, '0')
const ways = computed(() => [
  { to: '/signup', label: text.value.signUp, hint: text.value.signUpHint },
  { to: '/accept-invitation', label: text.value.acceptInvitation, hint: text.value.acceptInvitationHint },
  { to: '/activate', label: text.value.activateAccount, hint: text.value.activateAccountHint }
])
// Today's date, printed on the sign-in card as on a time card; set in the browser, whose day it is.
const today = ref('')
onMounted(() => { today.value = new Date().toLocaleDateString('sv-SE') })
const todayText = computed(() => today.value && formatDate(today.value, DEFAULT_FORMATS, locale.value, { weekday: true }))
const email = ref('')
const password = ref('')
const organizations = ref<SignInOrganization[]>([])
const error = ref('')
const validation = ref<Record<string, string[]>>({})
const pending = ref(false)

async function submit(action: () => Promise<unknown>) {
  pending.value = true
  error.value = ''
  validation.value = {}
  try { await action() }
  catch (cause) {
    error.value = safeError(cause, locale.value)
    validation.value = fieldErrors(cause, locale.value)
  }
  finally { pending.value = false }
}

function signInByEmail() {
  return submit(async () => {
    const result = await signIn(email.value, password.value)
    organizations.value = result.session ? [] : result.organizations
    if (result.session) password.value = ''
  })
}

function chooseOrganization(organization: SignInOrganization) {
  return submit(async () => {
    await login(organization.organizationCode, email.value, password.value)
    password.value = ''
    organizations.value = []
  })
}

function backToSignIn() {
  organizations.value = []
  password.value = ''
  error.value = ''
  validation.value = {}
}
</script>

<template>
  <HomeToday v-if="token && role !== 'PlatformAdmin'" />

  <section v-else-if="token" class="admin-home" aria-labelledby="admin-home">
    <h1 id="admin-home">{{ text.adminHomeTitle }}</h1>
    <p>{{ text.adminHomeHint }}</p>
    <NuxtLink class="button" to="/sys-admin">{{ text.openAdmin }}</NuxtLink>
  </section>

  <div v-else class="welcome">
    <section class="welcome-intro" aria-labelledby="welcome-title">
      <!-- Thai has no spaces to break at, so each phrase is kept on one line. -->
      <!-- The space goes between the spans: Vue trims one left at the end of a span's text. -->
      <h1 id="welcome-title"><template v-for="(phrase, index) in text.heroTitle.split('|')" :key="phrase"><span>{{ phrase }}</span>{{ index ? '' : ' ' }}</template></h1>
      <p class="welcome-lede">{{ text.heroSubtitle }}</p>
    </section>

    <DayRuler class="welcome-ruler" :work-start="540" :work-end="1080" :break-start="720" :break-end="780"
      :bands="sampleBands" :hour-label="sampleHour" animate />

    <div class="welcome-access">
      <section v-if="organizations.length" class="login-card punch-card" aria-labelledby="choose-organization">
        <div class="card-head">
          <h2 id="choose-organization">{{ text.chooseOrganization }}</h2>
          <time v-if="today" :datetime="today">{{ todayText }}</time>
        </div>
        <p>{{ text.chooseOrganizationHint }}</p>
        <button v-for="organization in organizations" :key="organization.organizationCode"
          class="button organization-choice" type="button" :disabled="pending" @click="chooseOrganization(organization)">
          {{ organization.name }} ({{ organization.organizationCode }})
        </button>
        <p v-if="error" role="alert" class="form-error">{{ error }}</p>
        <button type="button" class="link-button" @click="backToSignIn">{{ text.back }}</button>
      </section>

      <form v-else class="login-card punch-card" @submit.prevent="signInByEmail">
        <div class="card-head">
          <h2>{{ text.signIn }}</h2>
          <time v-if="today" :datetime="today">{{ todayText }}</time>
        </div>
        <label for="sign-in-email">{{ text.email }}</label>
        <input id="sign-in-email" v-model="email" required type="email" autocomplete="username"
          :aria-invalid="validation.email?.length ? 'true' : undefined"
          :aria-describedby="validation.email?.length ? 'sign-in-email-error' : undefined" />
        <small v-if="validation.email?.length" id="sign-in-email-error">{{ validation.email[0] }}</small>
        <div class="password-label">
          <label for="sign-in-password">{{ text.password }}</label>
          <NuxtLink to="/forgot-password">{{ text.forgotPassword }}</NuxtLink>
        </div>
        <input id="sign-in-password" v-model="password" required type="password" autocomplete="current-password"
          :aria-invalid="validation.password?.length ? 'true' : undefined"
          :aria-describedby="validation.password?.length ? 'sign-in-password-error' : undefined" />
        <small v-if="validation.password?.length" id="sign-in-password-error">{{ validation.password[0] }}</small>
        <p v-if="error" role="alert" class="form-error">{{ error }}</p>
        <button class="button" type="submit" :disabled="pending">{{ text.signIn }}</button>
      </form>

      <p class="welcome-admin"><NuxtLink to="/sys-admin/login">{{ text.adminSignIn }}</NuxtLink></p>

    </div>

    <nav class="welcome-new" :aria-label="text.newHere">
      <h2>{{ text.newHere }}</h2>
      <ul>
        <li v-for="way in ways" :key="way.to">
          <NuxtLink :to="way.to">{{ way.label }}</NuxtLink>
          <span>{{ way.hint }}</span>
        </li>
      </ul>
    </nav>
  </div>
</template>

<style scoped>
.welcome {
  align-content: center; align-items: end; display: grid; gap: clamp(1.25rem, 4vh, 2.75rem) 4rem;
  grid-template-areas: "intro access" "ruler access" "new new";
  grid-template-columns: minmax(0, 1fr) minmax(18rem, 24rem); min-height: var(--shell-fill);
}
/* The headline is sized from its own column, so each phrase keeps to one line at any width. */
.welcome-intro { container-type: inline-size; grid-area: intro; padding-bottom: .5rem; }
/* The sample day sits under the welcome, beside the card. */
.welcome-ruler { align-self: start; grid-area: ruler; padding-bottom: 0; }
.welcome-access { align-self: center; display: grid; gap: .9rem; grid-area: access; }
/* The other way to sign in sits under the card it belongs with. */
.welcome-admin { font-size: .9rem; margin: 0; text-align: center; }
.welcome h1 { font-family: var(--font-display); font-size: clamp(1.75rem, min(11.8cqi, 8.5vh), 5.25rem); font-weight: 600; letter-spacing: -.015em; line-height: 1.08; margin: 0 0 1.25rem; }
/* The longest phrase, “Your working day,”, is about 8.1em wide; 11.8cqi keeps it inside the column. */
.welcome h1 span { display: inline-block; white-space: nowrap; }
.welcome-lede { font-size: 1.125rem; margin: 0; max-width: 34rem; }
.login-card { gap: .45rem; max-width: none; padding: 2.4rem 1.75rem 1.75rem; }
.card-head { align-items: baseline; display: flex; flex-wrap: wrap; gap: .25rem 1rem; justify-content: space-between; margin-bottom: .6rem; }
.card-head h2 { font-family: var(--font-display); font-size: 1.5rem; font-weight: 600; margin: 0; }
.card-head time { color: var(--color-muted); font-family: var(--font-display); font-size: .95rem; font-variant-numeric: tabular-nums; font-weight: 500; }
.login-card p { margin: 0 0 .5rem; }
.login-card label { font-weight: 600; }
.login-card input { margin-bottom: .6rem; }
.login-card input:focus-visible { border-color: var(--color-brand); outline: 2px solid var(--color-brand); outline-offset: 0; }
.login-card small { color: var(--color-danger); margin: -.4rem 0 .5rem; }
.login-card .button { border: 0; font: inherit; font-weight: 600; margin-top: .4rem; padding: .85rem 1rem; }
.login-card .button:hover:not(:disabled) { background: var(--color-brand-strong); }
.login-card .button:disabled { cursor: wait; opacity: .65; }
.password-label { align-items: baseline; display: flex; gap: 1rem; justify-content: space-between; }
.password-label a { color: var(--color-brand); font-size: .9rem; }
.form-error { color: var(--color-danger); }
.organization-choice { text-align: left; }
/* Three doors in, side by side, each saying who it is for. */
.welcome-new { border-top: 1px solid var(--color-border); display: grid; gap: 1rem 2.5rem; grid-area: new; grid-template-columns: 10rem minmax(0, 1fr); padding-top: clamp(1rem, 3vh, 1.75rem); }
.welcome-new h2 { font-family: var(--font-display); font-size: 1.15rem; font-weight: 600; margin: 0; }
.welcome-new ul { display: grid; gap: 1.25rem 2.5rem; grid-template-columns: repeat(3, minmax(0, 1fr)); list-style: none; margin: 0; padding: 0; }
.welcome-new li { align-content: start; display: grid; gap: .3rem; justify-items: start; }
.welcome-new a { color: var(--color-brand); font-weight: 600; text-underline-offset: .2em; }
.welcome-new span { color: var(--color-muted); font-size: .95rem; line-height: 1.5; }
.admin-home { display: grid; gap: .5rem; justify-items: start; max-width: 40rem; }
.admin-home h1 { font-family: var(--font-display); font-weight: 600; }
.admin-home p { font-size: 1.125rem; margin: 0 0 1rem; }
/* Short laptop screens: a tighter card keeps the whole page on one screen. */
@media (min-width: 46.01rem) and (max-height: 50rem) {
  .login-card { padding: 2.1rem 1.5rem 1.4rem; }
  .card-head { margin-bottom: .3rem; }
  .login-card input { margin-bottom: .3rem; }
  .login-card .button { padding: .75rem 1rem; }
}
@media (min-width: 46.01rem) and (max-height: 46rem) {
  .welcome-ruler :deep(.ruler-hours) { height: 2rem; }
  .welcome-ruler :deep(.ruler-track) { height: 2.9rem; }
  .welcome h1 { margin-bottom: .9rem; }
}
@media (max-width: 64rem) {
  .welcome { column-gap: 2.5rem; grid-template-columns: minmax(0, 1fr) minmax(17rem, 21rem); }
}
@media (max-width: 60rem) {
  .welcome-new { column-gap: 1.5rem; grid-template-columns: 6.5rem minmax(0, 1fr); }
  .welcome-new ul { column-gap: 1.5rem; }
}
/* Tablets and phones: one column, with the card kept to a form's width. */
@media (max-width: 46rem) {
  .welcome { align-items: start; gap: 2rem; grid-template-areas: "intro" "access" "ruler" "new"; grid-template-columns: minmax(0, 1fr); min-height: 0; }
  .welcome-access { max-width: 28rem; width: 100%; }
  .welcome-new { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 40rem) {
  .welcome { gap: 1.5rem; }
  .welcome h1 { margin-bottom: .9rem; }
  .welcome-lede { font-size: 1rem; }
  .login-card { padding: 2.1rem 1.25rem 1.25rem; }
  .welcome-new ul { grid-template-columns: minmax(0, 1fr); }
}
</style>
