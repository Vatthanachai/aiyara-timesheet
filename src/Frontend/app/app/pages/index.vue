<script setup lang="ts">
import { fieldErrors, safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
import type { SignInOrganization } from '../composables/useSession'
import DayRuler, { type RulerBand } from '../../../shared/components/DayRuler.vue'
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
      <p class="welcome-product">Aiyara Timesheet</p>
      <!-- Thai has no spaces to break at, so each phrase is kept on one line. -->
      <!-- The space goes between the spans: Vue trims one left at the end of a span's text. -->
      <h1 id="welcome-title"><template v-for="(phrase, index) in text.heroTitle.split('|')" :key="phrase"><span>{{ phrase }}</span>{{ index ? '' : ' ' }}</template></h1>
      <p class="welcome-lede">{{ text.heroSubtitle }}</p>
    </section>

    <DayRuler class="welcome-ruler" :work-start="540" :work-end="1080" :break-start="720" :break-end="780"
      :bands="sampleBands" :hour-label="sampleHour" animate />

    <div class="welcome-access">
      <section v-if="organizations.length" class="login-card" aria-labelledby="choose-organization">
        <h2 id="choose-organization">{{ text.chooseOrganization }}</h2>
        <p>{{ text.chooseOrganizationHint }}</p>
        <button v-for="organization in organizations" :key="organization.organizationCode"
          class="button organization-choice" type="button" :disabled="pending" @click="chooseOrganization(organization)">
          {{ organization.name }} ({{ organization.organizationCode }})
        </button>
        <p v-if="error" role="alert" class="form-error">{{ error }}</p>
        <button type="button" class="link-button" @click="backToSignIn">{{ text.back }}</button>
      </section>

      <form v-else class="login-card" @submit.prevent="signInByEmail">
        <h2>{{ text.signIn }}</h2>
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

      <nav class="welcome-new" :aria-label="text.newHere">
        <h2>{{ text.newHere }}</h2>
        <NuxtLink to="/signup">{{ text.signUp }}</NuxtLink>
        <NuxtLink to="/accept-invitation">{{ text.acceptInvitation }}</NuxtLink>
        <NuxtLink to="/activate">{{ text.activateAccount }}</NuxtLink>
      </nav>
    </div>

    <p class="welcome-foot"><NuxtLink to="/sys-admin/login">{{ text.adminSignIn }}</NuxtLink></p>
  </div>
</template>

<style scoped>
.welcome {
  align-items: start; display: grid; gap: 2rem 4rem;
  grid-template-areas: "intro access" "ruler access" "foot foot";
  grid-template-columns: minmax(0, 1.35fr) minmax(18rem, 25rem);
}
.welcome-intro { grid-area: intro; }
.welcome-ruler { grid-area: ruler; margin-top: 1rem; }
.welcome-access { display: grid; gap: 1.75rem; grid-area: access; }
.welcome-foot { border-top: 1px solid var(--color-border); font-size: .9rem; grid-area: foot; margin: 1rem 0 0; padding-top: 1.25rem; }
.welcome-product { color: var(--color-brand); font-family: var(--font-display); font-size: 1.05rem; font-weight: 600; margin: 0; }
.welcome h1 { font-family: var(--font-display); font-size: clamp(2.4rem, 6.5vw, 4.5rem); font-weight: 600; letter-spacing: -.015em; line-height: 1.1; margin: .6rem 0 1.25rem; }
.welcome h1 span { display: inline-block; }
.welcome-lede { font-size: 1.125rem; margin: 0; max-width: 34rem; }
.login-card { box-shadow: 0 1px 0 var(--color-border), 0 1.25rem 2.5rem -1.5rem rgb(23 32 51 / 22%); gap: .45rem; max-width: none; padding: 1.75rem; }
.login-card h2 { font-family: var(--font-display); font-size: 1.5rem; font-weight: 600; margin: 0 0 .6rem; }
.login-card p { margin: 0 0 .5rem; }
.login-card label { font-weight: 600; }
.login-card input { margin-bottom: .6rem; }
.login-card input:focus-visible { border-color: var(--color-brand); outline: 2px solid var(--color-brand); outline-offset: 0; }
.login-card small { color: #8a2424; margin: -.4rem 0 .5rem; }
.login-card .button { border: 0; font: inherit; font-weight: 600; margin-top: .4rem; padding: .85rem 1rem; }
.login-card .button:hover:not(:disabled) { background: var(--color-brand-strong); }
.login-card .button:disabled { cursor: wait; opacity: .65; }
.password-label { align-items: baseline; display: flex; gap: 1rem; justify-content: space-between; }
.password-label a { color: var(--color-brand); font-size: .9rem; }
.form-error { color: #8a2424; }
.organization-choice { text-align: left; }
.welcome-new { display: grid; gap: .5rem; padding: 0 .25rem; }
.welcome-new h2 { font-size: 1rem; margin: 0 0 .15rem; }
.welcome-new a { color: var(--color-brand); justify-self: start; }
.admin-home { display: grid; gap: .5rem; justify-items: start; max-width: 40rem; }
.admin-home h1 { font-family: var(--font-display); font-weight: 600; }
.admin-home p { font-size: 1.125rem; margin: 0 0 1rem; }
@media (max-width: 52rem) {
  .welcome { gap: 1.75rem; grid-template-areas: "intro" "access" "ruler" "foot"; grid-template-columns: minmax(0, 1fr); }
}
</style>
