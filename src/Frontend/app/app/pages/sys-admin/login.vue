<script setup lang="ts">
import { safeError } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'
import type { MfaEnrollment, PlatformAdminLoginResult } from '../../composables/useSession'
const { token, role, loginPlatformAdmin, acceptSession } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', heroTitle: 'เข้าสู่ระบบผู้ดูแลระบบ',
    heroSubtitle: 'สำหรับบัญชี sys-admin ไม่ต้องกรอกรหัสองค์กร ต้องใช้รหัสจากแอปยืนยันตัวตนทุกครั้ง',
    signIn: 'เข้าสู่ระบบ', email: 'อีเมล', password: 'รหัสผ่าน', continue: 'ดำเนินการต่อ',
    organizationSignIn: 'เข้าสู่ระบบด้วยรหัสองค์กร', startOver: 'เริ่มใหม่', forgotPassword: 'ลืมรหัสผ่าน?',
    codeTitle: 'ยืนยันตัวตน', codeHint: 'กรอกรหัส 6 หลักจากแอปยืนยันตัวตน', code: 'รหัสยืนยัน',
    useRecovery: 'ใช้รหัสกู้คืนแทน', useAuthenticator: 'ใช้รหัสจากแอปแทน', recoveryCode: 'รหัสกู้คืน',
    enrollTitle: 'ตั้งค่าแอปยืนยันตัวตน',
    enrollHint: 'เพิ่มบัญชีนี้ในแอปยืนยันตัวตน (เช่น Google Authenticator หรือ 1Password) ด้วยคีย์ด้านล่าง แล้วกรอกรหัส 6 หลักที่แอปแสดง',
    setupKey: 'คีย์ตั้งค่า', openInApp: 'เปิดในแอปยืนยันตัวตน',
    codesTitle: 'บันทึกรหัสกู้คืน',
    codesHint: 'ใช้รหัสเหล่านี้แทนแอปเมื่อเข้าถึงแอปไม่ได้ แต่ละรหัสใช้ได้ครั้งเดียว และจะไม่แสดงอีก เก็บไว้ในที่ปลอดภัย',
    savedCodes: 'บันทึกรหัสแล้ว เข้าสู่ระบบต่อ'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', heroTitle: 'System administrator sign in',
    heroSubtitle: 'For sys-admin accounts. No organization code required; an authenticator code is required every time.',
    signIn: 'Sign in', email: 'Email', password: 'Password', continue: 'Continue',
    organizationSignIn: 'Sign in with an organization code', startOver: 'Start over', forgotPassword: 'Forgot password?',
    codeTitle: 'Verify it is you', codeHint: 'Enter the 6-digit code from your authenticator app.', code: 'Authentication code',
    useRecovery: 'Use a recovery code instead', useAuthenticator: 'Use an authenticator code instead', recoveryCode: 'Recovery code',
    enrollTitle: 'Set up your authenticator',
    enrollHint: 'Add this account to an authenticator app (such as Google Authenticator or 1Password) with the key below, then enter the 6-digit code it shows.',
    setupKey: 'Setup key', openInApp: 'Open in authenticator app',
    codesTitle: 'Save your recovery codes',
    codesHint: 'Use one of these instead of your authenticator if you lose access to it. Each works once and they will not be shown again. Keep them somewhere safe.',
    savedCodes: 'I saved these codes, continue'
  }
})

type Step = 'password' | 'code' | 'enroll' | 'codes'
const step = ref<Step>('password')
const email = ref('')
const password = ref('')
const code = ref('')
const useRecovery = ref(false)
const enrollment = ref<MfaEnrollment | null>(null)
const issued = ref<PlatformAdminLoginResult | null>(null)
const error = ref('')
const pending = ref(false)
const codeInput = ref<HTMLInputElement | null>(null)

watchEffect(() => {
  if (token.value && role.value === 'PlatformAdmin') navigateTo('/sys-admin')
})

const errorCode = (cause: unknown) => (cause as { data?: { errorCode?: string } })?.data?.errorCode

async function goTo(next: Step) {
  step.value = next
  code.value = ''
  await nextTick()
  codeInput.value?.focus()
}

async function submit() {
  pending.value = true
  error.value = ''
  try {
    const factor = step.value === 'password' ? {}
      : useRecovery.value ? { recoveryCode: code.value.trim() } : { totpCode: code.value.trim() }
    const result = await loginPlatformAdmin(email.value, password.value, factor)
    if (result.enrollment) {
      enrollment.value = result.enrollment
      await goTo('enroll')
    } else if (result.session && result.recoveryCodes.length) {
      issued.value = result
      step.value = 'codes'
    }
  } catch (cause) {
    if (step.value === 'password' && errorCode(cause) === 'MFAREQUIRED') await goTo('code')
    else error.value = safeError(cause, locale.value)
  } finally { pending.value = false }
}

function finish() {
  if (issued.value?.session) acceptSession(issued.value.session)
}

function startOver() {
  step.value = 'password'
  password.value = ''
  code.value = ''
  error.value = ''
  useRecovery.value = false
  enrollment.value = null
  issued.value = null
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.heroTitle }}</h1>
    <p>{{ text.heroSubtitle }}</p>
  </section>

  <section v-if="step === 'codes'" class="login-card" aria-labelledby="codes-heading">
    <h2 id="codes-heading">{{ text.codesTitle }}</h2>
    <p>{{ text.codesHint }}</p>
    <ol class="recovery-codes" :aria-label="text.codesTitle">
      <li v-for="item in issued?.recoveryCodes" :key="item"><code>{{ item }}</code></li>
    </ol>
    <button class="button" type="button" @click="finish">{{ text.savedCodes }}</button>
  </section>

  <form v-else class="login-card" @submit.prevent="submit">
    <template v-if="step === 'password'">
      <h2>{{ text.signIn }}</h2>
      <label>{{ text.email }}<input v-model="email" required type="email" autocomplete="username" /></label>
      <label>{{ text.password }}<input v-model="password" required type="password" autocomplete="current-password" /></label>
    </template>
    <template v-else-if="step === 'enroll'">
      <h2>{{ text.enrollTitle }}</h2>
      <p>{{ text.enrollHint }}</p>
      <p class="setup-key">{{ text.setupKey }}: <code>{{ enrollment?.secret }}</code></p>
      <a :href="enrollment?.provisioningUri">{{ text.openInApp }}</a>
      <label>{{ text.code }}<input ref="codeInput" v-model="code" required inputmode="numeric" pattern="[0-9 ]{6,7}"
        autocomplete="one-time-code" maxlength="7" /></label>
    </template>
    <template v-else>
      <h2>{{ text.codeTitle }}</h2>
      <p v-if="!useRecovery">{{ text.codeHint }}</p>
      <label v-if="useRecovery">{{ text.recoveryCode }}<input ref="codeInput" v-model="code" required autocomplete="off"
        maxlength="20" /></label>
      <label v-else>{{ text.code }}<input ref="codeInput" v-model="code" required inputmode="numeric" pattern="[0-9 ]{6,7}"
        autocomplete="one-time-code" maxlength="7" /></label>
      <button type="button" class="link-button" @click="useRecovery = !useRecovery; goTo('code')">
        {{ useRecovery ? text.useAuthenticator : text.useRecovery }}
      </button>
    </template>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">{{ step === 'password' ? text.signIn : text.continue }}</button>
    <button v-if="step !== 'password'" type="button" class="link-button" @click="startOver">{{ text.startOver }}</button>
    <template v-else>
      <NuxtLink to="/forgot-password">{{ text.forgotPassword }}</NuxtLink>
      <NuxtLink to="/">{{ text.organizationSignIn }}</NuxtLink>
    </template>
  </form>
</template>

<style scoped>
.setup-key code, .recovery-codes code { font-size: 1rem; letter-spacing: .08em; overflow-wrap: anywhere; }
.recovery-codes { display: grid; gap: .35rem; grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr)); margin: 0; padding-left: 1.4rem; }
</style>
