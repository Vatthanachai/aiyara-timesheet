<script setup lang="ts">
import { unwrapResponse, type ApiEnvelope } from '../../../shared/api-response'
const { locale } = useLocale()
const config = useRuntimeConfig()

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
    if (status === 409) {
      error.value = locale.value === 'th'
        ? 'ชื่อ URL องค์กรนี้ถูกใช้แล้ว กรุณาเปลี่ยนชื่อ URL แล้วลองอีกครั้ง'
        : 'That organization URL is already in use. Choose another and try again.'
    } else {
      error.value = locale.value === 'th'
        ? 'สมัครองค์กรไม่สำเร็จ กรุณาตรวจสอบข้อมูลแล้วลองอีกครั้ง'
        : 'We could not create the organization. Check the details and try again.'
    }
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
    resendMessage.value = locale.value === 'th'
      ? 'หากบัญชียังรอเปิดใช้งาน ระบบจะส่งรหัสใหม่ไปยังอีเมลนี้'
      : 'If the account is still awaiting activation, a new code will be sent to this email.'
  } catch {
    resendError.value = locale.value === 'th'
      ? 'ขอรหัสใหม่ไม่สำเร็จ กรุณารอสักครู่แล้วลองอีกครั้ง'
      : 'We could not request a new code. Wait a moment and try again.'
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
    <h1>{{ locale === 'th' ? 'สมัครใช้งานองค์กร' : 'Create your organization' }}</h1>
    <p>{{ locale === 'th'
      ? 'สร้างพื้นที่ทำงานและบัญชีผู้ดูแลองค์กร ระบบจะส่งรหัสเปิดใช้งานไปยังอีเมลที่ระบุ'
      : 'Set up a workspace and its administrator account. We’ll email an activation code to the address you provide.' }}</p>
  </section>

  <section v-if="result" class="login-card signup-result" role="status">
    <h2>{{ locale === 'th' ? 'สร้างองค์กรเรียบร้อยแล้ว' : 'Organization created' }}</h2>
    <p>{{ locale === 'th'
      ? `ระบบกำลังส่งรหัสเปิดใช้งานไปที่ ${result.adminEmail} ตรวจสอบกล่องจดหมายและโฟลเดอร์สแปม`
      : `An activation code is being sent to ${result.adminEmail}. Check your inbox and spam folder.` }}</p>
    <p>{{ locale === 'th'
      ? 'รหัสผ่านใหม่ต้องยาวอย่างน้อย 12 ตัว มีตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก ตัวเลข และสัญลักษณ์'
      : 'Your new password must be at least 12 characters and include uppercase and lowercase letters, a number, and a symbol.' }}</p>
    <button class="button button-secondary" type="button" :disabled="resendPending" @click="resendActivation">
      {{ resendPending
        ? (locale === 'th' ? 'กำลังขอรหัส…' : 'Requesting code…')
        : (locale === 'th' ? 'ยังไม่ได้รับอีเมล? ส่งรหัสอีกครั้ง' : 'Didn’t receive the email? Send another code') }}
    </button>
    <p v-if="resendMessage" role="status">{{ resendMessage }}</p>
    <p v-if="resendError" role="alert">{{ resendError }}</p>
    <div class="organization-code">
      <span>{{ locale === 'th' ? 'รหัสองค์กรสำหรับเข้าสู่ระบบ' : 'Organization code for sign in' }}</span>
      <strong>{{ result.organizationCode }}</strong>
      <button class="button button-secondary" type="button" @click="copyOrganizationCode">
        {{ copied
          ? (locale === 'th' ? 'คัดลอกแล้ว' : 'Copied')
          : (locale === 'th' ? 'คัดลอกรหัส' : 'Copy code') }}
      </button>
    </div>
    <p>{{ locale === 'th'
      ? 'ใช้รหัสนี้ อีเมลผู้ดูแล และรหัสผ่านใหม่ที่ตั้งในหน้าเปิดใช้งานเพื่อเข้าสู่ระบบ'
      : 'Use this code, the administrator email, and the password you set during activation to sign in.' }}</p>
    <NuxtLink class="button" to="/activate">
      {{ locale === 'th' ? 'ไปหน้าเปิดใช้งานบัญชี' : 'Go to account activation' }}
    </NuxtLink>
  </section>

  <form v-else class="login-card" @submit.prevent="signUp">
    <label>
      {{ locale === 'th' ? 'ชื่อองค์กร' : 'Organization name' }}
      <input v-model="name" required maxlength="200" autocomplete="organization" />
    </label>
    <label>
      {{ locale === 'th' ? 'ชื่อ URL องค์กร' : 'Organization URL name' }}
      <input v-model="slug" required minlength="3" maxlength="80" pattern="[a-z0-9][a-z0-9-]{2,79}"
        autocapitalize="none" autocomplete="url" placeholder="acme-studio" />
      <small>{{ locale === 'th'
        ? 'ใช้อักษร a-z ตัวเลข และขีดกลาง เริ่มด้วยตัวอักษรหรือตัวเลข'
        : 'Use lowercase a-z, numbers, and hyphens. Start with a letter or number.' }}</small>
    </label>
    <label>
      {{ locale === 'th' ? 'อีเมลผู้ดูแลองค์กร' : 'Organization administrator email' }}
      <input v-model="adminEmail" required type="email" autocomplete="email" />
    </label>
    <p>{{ locale === 'th'
      ? 'หลังสมัคร ให้เปิดอีเมลและใช้รหัสชั่วคราวในหน้าเปิดใช้งานเพื่อตั้งรหัสผ่าน'
      : 'After sign up, use the one-time code sent by email on the activation page to set a password.' }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button class="button" type="submit" :disabled="pending">
      {{ pending
        ? (locale === 'th' ? 'กำลังสร้างองค์กร…' : 'Creating organization…')
        : (locale === 'th' ? 'สมัครองค์กร' : 'Sign up organization') }}
    </button>
    <NuxtLink to="/">{{ locale === 'th' ? 'กลับไปเข้าสู่ระบบ' : 'Back to sign in' }}</NuxtLink>
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
