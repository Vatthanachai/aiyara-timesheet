<script setup lang="ts">
import { computed, nextTick, ref, toRef, watch } from 'vue'
import { safeError, type Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'
import { formatDateTime, type Formats } from '../../../../shared/date-format'

type SessionItem = { id: string; device: string; signedInAtUtc: string; lastUsedAtUtc: string; expiresAtUtc: string; current: boolean }
type Confirming = { kind: 'one'; id: string } | { kind: 'others' } | { kind: 'everywhere' } | null

/**
 * Where the member's account is signed in, within the current organization, and the ways to end
 * those sessions. The current device is never offered here: it signs out through the normal
 * sign-out. Signing out everywhere tells the page, which returns to the sign-in.
 */
const props = defineProps<{
  token: string; locale: Locale; formats: Formats
  api: <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
}>()
const emit = defineEmits<{ signedOutEverywhere: [] }>()
const copy = useLocaleText(toRef(props, 'locale'), {
  th: {
    title: 'อุปกรณ์ที่เข้าสู่ระบบ', hint: 'อุปกรณ์และเบราว์เซอร์ที่เข้าสู่ระบบบัญชีนี้ในองค์กรปัจจุบัน หากไม่รู้จักอุปกรณ์ใด ให้ออกจากระบบอุปกรณ์นั้นแล้วเปลี่ยนรหัสผ่าน',
    loading: 'กำลังโหลดรายการ…', loadError: 'โหลดรายการอุปกรณ์ไม่สำเร็จ', retry: 'ลองอีกครั้ง', empty: 'ไม่พบอุปกรณ์ที่เข้าสู่ระบบ',
    thisDevice: 'อุปกรณ์นี้', signedIn: 'เข้าสู่ระบบเมื่อ', lastUsed: 'ใช้งานล่าสุดประมาณ', listLabel: 'อุปกรณ์ที่เข้าสู่ระบบ',
    signOut: 'ออกจากระบบ', signOutDevice: 'ออกจากระบบอุปกรณ์ {device}', cancel: 'ยกเลิก', working: 'กำลังดำเนินการ…',
    confirmOne: 'ออกจากระบบอุปกรณ์นี้ใช่หรือไม่', confirmOneYes: 'ใช่ ออกจากระบบ',
    others: 'ออกจากระบบอุปกรณ์อื่นทั้งหมด', confirmOthers: 'ออกจากระบบอุปกรณ์อื่นทั้งหมดในองค์กรนี้ใช่หรือไม่ อุปกรณ์นี้จะยังเข้าสู่ระบบอยู่', confirmOthersYes: 'ใช่ ออกจากระบบอุปกรณ์อื่น',
    everywhere: 'ออกจากระบบทุกที่', confirmEverywhere: 'ออกจากระบบทุกอุปกรณ์ในทุกองค์กร รวมถึงอุปกรณ์นี้ใช่หรือไม่ คุณจะต้องเข้าสู่ระบบใหม่', confirmEverywhereYes: 'ใช่ ออกจากระบบทุกที่',
    note: 'อุปกรณ์ที่ออกจากระบบแล้วอาจยังใช้งานได้อีกไม่เกิน 15 นาที ส่วนการออกจากระบบทุกที่มีผลทันที เวลา "ใช้งานล่าสุด" เป็นค่าประมาณ (ราว 15 นาที)',
    doneOne: 'ออกจากระบบอุปกรณ์นั้นแล้ว', alreadyGone: 'อุปกรณ์นั้นออกจากระบบไปแล้ว', doneOthers: 'ออกจากระบบอุปกรณ์อื่นแล้ว',
    unknown: 'เซสชันนี้เป็นแบบเก่า กรุณาออกจากระบบแล้วเข้าสู่ระบบใหม่ก่อนใช้งานฟังก์ชันนี้', changed: 'รายการเปลี่ยนไป แสดงรายการล่าสุดแล้ว'
  },
  en: {
    title: 'Signed-in devices', hint: 'Devices and browsers signed in to this account in the current organization. If you do not recognize one, sign it out and change your password.',
    loading: 'Loading devices…', loadError: 'Could not load the devices.', retry: 'Try again', empty: 'No signed-in devices found.',
    thisDevice: 'This device', signedIn: 'Signed in', lastUsed: 'Last used about', listLabel: 'Signed-in devices',
    signOut: 'Sign out', signOutDevice: 'Sign out {device}', cancel: 'Cancel', working: 'Working…',
    confirmOne: 'Sign out this device?', confirmOneYes: 'Yes, sign out',
    others: 'Sign out other devices', confirmOthers: 'Sign out every other device in this organization? This device stays signed in.', confirmOthersYes: 'Yes, sign out other devices',
    everywhere: 'Sign out everywhere', confirmEverywhere: 'Sign out of every device in every organization, including this one? You will need to sign in again.', confirmEverywhereYes: 'Yes, sign out everywhere',
    note: 'A device you sign out may keep working for up to 15 minutes. Signing out everywhere takes effect at once. "Last used" is approximate (about 15 minutes).',
    doneOne: 'That device was signed out.', alreadyGone: 'That device was already signed out.', doneOthers: 'Other devices were signed out.',
    unknown: 'This session is from before this feature. Sign out and sign in again, then try again.', changed: 'The list had changed. Showing the latest.'
  }
})
const sessions = ref<SessionItem[] | null>(null)
const loading = ref(false)
const pending = ref(false)
const loadError = ref('')
const error = ref('')
const notice = ref('')
const confirming = ref<Confirming>(null)
const title = ref<HTMLElement | null>(null)
const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
const errorCode = (cause: unknown) => (cause as { data?: { errorCode?: string } })?.data?.errorCode
const others = computed(() => (sessions.value ?? []).filter(item => !item.current))
const show = (iso: string) => formatDateTime(iso, timeZone, props.formats, props.locale)

async function load(quiet = false) {
  if (!quiet) loading.value = true
  loadError.value = ''
  try { sessions.value = (await props.api<SessionItem[] | undefined>('')) ?? [] }
  catch (cause) { if (!quiet) sessions.value = null; loadError.value = safeError(cause, props.locale) }
  finally { loading.value = false }
}
watch(() => props.token, value => {
  if (value) void load()
  else { sessions.value = null; confirming.value = null; error.value = ''; notice.value = '' }
}, { immediate: true })

function ask(next: Confirming, focusId: string) {
  notice.value = ''
  error.value = ''
  confirming.value = next
  void nextTick(() => document.getElementById(focusId)?.focus())
}
async function cancel(focusId: string) {
  confirming.value = null
  await nextTick()
  document.getElementById(focusId)?.focus()
}

/** Runs one session action, then shows the list again; a vanished session is not an error. */
async function run(path: string, method: string, done: string, after?: () => void) {
  pending.value = true
  error.value = ''
  notice.value = ''
  try {
    await props.api(path, { method })
    notice.value = done
    confirming.value = null
    if (after) { after(); return }
    await load(true)
  } catch (cause) {
    const code = errorCode(cause)
    confirming.value = null
    if (code === 'NOT_FOUND' || code === 'CURRENT_SESSION') {
      notice.value = code === 'NOT_FOUND' ? copy.value.alreadyGone : copy.value.changed
      await load(true)
    } else if (code === 'SESSION_UNKNOWN') error.value = copy.value.unknown
    else error.value = safeError(cause, props.locale)
  } finally {
    pending.value = false
    await nextTick()
    if (!confirming.value) title.value?.focus()
  }
}
const signOutOne = (id: string) => run(`/${encodeURIComponent(id)}`, 'DELETE', copy.value.doneOne)
const signOutOthers = () => run('/revoke-others', 'POST', copy.value.doneOthers)
const signOutEverywhere = () => run('/revoke-all', 'POST', '', () => { sessions.value = null; emit('signedOutEverywhere') })
</script>

<template>
  <section class="sessions" aria-labelledby="sessions-heading" :aria-busy="loading || pending">
    <h2 id="sessions-heading" ref="title" tabindex="-1">{{ copy.title }}</h2>
    <p class="s-hint">{{ copy.hint }}</p>

    <div class="s-live" aria-live="polite">
      <p v-if="notice" class="s-notice" role="status">{{ notice }}</p>
    </div>
    <p v-if="error" class="s-error" role="alert">{{ error }}</p>
    <p v-if="loading && !sessions" class="s-muted" role="status">{{ copy.loading }}</p>
    <p v-else-if="loadError && !sessions" class="s-error" role="alert">{{ loadError }}
      <button class="s-button s-quiet" type="button" @click="load()">{{ copy.retry }}</button>
    </p>

    <template v-if="sessions">
      <p v-if="loadError" class="s-error" role="alert">{{ loadError }}
        <button class="s-button s-quiet" type="button" @click="load()">{{ copy.retry }}</button>
      </p>
      <p v-if="!sessions.length" class="s-muted">{{ copy.empty }}</p>
      <ul v-else class="s-list" :aria-label="copy.listLabel">
        <li v-for="item in sessions" :key="item.id" class="s-item" :class="{ 'is-current': item.current }">
          <div class="s-main">
            <p class="s-device">
              <span class="s-name">{{ item.device }}</span>
              <span v-if="item.current" class="s-badge">{{ copy.thisDevice }}</span>
            </p>
            <p class="s-meta">{{ copy.signedIn }} <time :datetime="item.signedInAtUtc">{{ show(item.signedInAtUtc) }}</time></p>
            <p class="s-meta">{{ copy.lastUsed }} <time :datetime="item.lastUsedAtUtc">{{ show(item.lastUsedAtUtc) }}</time></p>
          </div>
          <div v-if="!item.current" class="s-side">
            <div v-if="confirming?.kind === 'one' && confirming.id === item.id" class="s-confirm" role="group" :aria-label="copy.confirmOne">
              <p>{{ copy.confirmOne }}</p>
              <div class="s-actions">
                <button :id="`session-yes-${item.id}`" class="s-button s-danger-solid" type="button" :disabled="pending" @click="signOutOne(item.id)">{{ pending ? copy.working : copy.confirmOneYes }}</button>
                <button class="s-button s-secondary" type="button" :disabled="pending" @click="cancel(`session-out-${item.id}`)">{{ copy.cancel }}</button>
              </div>
            </div>
            <button v-else :id="`session-out-${item.id}`" class="s-button s-danger" type="button" :disabled="pending"
              :aria-label="copy.signOutDevice.replace('{device}', item.device)" @click="ask({ kind: 'one', id: item.id }, `session-yes-${item.id}`)">{{ copy.signOut }}</button>
          </div>
        </li>
      </ul>

      <div v-if="confirming?.kind === 'others' || confirming?.kind === 'everywhere'" class="s-confirm s-confirm-wide" role="group"
        :aria-label="confirming.kind === 'others' ? copy.others : copy.everywhere">
        <p>{{ confirming.kind === 'others' ? copy.confirmOthers : copy.confirmEverywhere }}</p>
        <div class="s-actions">
          <button v-if="confirming.kind === 'others'" id="sessions-others-yes" class="s-button s-danger-solid" type="button" :disabled="pending" @click="signOutOthers">{{ pending ? copy.working : copy.confirmOthersYes }}</button>
          <button v-else id="sessions-everywhere-yes" class="s-button s-danger-solid" type="button" :disabled="pending" @click="signOutEverywhere">{{ pending ? copy.working : copy.confirmEverywhereYes }}</button>
          <button class="s-button s-secondary" type="button" :disabled="pending" @click="cancel(confirming.kind === 'others' ? 'sessions-others' : 'sessions-everywhere')">{{ copy.cancel }}</button>
        </div>
      </div>
      <div v-else class="s-actions">
        <button v-if="others.length" id="sessions-others" class="s-button s-secondary" type="button" :disabled="pending" @click="ask({ kind: 'others' }, 'sessions-others-yes')">{{ copy.others }}</button>
        <button id="sessions-everywhere" class="s-button s-danger" type="button" :disabled="pending" @click="ask({ kind: 'everywhere' }, 'sessions-everywhere-yes')">{{ copy.everywhere }}</button>
      </div>
      <p class="s-muted s-note">{{ copy.note }}</p>
    </template>
  </section>
</template>

<style scoped>
.sessions { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .9rem; display: grid; gap: .75rem; min-width: 0; padding: clamp(1rem, 2.5vw, 1.5rem); }
.sessions > * { max-width: 100%; min-width: 0; }
.sessions h2 { font-family: var(--font-display); font-size: 1.2rem; font-weight: 600; margin: 0; }
.sessions h2:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 3px; }
.s-hint, .s-muted { color: var(--color-muted); font-size: .9rem; line-height: 1.5; margin: 0; }
.s-note { font-size: .82rem; }
.s-notice { background: var(--color-success-soft); border: 1px solid var(--color-success-line); border-radius: .5rem; color: var(--color-success); font-size: .9rem; margin: 0; padding: .5rem .7rem; }
.s-error { align-items: center; background: var(--color-danger-soft); border: 1px solid var(--color-danger-line); border-radius: .5rem; color: var(--color-danger); display: flex; flex-wrap: wrap; font-size: .9rem; gap: .5rem; margin: 0; padding: .5rem .7rem; }
.s-list { display: grid; gap: .6rem; list-style: none; margin: 0; padding: 0; }
.s-item { align-items: center; border: 1px solid var(--color-border); border-radius: .6rem; display: flex; flex-wrap: wrap; gap: .6rem 1rem; justify-content: space-between; min-width: 0; padding: .75rem .9rem; }
.s-item.is-current { border-color: var(--color-brand); }
.s-main { display: grid; gap: .15rem; min-width: 0; }
.s-device { align-items: center; display: flex; flex-wrap: wrap; gap: .4rem .6rem; margin: 0; }
.s-name { font-weight: 600; overflow-wrap: anywhere; }
.s-badge { background: var(--color-brand-soft); border-radius: 999px; color: var(--color-brand-ink); font-size: .75rem; font-weight: 700; padding: .12rem .6rem; }
.s-meta { color: var(--color-muted); font-size: .85rem; margin: 0; overflow-wrap: anywhere; }
.s-side { min-width: 0; }
.s-confirm { background: var(--color-danger-soft); border: 1px solid var(--color-danger-line); border-radius: .5rem; display: grid; gap: .5rem; padding: .6rem .75rem; }
.s-confirm p { font-size: .9rem; margin: 0; }
.s-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
.s-button { align-items: center; border: 1px solid transparent; border-radius: .5rem; cursor: pointer; display: inline-flex; font: inherit; font-size: .9rem; font-weight: 600; justify-content: center; max-width: 100%; min-height: 2.75rem; padding: .55rem 1rem; text-align: center; }
.s-button:focus-visible { outline: 2px solid var(--color-brand); outline-offset: 2px; }
.s-button:disabled { cursor: not-allowed; opacity: .6; }
.s-secondary, .s-quiet { background: var(--color-surface); border-color: var(--color-border); color: var(--color-ink); }
.s-danger { background: transparent; border-color: var(--color-danger-line); color: var(--color-danger); }
.s-danger-solid { background: var(--color-danger); color: var(--color-on-brand); }
@media (max-width: 30rem) {
  .s-item { align-items: stretch; flex-direction: column; }
  .s-side .s-button, .s-actions .s-button { flex: 1 1 100%; }
}
</style>
