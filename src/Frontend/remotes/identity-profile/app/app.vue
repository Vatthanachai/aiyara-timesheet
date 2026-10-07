<script setup lang="ts">
import { unwrapResponse, withRemoteRefresh, type ApiEnvelope } from '../../../shared/api-response'
import { useIframeAutoHeight } from '../../../shared/iframe-auto-height'

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
const locale = ref<'th' | 'en'>('th')
const profile = reactive({ firstName: '', lastName: '', photoUrl: '' as string | null, jobTitle: '' })
const email = ref('')
const photoSource = ref('')
const selectedFile = ref<File | null>(null)
const selectedPreview = ref('')
const error = ref('')
const notice = ref('')
const saving = ref(false)
const photoBusy = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
let photoObjectUrl = ''

const fullName = computed(() => [profile.firstName, profile.lastName].filter(Boolean).join(' '))
const initials = computed(() => [profile.firstName, profile.lastName]
  .filter(Boolean).map(name => name.trim()[0]?.toUpperCase()).join('') || 'A')
const avatarSource = computed(() => selectedPreview.value || photoSource.value)
const copy = computed(() => locale.value === 'th' ? {
  title: 'ตั้งค่าโปรไฟล์', subtitle: 'จัดการข้อมูลส่วนตัวและรูปภาพที่ใช้ในระบบ',
  signIn: 'กรุณาเข้าสู่ระบบผ่าน Aiyara Shell เพื่อแก้ไขโปรไฟล์',
  photoTitle: 'รูปโปรไฟล์', photoHelp: 'ใช้ภาพ JPEG, PNG หรือ WebP ขนาดไม่เกิน 5 MB',
  choosePhoto: 'เลือกรูปภาพ', replacePhoto: 'เปลี่ยนรูปภาพ', uploadPhoto: 'บันทึกรูปภาพ',
  removePhoto: 'ลบรูปภาพ', firstName: 'ชื่อ', lastName: 'นามสกุล', jobTitle: 'ตำแหน่งงาน',
  accountEmail: 'อีเมลบัญชี', detailsTitle: 'ข้อมูลส่วนตัว', save: 'บันทึกข้อมูล',
  saving: 'กำลังบันทึก…', loadedError: 'โหลดโปรไฟล์ไม่สำเร็จ กรุณาลองใหม่',
  saved: 'บันทึกข้อมูลโปรไฟล์แล้ว', saveError: 'บันทึกไม่สำเร็จ กรุณาตรวจสอบข้อมูลแล้วลองอีกครั้ง',
  fileTypeError: 'เลือกรูป JPEG, PNG หรือ WebP', fileSizeError: 'รูปภาพต้องมีขนาดไม่เกิน 5 MB',
  photoSaved: 'อัปเดตรูปโปรไฟล์แล้ว', photoError: 'อัปโหลดรูปไม่สำเร็จ กรุณาลองใหม่',
  photoRemoved: 'ลบรูปโปรไฟล์แล้ว', removeError: 'ลบรูปไม่สำเร็จ กรุณาลองใหม่',
  dragHint: 'หรือลากรูปภาพมาวางที่นี่',
  firstNamePlaceholder: 'ระบุชื่อของคุณ', lastNamePlaceholder: 'ระบุนามสกุลของคุณ',
  jobTitlePlaceholder: 'เช่น ผู้จัดการโครงการ'
} : {
  title: 'Profile settings', subtitle: 'Manage your personal details and the photo used across Aiyara.',
  signIn: 'Please sign in through Aiyara Shell to edit your profile.',
  photoTitle: 'Profile photo', photoHelp: 'JPEG, PNG, or WebP. Up to 5 MB.',
  choosePhoto: 'Choose photo', replacePhoto: 'Change photo', uploadPhoto: 'Save photo',
  removePhoto: 'Remove photo', firstName: 'First name', lastName: 'Last name', jobTitle: 'Job title',
  accountEmail: 'Account email', detailsTitle: 'Personal details', save: 'Save changes',
  saving: 'Saving…', loadedError: 'Could not load your profile. Please try again.',
  saved: 'Profile details saved.', saveError: 'Could not save your profile. Check the fields and try again.',
  fileTypeError: 'Choose a JPEG, PNG, or WebP image.', fileSizeError: 'The image must be no larger than 5 MB.',
  photoSaved: 'Profile photo updated.', photoError: 'Could not upload the photo. Please try again.',
  photoRemoved: 'Profile photo removed.', removeError: 'Could not remove the photo. Please try again.',
  dragHint: 'or drag an image here',
  firstNamePlaceholder: 'Enter your first name', lastNamePlaceholder: 'Enter your last name',
  jobTitlePlaceholder: 'For example, Project manager'
})

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
  locale.value = event.data.locale === 'en' ? 'en' : 'th'
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
    await displayPhoto(value.photoUrl)
  } catch {
    error.value = copy.value.loadedError
  }
}

function setPhotoFile(file: File | undefined) {
  if (!file) return
  error.value = ''
  notice.value = ''
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    error.value = copy.value.fileTypeError
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    error.value = copy.value.fileSizeError
    return
  }
  if (selectedPreview.value) URL.revokeObjectURL(selectedPreview.value)
  selectedFile.value = file
  selectedPreview.value = URL.createObjectURL(file)
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
  error.value = ''
  notice.value = ''
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
    notice.value = copy.value.photoSaved
    notifyShell()
  } catch {
    error.value = copy.value.photoError
  } finally {
    photoBusy.value = false
    if (fileInput.value) fileInput.value.value = ''
  }
}

async function removePhoto() {
  if (photoBusy.value) return
  photoBusy.value = true
  error.value = ''
  notice.value = ''
  try {
    await withRemoteRefresh(accessToken => $fetch(`${gateway.value}/api/v1/identity/profile/me/photo`, {
      method: 'DELETE', headers: { Authorization: `Bearer ${accessToken}` }
    }), token, config.public.shellOrigin)
    profile.photoUrl = null
    releasePhotoUrls()
    photoSource.value = ''
    selectedFile.value = null
    notice.value = copy.value.photoRemoved
    notifyShell()
  } catch {
    error.value = copy.value.removeError
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
    <header class="page-heading">
      <span class="eyebrow">AIYARA ACCOUNT</span>
      <h1>{{ copy.title }}</h1>
      <p>{{ copy.subtitle }}</p>
    </header>

    <p v-if="!token" class="sign-in-message">{{ copy.signIn }}</p>

    <template v-else>
      <section class="profile-card photo-card" :aria-label="copy.photoTitle" @dragover.prevent @drop="onPhotoDrop">
        <div class="section-heading">
          <div>
            <h2>{{ copy.photoTitle }}</h2>
            <p>{{ copy.photoHelp }}</p>
          </div>
        </div>

        <div class="photo-layout">
          <div class="photo-preview" aria-hidden="true">
            <img v-if="avatarSource" :src="avatarSource" alt="" />
            <span v-else>{{ initials }}</span>
          </div>
          <div class="photo-actions">
            <p class="profile-display-name">{{ fullName || email }}</p>
            <p class="profile-email">{{ email }}</p>
            <div class="photo-buttons">
              <label class="button button-secondary" for="profile-photo-input">
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
                {{ avatarSource ? copy.replacePhoto : copy.choosePhoto }}
              </label>
              <button v-if="profile.photoUrl && !selectedFile" class="text-button danger-button" type="button" :disabled="photoBusy" @click="removePhoto">
                {{ copy.removePhoto }}
              </button>
            </div>
            <input id="profile-photo-input" ref="fileInput" class="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" @change="onFileChange" />
            <div v-if="selectedFile" class="selected-file-row">
              <span>{{ selectedFile.name }}</span>
              <button class="button button-primary" type="button" :disabled="photoBusy" @click="uploadPhoto">
                {{ photoBusy ? copy.saving : copy.uploadPhoto }}
              </button>
            </div>
            <p class="drag-hint">{{ copy.dragHint }}</p>
          </div>
        </div>
      </section>

      <form class="profile-card details-card" @submit.prevent="saveProfile">
        <div class="section-heading">
          <div>
            <h2>{{ copy.detailsTitle }}</h2>
            <p>{{ email }}</p>
          </div>
          <span class="account-badge">{{ copy.accountEmail }}</span>
        </div>
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
        </div>
        <div class="form-footer">
          <div class="feedback" aria-live="polite">
            <p v-if="error" class="feedback-error" role="alert">{{ error }}</p>
            <p v-else-if="notice" class="feedback-success" role="status">{{ notice }}</p>
          </div>
          <button class="button button-primary" type="submit" :disabled="saving">
            {{ saving ? copy.saving : copy.save }}
          </button>
        </div>
      </form>
    </template>
  </main>
</template>

<style scoped>
.profile-page { box-sizing: border-box; color: var(--color-ink); margin: 0 auto; max-width: 54rem; min-width: 0; padding: clamp(1rem, 3vw, 2.5rem); width: 100%; }
.page-heading { margin-bottom: 1.75rem; }
.eyebrow { color: var(--color-brand); font-size: .72rem; font-weight: 750; letter-spacing: .14em; }
.page-heading h1 { font-size: clamp(1.75rem, 4vw, 2.3rem); letter-spacing: -.035em; line-height: 1.2; margin: .55rem 0 .45rem; }
.page-heading p, .section-heading p { color: var(--color-muted); line-height: 1.55; margin: 0; }
.profile-card { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: 1rem; box-shadow: 0 8px 28px rgb(25 43 75 / 5%); margin-top: 1rem; padding: clamp(1rem, 2.5vw, 1.5rem); }
.section-heading { align-items: flex-start; display: flex; gap: 1rem; justify-content: space-between; min-width: 0; }
.section-heading > div { min-width: 0; }
.section-heading h2 { font-size: 1.05rem; letter-spacing: -.01em; margin: 0 0 .35rem; }
.photo-layout { align-items: center; display: flex; gap: 1.5rem; margin-top: 1.4rem; }
.photo-preview { align-items: center; background: #eaf1ff; border: 4px solid #fff; border-radius: 50%; box-shadow: 0 0 0 1px var(--color-border); color: var(--color-brand); display: flex; flex: 0 0 7rem; font-size: 1.8rem; font-weight: 700; height: 7rem; justify-content: center; overflow: hidden; width: 7rem; }
.photo-preview img { height: 100%; object-fit: cover; width: 100%; }
.photo-actions { min-width: 0; }
.profile-display-name { font-size: 1.05rem; font-weight: 700; margin: 0; overflow-wrap: anywhere; }
.profile-email { color: var(--color-muted); font-size: .88rem; margin: .2rem 0 .85rem; overflow-wrap: anywhere; }
.photo-buttons { align-items: center; display: flex; flex-wrap: wrap; gap: .85rem; }
.button { align-items: center; border: 1px solid transparent; border-radius: .55rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .9rem; font-weight: 650; gap: .5rem; justify-content: center; min-height: 2.65rem; padding: .6rem .9rem; text-decoration: none; transition: background 120ms ease, border-color 120ms ease, transform 120ms ease; }
.button:hover:not(:disabled) { transform: translateY(-1px); }
.button:focus-visible, .text-button:focus-visible, input:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.button:disabled { cursor: wait; opacity: .65; }
.button svg { fill: none; height: 1rem; stroke: currentColor; stroke-linecap: round; stroke-width: 1.8; width: 1rem; }
.button-primary { background: var(--color-brand); color: white; }
.button-primary:hover:not(:disabled) { background: #0f4fd7; }
.button-secondary { background: #fff; border-color: var(--color-border); color: var(--color-ink); }
.button-secondary:hover { background: #f6f8fb; border-color: #aab6c8; }
.text-button { background: transparent; border: 0; cursor: pointer; font: inherit; font-size: .88rem; padding: .5rem .25rem; }
.danger-button { color: #b42318; }
.danger-button:disabled { cursor: wait; opacity: .55; }
.drag-hint { color: var(--color-muted); font-size: .78rem; margin: .6rem 0 0; }
.selected-file-row { align-items: center; display: flex; flex-wrap: wrap; gap: .75rem; justify-content: space-between; margin-top: .9rem; }
.selected-file-row > span { color: var(--color-muted); font-size: .82rem; max-width: 18rem; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.visually-hidden { height: 1px; margin: -1px; overflow: hidden; position: absolute; clip: rect(0, 0, 0, 0); white-space: nowrap; width: 1px; }
.account-badge { background: #f1f4f9; border-radius: 999px; color: var(--color-muted); font-size: .73rem; font-weight: 600; padding: .35rem .6rem; white-space: nowrap; }
.fields-grid { display: grid; gap: 1.1rem; grid-template-columns: repeat(2, minmax(0, 1fr)); margin-top: 1.5rem; }
.field { display: grid; gap: .45rem; }
.field > span { font-size: .86rem; font-weight: 650; }
.field input { background: #fff; border: 1px solid #cbd4e1; border-radius: .55rem; color: var(--color-ink); font: inherit; min-height: 2.8rem; padding: .7rem .8rem; width: 100%; }
.field input::placeholder { color: #929dad; }
.field-full { grid-column: 1 / -1; }
.form-footer { align-items: center; border-top: 1px solid #edf0f5; display: flex; gap: 1rem; justify-content: space-between; margin-top: 1.4rem; padding-top: 1.1rem; }
.feedback { min-height: 1.4rem; }
.feedback p { font-size: .86rem; margin: .2rem 0; }
.feedback-error { color: #b42318; }
.feedback-success { color: #067647; }
.sign-in-message { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .75rem; color: var(--color-muted); padding: 1.25rem; }
@media (max-width: 38rem) {
  .profile-page { padding: 1rem; }
  .page-heading { margin-bottom: 1.25rem; }
  .photo-layout { align-items: flex-start; flex-direction: column; gap: 1rem; margin-top: 1rem; }
  .photo-preview { flex-basis: 5.5rem; height: 5.5rem; width: 5.5rem; }
  .photo-actions { width: 100%; }
  .photo-buttons { align-items: stretch; flex-direction: column; }
  .photo-buttons > * { justify-content: center; text-align: center; width: 100%; }
  .selected-file-row { align-items: stretch; flex-direction: column; }
  .selected-file-row > span { max-width: 100%; }
  .selected-file-row > button { width: 100%; }
  .section-heading { flex-direction: column; gap: .65rem; }
  .fields-grid { grid-template-columns: 1fr; }
  .field-full { grid-column: auto; }
  .form-footer { align-items: stretch; flex-direction: column; }
  .form-footer > button { width: 100%; }
  .account-badge { display: none; }
}
@media (max-width: 22rem) {
  .profile-page { padding: .75rem; }
  .profile-card { border-radius: .75rem; padding: .85rem; }
  .button { font-size: .84rem; padding-inline: .65rem; }
}
</style>
