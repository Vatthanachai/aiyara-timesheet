<script setup lang="ts">
import { useLocaleText } from '../../../../shared/i18n'

type PasswordPolicy = {
  minimumLength: number
  expiryDays: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
  requireSymbol: boolean
}

const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    eyebrow: 'ผู้ดูแลระบบ', title: 'ความปลอดภัย',
    subtitle: 'ตั้งค่าความปลอดภัยของบัญชีผู้ดูแลระบบ นโยบายขององค์กรลูกค้ายังเป็นหน้าที่ของผู้ดูแลแต่ละองค์กร',
    policy: 'นโยบายรหัสผ่านของผู้ดูแลระบบ', minimumLength: 'ความยาวขั้นต่ำ (12–128)', expiryDays: 'อายุรหัสผ่าน (วัน)',
    uppercase: 'ต้องมีตัวพิมพ์ใหญ่', lowercase: 'ต้องมีตัวพิมพ์เล็ก', digit: 'ต้องมีตัวเลข', symbol: 'ต้องมีสัญลักษณ์',
    reason: 'เหตุผล (ไม่บังคับ)', save: 'บันทึกนโยบาย',
    stricterNote: 'ถ้านโยบายเข้มขึ้น ผู้ดูแลระบบทุกคน รวมถึงคุณ ต้องเปลี่ยนรหัสผ่านและเข้าสู่ระบบใหม่',
    mfa: 'การยืนยันตัวตนสองขั้นตอน',
    mfaBody: 'ผู้ดูแลระบบทุกคนต้องใช้รหัสจากแอปยืนยันตัวตนเมื่อเข้าสู่ระบบ ถ้าผู้ดูแลคนใดทำแอปหาย ให้ผู้ดูแลคนอื่นรีเซ็ต MFA ได้ที่หน้าผู้ดูแลระบบ',
    manageAdmins: 'ไปที่หน้าผู้ดูแลระบบ', saved: 'บันทึกนโยบายแล้ว',
    loading: 'กำลังโหลด…', loadError: 'โหลดนโยบายไม่สำเร็จ', saveError: 'บันทึกไม่สำเร็จ กรุณาตรวจสอบค่าที่กรอก'
  },
  en: {
    eyebrow: 'SYSTEM ADMINISTRATION', title: 'Security',
    subtitle: 'Security settings for system administrator accounts. Customer organizations keep their own policies.',
    policy: 'System administrator password policy', minimumLength: 'Minimum length (12–128)', expiryDays: 'Expiry (days)',
    uppercase: 'Require uppercase', lowercase: 'Require lowercase', digit: 'Require a digit', symbol: 'Require a symbol',
    reason: 'Reason (optional)', save: 'Save policy',
    stricterNote: 'A stricter policy makes every system administrator, you included, change their password and sign in again.',
    mfa: 'Two-step verification',
    mfaBody: 'Every system administrator signs in with a code from an authenticator app. If someone loses theirs, another admin can reset their MFA from the Platform admins page.',
    manageAdmins: 'Go to Platform admins', saved: 'Policy saved.',
    loading: 'Loading…', loadError: 'The policy could not be loaded.', saveError: 'The policy could not be saved. Check the values.'
  }
})
const { isPlatformAdmin, platformGet, platformSend, ready } = usePlatformApi()
const policy = reactive<PasswordPolicy>({
  minimumLength: 12, expiryDays: 180, requireUppercase: true, requireLowercase: true, requireDigit: true, requireSymbol: true
})
const reason = ref('')
const loading = ref(false)
const pending = ref(false)
const notice = ref('')
const failure = ref('')

async function save() {
  pending.value = true
  notice.value = ''
  failure.value = ''
  try {
    Object.assign(policy, await platformSend<PasswordPolicy>('/security/password-policy', {
      method: 'PUT', body: { ...policy, reason: reason.value.trim() || null }
    }))
    reason.value = ''
    notice.value = text.value.saved
  } catch { failure.value = text.value.saveError }
  finally { pending.value = false }
}

onMounted(async () => {
  if (!await ready()) return
  loading.value = true
  try { Object.assign(policy, await platformGet<PasswordPolicy>('/security/password-policy')) }
  catch { failure.value = text.value.loadError }
  finally { loading.value = false }
})
</script>

<template>
  <section class="hero">
    <p class="eyebrow">{{ text.eyebrow }}</p>
    <h1>{{ text.title }}</h1>
    <p>{{ text.subtitle }}</p>
  </section>

  <SysAdminRestricted v-if="!isPlatformAdmin" />
  <template v-else>
    <SysAdminNav />
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <p v-if="failure" class="notice notice-error" role="alert">{{ failure }}</p>
    <div class="sysadmin-columns">
      <section class="sysadmin-card" aria-labelledby="policy-heading">
        <h2 id="policy-heading">{{ text.policy }}</h2>
        <p v-if="loading" role="status">{{ text.loading }}</p>
        <form v-else class="sysadmin-inline-form" @submit.prevent="save">
          <label>{{ text.minimumLength }}<input v-model.number="policy.minimumLength" type="number" min="12" max="128" required /></label>
          <label>{{ text.expiryDays }}<input v-model.number="policy.expiryDays" type="number" min="1" max="3650" required /></label>
          <label class="sysadmin-check"><input v-model="policy.requireUppercase" type="checkbox" /> {{ text.uppercase }}</label>
          <label class="sysadmin-check"><input v-model="policy.requireLowercase" type="checkbox" /> {{ text.lowercase }}</label>
          <label class="sysadmin-check"><input v-model="policy.requireDigit" type="checkbox" /> {{ text.digit }}</label>
          <label class="sysadmin-check"><input v-model="policy.requireSymbol" type="checkbox" /> {{ text.symbol }}</label>
          <label>{{ text.reason }}<input v-model="reason" maxlength="500" /></label>
          <p class="sysadmin-muted">{{ text.stricterNote }}</p>
          <button class="button" type="submit" :disabled="pending">{{ text.save }}</button>
        </form>
      </section>
      <section class="sysadmin-card" aria-labelledby="mfa-heading">
        <h2 id="mfa-heading">{{ text.mfa }}</h2>
        <p>{{ text.mfaBody }}</p>
        <NuxtLink to="/sys-admin/admins">{{ text.manageAdmins }}</NuxtLink>
      </section>
    </div>
  </template>
</template>
