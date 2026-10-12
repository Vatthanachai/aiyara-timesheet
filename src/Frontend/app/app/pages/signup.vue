<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'สมัครใช้งานองค์กร',
    subtitle: 'สร้างพื้นที่ทำงานและบัญชีผู้ดูแลองค์กร ระบบจะส่งรหัสเปิดใช้งานไปยังอีเมลที่ระบุ',
    createdTitle: 'สร้างองค์กรเรียบร้อยแล้ว',
    activationSending: 'ระบบกำลังส่งรหัสเปิดใช้งานไปที่ {email} ตรวจสอบกล่องจดหมายและโฟลเดอร์สแปม',
    passwordRequirement: 'รหัสผ่านใหม่ต้องยาวอย่างน้อย 12 ตัว มีตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก ตัวเลข และสัญลักษณ์',
    resending: 'กำลังขอรหัส…', resend: 'ยังไม่ได้รับอีเมล? ส่งรหัสอีกครั้ง',
    organizationCodeLabel: 'รหัสองค์กรสำหรับเข้าสู่ระบบ', copied: 'คัดลอกแล้ว', copyCode: 'คัดลอกรหัส',
    useCodeHint: 'ใช้รหัสนี้ อีเมลผู้ดูแล และรหัสผ่านใหม่ที่ตั้งในหน้าเปิดใช้งานเพื่อเข้าสู่ระบบ',
    goToActivation: 'ไปหน้าเปิดใช้งานบัญชี',
    organizationName: 'ชื่อองค์กร', organizationUrlName: 'ชื่อ URL องค์กร',
    organizationUrlHint: 'ใช้อักษร a-z ตัวเลข และขีดกลาง เริ่มด้วยตัวอักษรหรือตัวเลข',
    organizationAdminEmail: 'อีเมลผู้ดูแลองค์กร',
    afterSignUpHint: 'หลังสมัคร ให้เปิดอีเมลและใช้รหัสชั่วคราวในหน้าเปิดใช้งานเพื่อตั้งรหัสผ่าน',
    creating: 'กำลังสร้างองค์กร…', signUp: 'สมัครองค์กร', backToSignIn: 'กลับไปเข้าสู่ระบบ',
    slugConflict: 'ชื่อ URL องค์กรนี้ถูกใช้แล้ว กรุณาเปลี่ยนชื่อ URL แล้วลองอีกครั้ง',
    createError: 'สมัครองค์กรไม่สำเร็จ กรุณาตรวจสอบข้อมูลแล้วลองอีกครั้ง',
    resendMessage: 'หากบัญชียังรอเปิดใช้งาน ระบบจะส่งรหัสใหม่ไปยังอีเมลนี้',
    resendError: 'ขอรหัสใหม่ไม่สำเร็จ กรุณารอสักครู่แล้วลองอีกครั้ง'
  },
  en: {
    title: 'Create your organization',
    subtitle: 'Set up a workspace and its administrator account. We’ll email an activation code to the address you provide.',
    createdTitle: 'Organization created',
    activationSending: 'An activation code is being sent to {email}. Check your inbox and spam folder.',
    passwordRequirement: 'Your new password must be at least 12 characters and include uppercase and lowercase letters, a number, and a symbol.',
    resending: 'Requesting code…', resend: 'Didn’t receive the email? Send another code',
    organizationCodeLabel: 'Organization code for sign in', copied: 'Copied', copyCode: 'Copy code',
    useCodeHint: 'Use this code, the administrator email, and the password you set during activation to sign in.',
    goToActivation: 'Go to account activation',
    organizationName: 'Organization name', organizationUrlName: 'Organization URL name',
    organizationUrlHint: 'Use lowercase a-z, numbers, and hyphens. Start with a letter or number.',
    organizationAdminEmail: 'Organization administrator email',
    afterSignUpHint: 'After sign up, use the one-time code sent by email on the activation page to set a password.',
    creating: 'Creating organization…', signUp: 'Sign up organization', backToSignIn: 'Back to sign in',
    slugConflict: 'That organization URL is already in use. Choose another and try again.',
    createError: 'We could not create the organization. Check the details and try again.',
    resendMessage: 'If the account is still awaiting activation, a new code will be sent to this email.',
    resendError: 'We could not request a new code. Wait a moment and try again.'
  }
})

const name = ref('')
const slug = ref('')
const adminEmail = ref('')
const error = ref('')
const pending = ref(false)
const result = ref<{ tenantId: string; organizationCode: string; adminEmail: string } | null>(null)
const copied = ref(false)
const resendPending = ref(false)
const resendMessage = ref('')
const resendError = ref('')
const config = useRuntimeConfig()

async function signUp() {
  error.value = ''
  copied.value = false
  pending.value = true

  try {
    const created = unwrapResponse(await $fetch<ApiEnvelope<{
      tenantId: string
      organizationCode: string
      adminEmail?: string
    }>>(`${config.public.gatewayUrl}/api/v1/tenants`, {
      method: 'POST',
      body: {
        name: name.value.trim(),
        slug: slug.value.trim().toLowerCase(),
        adminEmail: adminEmail.value.trim()
      }
    }))

    // Do not persist or render the one-time onboarding capability in the UI.
    result.value = {
      tenantId: created.tenantId,
      organizationCode: created.organizationCode,
      adminEmail: adminEmail.value.trim()
    }
  } catch (cause: unknown) {
    const status = (cause as { status?: number; statusCode?: number })?.statusCode
      ?? (cause as { status?: number })?.status
    error.value = status === 409 ? text.value.slugConflict : text.value.createError
  } finally {
    pending.value = false
  }
}

async function resendActivation() {
  if (!result.value) return
  resendPending.value = true
  resendMessage.value = ''
  resendError.value = ''
  try {
    await $fetch(`${config.public.gatewayUrl}/api/v1/auth/activation/request`, {
      method: 'POST',
      body: { tenantId: result.value.tenantId, email: result.value.adminEmail }
    })
    resendMessage.value = text.value.resendMessage
  } catch {
    resendError.value = text.value.resendError
  } finally {
    resendPending.value = false
  }
}

async function copyOrganizationCode() {
  if (!result.value) return
  await navigator.clipboard.writeText(result.value.organizationCode)
  copied.value = true
}
</script>

<template>
  <section class="hero">
    <p class="eyebrow">Aiyara Timesheet</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <section v-if="result" class="login-card signup-result" role="status">
    <h2>{{ text.createdTitle }}</h2>
    <p>{{ text.activationSending.replace('{email}', result.adminEmail) }}</p>
    <p>{{ text.passwordRequirement }}</p>
    <button class="button button-secondary" type="button" :disabled="resendPending" @click="resendActivation">
      {{ resendPending ? text.resending : text.resend }}
    </button>
    <p v-if="resendMessage" role="status">{{ resendMessage }}</p>
    <p v-if="resendError" role="alert">{{ resendError }}</p>
    <div class="organization-code">
      <span>{{ text.organizationCodeLabel }}</span>
      <strong>{{ result.organizationCode }}</strong>
      <button class="button button-secondary" type="button" @click="copyOrganizationCode">
        {{ copied ? text.copied : text.copyCode }}
      </button>
    </div>
    <p>{{ text.useCodeHint }}</p>
    <NuxtLink class="button" to="/activate">
      {{ text.goToActivation }}
    </NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="signUp">
    <label>
      {{ text.organizationName }}
      <input v-model="name" required maxlength="200" autocomplete="organization" />
    </label>
    <label>
      {{ text.organizationUrlName }}
      <input v-model="slug" required minlength="3" maxlength="80" pattern="[a-z0-9][a-z0-9-]{2,79}"
        autocapitalize="none" autocomplete="url" placeholder="acme-studio" />
      <small>{{ text.organizationUrlHint }}</small>
    </label>
    <label>
      {{ text.organizationAdminEmail }}
      <input v-model="adminEmail" required type="email" autocomplete="email" />
    </label>
    <p>{{ text.afterSignUpHint }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending ? text.creating : text.signUp }}
    </button>
    <NuxtLink to="/">{{ text.backToSignIn }}</NuxtLink>
  </form>
</template>

<style scoped>
.organization-code {
  align-items: center;
  background: var(--color-canvas);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-card);
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  padding: 1rem;
}

.organization-code span { color: var(--color-muted); flex-basis: 100%; }
.organization-code strong { font-size: 1.75rem; letter-spacing: 0.12em; }
.button-secondary { border: 0; cursor: pointer; font: inherit; }
small { color: var(--color-muted); line-height: 1.5; }
</style>
