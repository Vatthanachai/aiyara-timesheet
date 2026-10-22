<script setup lang="ts">
import { useLocaleText } from '../../../shared/i18n'
import { formatDate, formatTime } from '../../../shared/date-format'
import { readSignOut, type SignOutRecord } from '../composables/useSession'

const { token } = useSession()
const { locale } = useLocale()
const text = useLocaleText(locale, {
  th: {
    title: 'ออกจากระบบแล้ว',
    body: 'เซสชันบนเบราว์เซอร์นี้สิ้นสุดแล้ว ใครก็ตามที่เปิด Aiyara Timesheet บนเครื่องนี้ต้องเข้าสู่ระบบใหม่',
    signedOut: 'ออกจากระบบ', signInAgain: 'เข้าสู่ระบบอีกครั้ง'
  },
  en: {
    title: 'You’re signed out',
    body: 'Your session on this browser has ended. Anyone who opens Aiyara Timesheet here now has to sign in.',
    signedOut: 'Signed out', signInAgain: 'Sign in again'
  }
})
useHead(() => ({ title: text.value.title }))

// The moment is written by the header's Sign out; without it there is nothing to confirm.
const record = ref<SignOutRecord | null>(null)
onMounted(() => {
  record.value = readSignOut()
  if (token.value || !record.value) void navigateTo('/', { replace: true })
})
const date = computed(() => record.value && formatDate(record.value.date, record.value.formats, locale.value, { weekday: true }))
const time = computed(() => record.value && formatTime(record.value.time, record.value.formats))
</script>

<template>
  <section v-if="record" class="signed-out" aria-labelledby="signed-out-title">
    <!-- The slip is read in order: the date, then the time it was signed out. -->
    <div class="slip punch-card">
      <p class="slip-date">{{ date }}</p>
      <p class="slip-label">{{ text.signedOut }}</p>
      <p class="slip-time"><time :datetime="`${record.date}T${record.time}`">{{ time }}</time></p>
    </div>

    <div class="signed-out-copy">
      <h1 id="signed-out-title">{{ text.title }}</h1>
      <p>{{ text.body }}</p>
      <NuxtLink class="button" :to="record.admin ? '/sys-admin/login' : '/'">{{ text.signInAgain }}</NuxtLink>
    </div>
  </section>
</template>

<style scoped>
.signed-out {
  align-items: center; display: grid; gap: 2.5rem 5rem;
  grid-template-columns: auto minmax(0, 32rem); justify-content: center; padding-block: clamp(0rem, 6vh, 4rem);
}
.slip {
  animation: slip-in 420ms cubic-bezier(.2, .7, .2, 1) both; padding: 2.4rem 1.75rem 1.5rem;
  transform: rotate(-2.5deg); width: 17rem;
}
.slip p { margin: 0; }
.slip-date { border-bottom: 1px dashed var(--color-border); color: var(--color-ink); font-family: var(--font-display); font-weight: 600; padding-bottom: .9rem; }
.slip .slip-label { color: var(--color-muted); font-size: .9rem; margin-top: 1rem; }
/* The time is printed onto the card a beat after it lands, like a time clock's stamp. */
.slip-time {
  animation: slip-print 260ms 380ms ease-out both; color: var(--color-brand-strong); font-family: var(--font-display);
  font-size: clamp(3.25rem, 10vw, 4.5rem); font-variant-numeric: tabular-nums; font-weight: 600; letter-spacing: -.02em; line-height: 1.1;
}
@keyframes slip-in { from { opacity: 0; transform: translateY(-1.5rem) rotate(-6deg); } }
@keyframes slip-print { from { opacity: 0; transform: scale(1.18); } }
.signed-out-copy { display: grid; justify-items: start; }
.signed-out-copy h1 { font-family: var(--font-display); font-size: clamp(2.2rem, 5.5vw, 3.5rem); font-weight: 600; letter-spacing: -.015em; margin: 0 0 1rem; }
.signed-out-copy p { font-size: 1.125rem; margin: 0 0 1.75rem; max-width: 30rem; }
.signed-out-copy .button { padding: .85rem 1.4rem; }
.signed-out-copy .button:hover { background: var(--color-brand-strong); }
@media (prefers-reduced-motion: reduce) { .slip, .slip-time { animation: none; } }
@media (max-width: 48rem) {
  .signed-out { gap: 2rem; grid-template-columns: minmax(0, 1fr); justify-items: start; padding-block: 0; }
  .slip { justify-self: center; }
}
</style>
