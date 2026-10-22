<script setup lang="ts">
import { unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'
import { readSessionLocale, useLocaleText, type Locale, fieldLocaleKey } from '../../../shared/i18n'
import { applyTheme, readSessionTheme } from '../../../shared/theme'
import { nameInitials } from '../../../shared/initials'

type Profile = {
  id: string
  email: string
  firstName: string
  lastName: string
  photoUrl: string | null
  jobTitle: string | null
}

const token = ref('')
const config = useRuntimeConfig()
useIframeAutoHeight(config.public.shellOrigin)
const gateway = ref('http://localhost:8081')
const locale = ref<Locale>('en')
provide(fieldLocaleKey, locale)
const profile = reactive({ firstName: '', lastName: '', photoUrl: '' as string | null, jobTitle: '' })
const email = ref('')
const photoSource = ref('')
const selectedFile = ref<File | null>(null)
const selectedPreview = ref('')
// Messages for the details form, and separately for the photo, each shown next to its own action.
const error = ref('')
const notice = ref('')
const photoError = ref('')
const photoNotice = ref('')
/** The details as last loaded or saved, to tell whether the form has unsaved changes. */
const saved = reactive({ firstName: '', lastName: '', jobTitle: '' })
const dirty = computed(() => profile.firstName !== saved.firstName || profile.lastName !== saved.lastName ||
  (profile.jobTitle || '') !== saved.jobTitle)
function markSaved() {
  Object.assign(saved, { firstName: profile.firstName, lastName: profile.lastName, jobTitle: profile.jobTitle || '' })
}
function cancelDetails() {
  Object.assign(profile, { firstName: saved.firstName, lastName: saved.lastName, jobTitle: saved.jobTitle })
  error.value = ''
  notice.value = ''
}
const saving = ref(false)
const photoBusy = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
let photoObjectUrl = ''

const fullName = computed(() => [profile.firstName.trim(), profile.lastName.trim()].filter(Boolean).join(' '))
const initials = computed(() => nameInitials(profile.firstName, profile.lastName) || email.value[0]?.toUpperCase() || '•')
const avatarSource = computed(() => selectedPreview.value || photoSource.value)
const copy = useLocaleText(locale, { th: {
  title: 'ตั้งค่าโปรไฟล์', cardLabel: 'โปรไฟล์ของคุณตามที่เพื่อนร่วมงานเห็น', headerPreview: 'ที่ส่วนหัว',
  signInEmail: 'อีเมลสำหรับเข้าสู่ระบบ', emailFixed: 'คุณใช้อีเมลนี้เข้าสู่ระบบ และเปลี่ยนไม่ได้',
  unsaved: 'มีการแก้ไขที่ยังไม่บันทึก', cancel: 'ยกเลิก', previewMark: 'ตัวอย่าง ยังไม่ได้บันทึก',
  signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell เพื่อแก้ไขโปรไฟล์',
  photoTitle: 'รูปโปรไฟล์', photoHelp: 'ไฟล์ JPEG, PNG หรือ WebP ขนาดไม่เกิน 5 MB หรือลากรูปมาวางบนการ์ดนี้',
  choosePhoto: 'เลือกรูปภาพ', replacePhoto: 'เปลี่ยนรูปภาพ', uploadPhoto: 'บันทึกรูปภาพ',
  removePhoto: 'ลบรูปภาพ', firstName: 'ชื่อ', lastName: 'นามสกุล', jobTitle: 'ตำแหน่งงาน',
  accountEmail: 'อีเมลบัญชี', detailsTitle: 'ข้อมูลส่วนตัว', save: 'บันทึกข้อมูล',
  saving: 'กำลังบันทึก…', loadedError: 'โหลดโปรไฟล์ไม่สำเร็จ กรุณาลองใหม่',
  saved: 'บันทึกข้อมูลโปรไฟล์แล้ว', saveError: 'บันทึกไม่สำเร็จ กรุณาตรวจสอบข้อมูลแล้วลองอีกครั้ง',
  fileTypeError: 'เลือกรูป JPEG, PNG หรือ WebP', fileSizeError: 'รูปภาพต้องมีขนาดไม่เกิน 5 MB',
  photoSaved: 'อัปเดตรูปโปรไฟล์แล้ว', photoError: 'อัปโหลดรูปไม่สำเร็จ กรุณาลองใหม่',
  photoRemoved: 'ลบรูปโปรไฟล์แล้ว', removeError: 'ลบรูปไม่สำเร็จ กรุณาลองใหม่',
  firstNamePlaceholder: 'ระบุชื่อของคุณ', lastNamePlaceholder: 'ระบุนามสกุลของคุณ',
  jobTitlePlaceholder: 'เช่น ผู้จัดการโครงการ'
}, en: {
  title: 'Profile settings', cardLabel: 'Your profile as colleagues see it', headerPreview: 'In the header',
  signInEmail: 'Sign-in email', emailFixed: 'You sign in with this email. It cannot be changed.',
  unsaved: 'Unsaved changes', cancel: 'Cancel', previewMark: 'Preview, not saved yet',
  signIn: 'Please sign in through Aiyara Shell to edit your profile.',
  photoTitle: 'Profile photo', photoHelp: 'JPEG, PNG, or WebP up to 5 MB. You can also drag one onto this card.',
  choosePhoto: 'Choose photo', replacePhoto: 'Change photo', uploadPhoto: 'Save photo',
  removePhoto: 'Remove photo', firstName: 'First name', lastName: 'Last name', jobTitle: 'Job title',
  accountEmail: 'Account email', detailsTitle: 'Personal details', save: 'Save changes',
  saving: 'Saving…', loadedError: 'Could not load your profile. Please try again.',
  saved: 'Profile details saved.', saveError: 'Could not save your profile. Check the fields and try again.',
  fileTypeError: 'Choose a JPEG, PNG, or WebP image.', fileSizeError: 'The image must be no larger than 5 MB.',
  photoSaved: 'Profile photo updated.', photoError: 'Could not upload the photo. Please try again.',
  photoRemoved: 'Profile photo removed.', removeError: 'Could not remove the photo. Please try again.',
  firstNamePlaceholder: 'Enter your first name', lastNamePlaceholder: 'Enter your last name',
  jobTitlePlaceholder: 'For example, Project manager'
} })

async function authenticatedFetch(path: string, options: RequestInit = {}) {
  return withRemoteRefresh(async accessToken => {
    const response = await fetch(`${gateway.value}${path}`, {
      ...options, headers: { ...options.headers, Authorization: `Bearer ${accessToken}` }
    })
    if (!response.ok) {
      const data = await response.json().catch(() => undefined)
      throw { status: response.status, data }
    }
    return response
  }, token, config.public.shellOrigin)
}

function notifyShell() {
  if (window.parent !== window) {
    window.parent.postMessage({ type: 'aiyara.profile.updated' }, config.public.shellOrigin)
  }
}

function releasePhotoUrls() {
  if (photoObjectUrl) URL.revokeObjectURL(photoObjectUrl)
  if (selectedPreview.value) URL.revokeObjectURL(selectedPreview.value)
  photoObjectUrl = ''
  selectedPreview.value = ''
}

async function displayPhoto(photoUrl: string | null) {
  if (photoObjectUrl) URL.revokeObjectURL(photoObjectUrl)
  photoObjectUrl = ''
  photoSource.value = ''
  if (!photoUrl) return
  if (photoUrl.startsWith('https://')) {
    photoSource.value = photoUrl
    return
  }

  const response = await authenticatedFetch(photoUrl)
  photoObjectUrl = URL.createObjectURL(await response.blob())
  photoSource.value = photoObjectUrl
}

function onMessage(event: MessageEvent) {
  if (event.source !== window.parent || event.origin !== config.public.shellOrigin || event.data?.type !== 'aiyara.session') return
  const nextToken = event.data.token || ''
  const sessionChanged = token.value !== nextToken
  token.value = nextToken
  gateway.value = event.data.gatewayUrl || gateway.value
  locale.value = readSessionLocale(event.data)
  applyTheme(readSessionTheme(event.data))
  if (sessionChanged && token.value) void load()
  if (!token.value) {
    email.value = ''
    profile.firstName = ''
    profile.lastName = ''
    profile.photoUrl = null
    profile.jobTitle = ''
    releasePhotoUrls()
    photoSource.value = ''
  }
}

async function load() {
  error.value = ''
  try {
    const value = unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<Profile>>(`${gateway.value}/api/v1/identity/profile/me`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      }), token, config.public.shellOrigin))
    email.value = value.email
    profile.firstName = value.firstName
    profile.lastName = value.lastName
    profile.photoUrl = value.photoUrl
    profile.jobTitle = value.jobTitle || ''
    markSaved()
    await displayPhoto(value.photoUrl)
  } catch {
    error.value = copy.value.loadedError
  }
}

function setPhotoFile(file: File | undefined) {
  if (!file) return
  photoError.value = ''
  photoNotice.value = ''
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    photoError.value = copy.value.fileTypeError
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    photoError.value = copy.value.fileSizeError
    return
  }
  if (selectedPreview.value) URL.revokeObjectURL(selectedPreview.value)
  selectedFile.value = file
  selectedPreview.value = URL.createObjectURL(file)
}

/** Drops the chosen photo without saving it. */
function cancelPhoto() {
  if (selectedPreview.value) URL.revokeObjectURL(selectedPreview.value)
  selectedPreview.value = ''
  selectedFile.value = null
  photoError.value = ''
  if (fileInput.value) fileInput.value.value = ''
}

function onFileChange(event: Event) {
  setPhotoFile((event.target as HTMLInputElement).files?.[0])
}

function onPhotoDrop(event: DragEvent) {
  event.preventDefault()
  setPhotoFile(event.dataTransfer?.files[0])
}

async function uploadPhoto() {
  if (!selectedFile.value || photoBusy.value) return
  photoBusy.value = true
  photoError.value = ''
  photoNotice.value = ''
  try {
    const form = new FormData()
    form.append('photo', selectedFile.value)
    const result = unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<{ photoUrl: string }>>(`${gateway.value}/api/v1/identity/profile/me/photo`, {
        method: 'POST', headers: { Authorization: `Bearer ${accessToken}` }, body: form
      }), token, config.public.shellOrigin))
    selectedFile.value = null
    if (selectedPreview.value) URL.revokeObjectURL(selectedPreview.value)
    selectedPreview.value = ''
    profile.photoUrl = result.photoUrl
    await displayPhoto(result.photoUrl)
    photoNotice.value = copy.value.photoSaved
    notifyShell()
  } catch {
    photoError.value = copy.value.photoError
  } finally {
    photoBusy.value = false
    if (fileInput.value) fileInput.value.value = ''
  }
}

async function removePhoto() {
  if (photoBusy.value) return
  photoBusy.value = true
  photoError.value = ''
  photoNotice.value = ''
  try {
    await withRemoteRefresh(accessToken => $fetch(`${gateway.value}/api/v1/identity/profile/me/photo`, {
      method: 'DELETE', headers: { Authorization: `Bearer ${accessToken}` }
    }), token, config.public.shellOrigin)
    profile.photoUrl = null
    releasePhotoUrls()
    photoSource.value = ''
    selectedFile.value = null
    photoNotice.value = copy.value.photoRemoved
    notifyShell()
  } catch {
    photoError.value = copy.value.removeError
  } finally {
    photoBusy.value = false
  }
}

async function saveProfile() {
  if (saving.value) return
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    unwrapResponse(await withRemoteRefresh(accessToken =>
      $fetch<ApiEnvelope<Profile>>(`${gateway.value}/api/v1/identity/profile/me`, {
        method: 'PUT', headers: { Authorization: `Bearer ${accessToken}` },
        body: { firstName: profile.firstName, lastName: profile.lastName, jobTitle: profile.jobTitle || null }
      }), token, config.public.shellOrigin))
    markSaved()
    notice.value = copy.value.saved
    notifyShell()
  } catch {
    error.value = copy.value.saveError
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  window.addEventListener('message', onMessage)
  if (window.parent !== window) {
    window.parent.postMessage({ type: 'aiyara.ready' }, config.public.shellOrigin)
  }
})
onBeforeUnmount(() => {
  window.removeEventListener('message', onMessage)
  releasePhotoUrls()
})
</script>

<template>
  <main class="profile-page">
    <h1>{{ copy.title }}</h1>

    <p v-if="!token" class="sign-in-message">{{ copy.signIn }}</p>

    <div v-else class="profile-layout">
      <!-- Your card: you as colleagues see you, drawn from what is being typed. A photo can be dropped on it. -->
      <section class="your-card" :class="{ 'is-preview': selectedFile }" :aria-label="copy.cardLabel" @dragover.prevent @drop="onPhotoDrop">
        <div class="photo-preview" aria-hidden="true">
          <img v-if="avatarSource" :src="avatarSource" alt="" />
          <span v-else>{{ initials }}</span>
        </div>
        <p v-if="selectedFile" class="preview-mark">{{ copy.previewMark }}</p>
        <p class="card-name">{{ fullName || email }}</p>
        <p v-if="profile.jobTitle?.trim()" class="card-title">{{ profile.jobTitle }}</p>

        <div class="photo-buttons">
          <template v-if="selectedFile">
            <button class="button button-primary" type="button" :disabled="photoBusy" @click="uploadPhoto">{{ photoBusy ? copy.saving : copy.uploadPhoto }}</button>
            <button class="button button-quiet" type="button" :disabled="photoBusy" @click="cancelPhoto">{{ copy.cancel }}</button>
          </template>
          <template v-else>
            <label class="button button-secondary" for="profile-photo-input">{{ avatarSource ? copy.replacePhoto : copy.choosePhoto }}</label>
            <button v-if="profile.photoUrl" class="button button-quiet danger" type="button" :disabled="photoBusy" @click="removePhoto">{{ copy.removePhoto }}</button>
          </template>
        </div>
        <input id="profile-photo-input" ref="fileInput" class="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" @change="onFileChange" />
        <p class="photo-help">{{ copy.photoHelp }}</p>
        <p v-if="photoError" class="feedback-error" role="alert">{{ photoError }}</p>
        <p v-else-if="photoNotice" class="feedback-success" role="status">{{ photoNotice }}</p>

        <!-- How the Shell's header shows you, from the same live values. -->
        <div class="header-preview">
          <span class="header-preview-label">{{ copy.headerPreview }}</span>
          <span class="header-chip" aria-hidden="true">
            <span class="chip-avatar"><img v-if="avatarSource" :src="avatarSource" alt="" /><template v-else>{{ initials }}</template></span>
            <span class="chip-name">{{ fullName || email }}</span>
            <svg viewBox="0 0 20 20"><path d="m5 7.5 5 5 5-5" /></svg>
          </span>
        </div>
      </section>

      <form class="details" @submit.prevent="saveProfile">
        <h2>{{ copy.detailsTitle }}</h2>
        <div class="fields-grid">
          <label class="field">
            <span>{{ copy.firstName }}</span>
            <input v-model="profile.firstName" required maxlength="100" autocomplete="given-name" :placeholder="copy.firstNamePlaceholder" />
          </label>
          <label class="field">
            <span>{{ copy.lastName }}</span>
            <input v-model="profile.lastName" required maxlength="100" autocomplete="family-name" :placeholder="copy.lastNamePlaceholder" />
          </label>
          <label class="field field-full">
            <span>{{ copy.jobTitle }}</span>
            <input v-model="profile.jobTitle" maxlength="160" autocomplete="organization-title" :placeholder="copy.jobTitlePlaceholder" />
          </label>
          <div class="field field-full">
            <span>{{ copy.signInEmail }}</span>
            <p class="email-value">{{ email }}</p>
            <small>{{ copy.emailFixed }}</small>
          </div>
        </div>
        <div class="form-footer">
          <div class="feedback" aria-live="polite">
            <p v-if="error" class="feedback-error" role="alert">{{ error }}</p>
            <p v-else-if="notice && !dirty" class="feedback-success" role="status">{{ notice }}</p>
            <p v-else-if="dirty" class="unsaved">{{ copy.unsaved }}</p>
          </div>
          <div class="form-actions">
            <button v-if="dirty" class="button button-quiet" type="button" :disabled="saving" @click="cancelDetails">{{ copy.cancel }}</button>
            <button class="button button-primary" type="submit" :disabled="saving || !dirty">{{ saving ? copy.saving : copy.save }}</button>
          </div>
        </div>
      </form>
    </div>
  </main>
</template>

<style scoped>
.profile-page { box-sizing: border-box; color: var(--color-ink); margin: 0 auto; max-width: 60rem; min-width: 0; padding: clamp(1rem, 3vw, 2.5rem); width: 100%; }
h1 { font-family: var(--font-display); font-size: clamp(1.6rem, 3.5vw, 2.2rem); font-weight: 600; line-height: 1.2; margin: 0 0 1.5rem; }
.profile-layout { align-items: start; display: grid; gap: 1.5rem; grid-template-columns: minmax(16rem, 20rem) minmax(0, 1fr); }
/* Your card. */
.your-card { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .9rem; display: grid; justify-items: center; min-width: 0; padding: 1.75rem 1.25rem 1.25rem; text-align: center; }
.your-card.is-preview { border-color: var(--color-brand); border-style: dashed; }
.photo-preview { align-items: center; background: var(--color-work-track); border-radius: 50%; color: var(--color-brand); display: flex; font-family: var(--font-display); font-size: 2.6rem; font-weight: 600; height: 8.5rem; justify-content: center; overflow: hidden; width: 8.5rem; }
.photo-preview img { height: 100%; object-fit: cover; width: 100%; }
.preview-mark { background: var(--color-work-track); border-radius: 999px; color: var(--color-brand-ink); font-size: .75rem; font-weight: 600; margin: .75rem 0 0; padding: .2rem .6rem; }
.card-name { font-family: var(--font-display); font-size: 1.5rem; font-weight: 600; line-height: 1.25; margin: 1rem 0 0; max-width: 100%; overflow-wrap: anywhere; }
.card-title { color: var(--color-muted); margin: .25rem 0 0; max-width: 100%; overflow-wrap: anywhere; }
.photo-buttons { display: flex; flex-wrap: wrap; gap: .5rem; justify-content: center; margin-top: 1.25rem; }
.photo-help { color: var(--color-muted); font-size: .8rem; line-height: 1.45; margin: .75rem 0 0; }
.your-card .feedback-error, .your-card .feedback-success { font-size: .85rem; margin: .6rem 0 0; }
.header-preview { align-items: center; border-top: 1px solid var(--color-border); display: grid; gap: .5rem; justify-items: center; margin-top: 1.25rem; padding-top: 1rem; width: 100%; }
.header-preview-label { color: var(--color-muted); font-size: .78rem; }
.header-chip { align-items: center; border: 1px solid var(--color-border); border-radius: 999px; display: inline-flex; gap: .5rem; max-width: 100%; padding: .2rem .7rem .2rem .25rem; }
.chip-avatar { align-items: center; background: var(--color-brand-soft); border-radius: 50%; color: var(--color-brand); display: inline-flex; flex: 0 0 1.9rem; font-size: .78rem; font-weight: 700; height: 1.9rem; justify-content: center; overflow: hidden; width: 1.9rem; }
.chip-avatar img { height: 100%; object-fit: cover; width: 100%; }
.chip-name { font-size: .88rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.header-chip svg { fill: none; flex: 0 0 .9rem; height: .9rem; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; stroke-width: 1.7; width: .9rem; }
/* The details form. */
.details { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .9rem; min-width: 0; padding: clamp(1rem, 2.5vw, 1.5rem); }
.details h2 { font-family: var(--font-display); font-size: 1.2rem; font-weight: 600; margin: 0 0 1.1rem; }
.fields-grid { display: grid; gap: 1.1rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
.field { display: grid; gap: .4rem; min-width: 0; }
.field > span { font-size: .86rem; font-weight: 600; }
.field input { background: var(--color-surface); border: 1px solid var(--color-border-strong); border-radius: .5rem; color: var(--color-ink); font: inherit; min-height: 2.75rem; padding: .65rem .8rem; width: 100%; }
.field input::placeholder { color: var(--color-faint); }
.field-full { grid-column: 1 / -1; }
.email-value { font-weight: 500; margin: 0; overflow-wrap: anywhere; }
.field small { color: var(--color-muted); font-size: .8rem; }
.form-footer { align-items: center; border-top: 1px solid var(--color-border); display: flex; flex-wrap: wrap; gap: .75rem 1rem; justify-content: space-between; margin-top: 1.4rem; padding-top: 1.1rem; }
.feedback p { font-size: .88rem; margin: 0; }
.feedback-error { color: var(--color-danger); }
.feedback-success { color: var(--color-success); }
.unsaved { color: var(--color-warn); }
.form-actions { display: flex; gap: .5rem; margin-left: auto; }
/* Buttons: no hover lift; colour changes only. */
.button { align-items: center; border: 1px solid transparent; border-radius: .5rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .88rem; font-weight: 600; justify-content: center; min-height: 2.6rem; padding: .55rem .9rem; }
.button:focus-visible, input:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
/* The file input is hidden, so its label shows the keyboard focus. */
/* (it follows the buttons, so :has finds the label before it) */
.photo-buttons:has(+ #profile-photo-input:focus-visible) .button-secondary { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.button:disabled { cursor: not-allowed; opacity: .55; }
.button-primary { background: var(--color-brand); color: var(--color-on-brand); }
.button-primary:hover:not(:disabled) { background: var(--color-brand-strong); }
.button-secondary { background: var(--color-surface); border-color: var(--color-border); color: var(--color-ink); }
.button-secondary:hover { background: var(--color-surface-muted); }
.button-quiet { background: transparent; color: var(--color-muted); }
.button-quiet:hover:not(:disabled) { background: var(--color-surface-muted); }
.button-quiet.danger { color: var(--color-danger); }
.visually-hidden { height: 1px; margin: -1px; overflow: hidden; position: absolute; clip: rect(0, 0, 0, 0); white-space: nowrap; width: 1px; }
.sign-in-message { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .75rem; color: var(--color-muted); padding: 1.25rem; }
@media (max-width: 44rem) {
  .profile-layout { grid-template-columns: minmax(0, 1fr); }
  .photo-preview { font-size: 2rem; height: 6.5rem; width: 6.5rem; }
}
@media (max-width: 30rem) {
  .profile-page { padding: 1rem; }
  .fields-grid { grid-template-columns: minmax(0, 1fr); }
  .form-actions { width: 100%; }
  .form-actions .button { flex: 1; }
}
</style>
