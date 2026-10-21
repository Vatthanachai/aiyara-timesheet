<script setup lang="ts">
import { safeError } from '../../../shared/api-response'
import { useLocaleText } from '../../../shared/i18n'
import { DEFAULT_FORMATS, formatDate, formatMonth, formatTime, type Formats } from '../../../shared/date-format'
import { isoDate, type MyEntry, type MyWeek } from '../composables/useMyTimesheet'
import type { RulerBand } from './DayRuler.vue'

const { locale } = useLocale()
const { visibleNavigation } = useRemoteNavigation()
const { loadWeek } = useMyTimesheet()
const formats = useState<Formats>('organization-formats', () => DEFAULT_FORMATS)
const profile = useState<{ firstName: string } | null>('profile-summary', () => null)
const text = useLocaleText(locale, {
  th: {
    morning: 'สวัสดีตอนเช้า', afternoon: 'สวัสดีตอนบ่าย', evening: 'สวัสดีตอนเย็น', honorific: 'คุณ',
    recordTime: 'บันทึกเวลา', loggedToday: 'วันนี้บันทึกแล้ว {logged} จาก {standard}', loggedTodayOff: 'วันนี้บันทึกแล้ว {logged}',
    nothingYet: 'วันนี้ยังไม่ได้บันทึกเวลา เวลาทำงาน {start}–{end}', holidayToday: 'วันนี้เป็นวันหยุด: {name}',
    leaveToday: 'วันนี้คุณ{kind}', dayOffToday: 'วันนี้ไม่ใช่วันทำงาน', now: 'ตอนนี้',
    loading: 'กำลังโหลดวันทำงานของคุณ…', loadFailed: 'โหลดวันทำงานของคุณไม่สำเร็จ', tryAgain: 'ลองอีกครั้ง',
    todayEntries: 'รายการวันนี้', thisWeek: 'สัปดาห์นี้', today: 'วันนี้', dayOff: 'วันหยุดประจำสัปดาห์',
    nothingLogged: 'ไม่มีรายการ', monthTotal: '{total} ใน {days} วัน', monthEmpty: 'เดือนนี้ยังไม่มีรายการ',
    monthClosed: 'ปิดเดือนแล้ว', apps: 'เมนูอื่น', annual: 'ลาพักร้อน', sick: 'ลาป่วย', other: 'ลา'
  },
  en: {
    morning: 'Good morning', afternoon: 'Good afternoon', evening: 'Good evening', honorific: '',
    recordTime: 'Record time', loggedToday: '{logged} logged today of {standard}', loggedTodayOff: '{logged} logged today',
    nothingYet: 'Nothing logged yet today. Your day runs {start}–{end}.', holidayToday: 'Today is a holiday: {name}',
    leaveToday: 'You are on {kind} today', dayOffToday: 'Today is not a working day', now: 'Now',
    loading: 'Loading your day…', loadFailed: 'Your day could not be loaded', tryAgain: 'Try again',
    todayEntries: 'Logged today', thisWeek: 'This week', today: 'Today', dayOff: 'Day off',
    nothingLogged: 'Nothing logged', monthTotal: '{total} over {days} days', monthEmpty: 'Nothing logged this month yet',
    monthClosed: 'Month closed', apps: 'Also here', annual: 'annual leave', sick: 'sick leave', other: 'leave'
  }
})
const fill = (template: string, values: Record<string, string | number>) =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? ''))

const week = ref<MyWeek | null>(null)
const pending = ref(true)
const error = ref('')
const clock = ref(new Date())
let clockTimer: ReturnType<typeof setInterval> | undefined

async function load() {
  pending.value = true
  error.value = ''
  try { week.value = await loadWeek() }
  catch (cause) { error.value = safeError(cause, locale.value) }
  finally { pending.value = false }
}

onMounted(() => {
  void load()
  clockTimer = setInterval(() => { clock.value = new Date() }, 60_000)
})
onUnmounted(() => { if (clockTimer) clearInterval(clockTimer) })

const greeting = computed(() => {
  const hour = clock.value.getHours()
  const part = hour < 12 ? text.value.morning : hour < 18 ? text.value.afternoon : text.value.evening
  const name = profile.value?.firstName?.trim()
  return name ? (locale.value === 'th' ? `${part} ${text.value.honorific}${name}` : `${part}, ${name}`) : part
})
const intlLocale = computed(() => locale.value === 'th' ? 'th-TH-u-ca-gregory' : 'en-GB')
const weekday = (day: string, style: 'long' | 'short') => new Intl.DateTimeFormat(intlLocale.value,
  { weekday: style, timeZone: 'UTC' }).format(new Date(`${day}T00:00:00Z`))
const todayIso = computed(() => isoDate(clock.value))
const todayLabel = computed(() => `${weekday(todayIso.value, 'long')} ${formatDate(todayIso.value, formats.value, locale.value)}`)

const hours = computed(() => {
  const value = week.value?.workHours
  if (!value) return null
  const start = clockMinutes(value.startTime)
  const end = clockMinutes(value.endTime)
  const breakStart = value.breakStart ? clockMinutes(value.breakStart) : null
  const breakEnd = value.breakEnd ? clockMinutes(value.breakEnd) : null
  const breakLength = breakStart != null && breakEnd != null ? breakEnd - breakStart : 0
  return { start, end, breakStart, breakEnd, standard: end - start - breakLength }
})

const minutesOn = (day: string) => (week.value?.entries ?? [])
  .filter(entry => entry.date.slice(0, 10) === day)
  .reduce((sum, entry) => sum + entry.durationMinutes, 0)
const holidayOn = (day: string) => week.value?.holidays.find(holiday => holiday.date.slice(0, 10) === day)
const leaveOn = (day: string) => week.value?.leaves.find(leave => leave.date.slice(0, 10) === day)
const isWorkingDay = (day: string) => Boolean(week.value?.workingDays.includes(new Date(`${day}T00:00:00Z`).getUTCDay()))
const leaveLabel = (kind: string) => kind === 'annual' ? text.value.annual : kind === 'sick' ? text.value.sick : text.value.other
const time = (value: string) => formatTime(value, formats.value)

const todayEntries = computed<MyEntry[]>(() => (week.value?.entries ?? [])
  .filter(entry => entry.date.slice(0, 10) === todayIso.value)
  .sort((a, b) => a.startTime.localeCompare(b.startTime)))
const bands = computed<RulerBand[]>(() => todayEntries.value.map(entry => {
  const start = clockMinutes(entry.startTime)
  const end = clockMinutes(entry.endTime)
  // An overnight entry is drawn up to midnight.
  return { start, end: end > start ? end : 24 * 60, label: entry.taskName }
}))
const nowMinutes = computed(() => clock.value.getHours() * 60 + clock.value.getMinutes())
const hourLabel = (hour: number) => formats.value.timeFormat === 'HH:mm' ? String(hour).padStart(2, '0') : String(hour % 12 || 12)

const summary = computed(() => {
  if (!week.value || !hours.value) return ''
  const logged = minutesOn(todayIso.value)
  const holiday = holidayOn(todayIso.value)
  const leave = leaveOn(todayIso.value)
  const working = isWorkingDay(todayIso.value) && !holiday && !leave
  if (logged) return fill(working ? text.value.loggedToday : text.value.loggedTodayOff,
    { logged: formatDuration(logged, locale.value), standard: formatDuration(hours.value.standard, locale.value) })
  if (holiday) return fill(text.value.holidayToday, { name: holiday.name })
  if (leave) return fill(text.value.leaveToday, { kind: leaveLabel(leave.kind) })
  if (!working) return text.value.dayOffToday
  return fill(text.value.nothingYet, { start: time(week.value.workHours.startTime), end: time(week.value.workHours.endTime) })
})

const weekRows = computed(() => {
  if (!week.value || !hours.value) return []
  const scale = Math.max(hours.value.standard, ...week.value.days.map(minutesOn), 1)
  return week.value.days.map(day => {
    const minutes = minutesOn(day)
    const holiday = holidayOn(day)
    const leave = leaveOn(day)
    const note = holiday?.name ?? (leave ? leaveLabel(leave.kind) : !isWorkingDay(day) ? text.value.dayOff
      : day < todayIso.value ? text.value.nothingLogged : '')
    return {
      day, minutes, note, isToday: day === todayIso.value, isOff: Boolean(holiday || leave || !isWorkingDay(day)),
      name: weekday(day, 'short'), date: Number(day.slice(8, 10)), width: `${(minutes / scale) * 100}%`
    }
  })
})

const month = computed(() => {
  if (!week.value) return null
  const entries = week.value.monthEntries
  const total = entries.reduce((sum, entry) => sum + entry.durationMinutes, 0)
  const days = new Set(entries.map(entry => entry.date.slice(0, 10))).size
  return {
    label: formatMonth(todayIso.value.slice(0, 7), formats.value, locale.value),
    text: total ? fill(text.value.monthTotal, { total: formatDuration(total, locale.value), days }) : text.value.monthEmpty,
    locked: week.value.monthLocked
  }
})
const links = computed(() => visibleNavigation.value.filter(item => item.id !== 'identity' && item.id !== 'timesheet'))
</script>

<template>
  <section class="today" aria-labelledby="today-greeting">
    <header class="today-heading">
      <div>
        <p class="today-date"><time :datetime="todayIso">{{ todayLabel }}</time></p>
        <h1 id="today-greeting">{{ greeting }}</h1>
        <p v-if="summary" class="today-summary">{{ summary }}</p>
      </div>
      <NuxtLink class="button today-action" to="/remote/timesheet">{{ text.recordTime }}</NuxtLink>
    </header>

    <p v-if="pending && !week" class="today-status">{{ text.loading }}</p>
    <div v-else-if="error" class="notice notice-error today-error" role="alert">
      <p>{{ text.loadFailed }}. {{ error }}</p>
      <button type="button" class="button" @click="load">{{ text.tryAgain }}</button>
    </div>

    <template v-if="week && hours">
      <DayRuler class="today-ruler" :work-start="hours.start" :work-end="hours.end" :break-start="hours.breakStart"
        :break-end="hours.breakEnd" :bands="bands" :now="nowMinutes" :now-label="text.now" :hour-label="hourLabel" animate />

      <section v-if="todayEntries.length" class="today-entries" aria-labelledby="today-entries">
        <h2 id="today-entries" class="sr-only">{{ text.todayEntries }}</h2>
        <ol>
          <li v-for="entry in todayEntries" :key="entry.id">
            <span class="entry-time">{{ time(entry.startTime) }}–{{ time(entry.endTime) }}</span>
            <span class="entry-task">{{ entry.taskName }}</span>
            <span v-if="entry.projectName" class="entry-project">{{ entry.projectName }}</span>
          </li>
        </ol>
      </section>
    </template>

    <div v-if="!pending || week" class="today-columns">
      <section v-if="week" aria-labelledby="this-week">
        <h2 id="this-week">{{ text.thisWeek }}</h2>
        <ol class="week-list">
          <li v-for="row in weekRows" :key="row.day" :class="{ 'is-today': row.isToday, 'is-off': row.isOff }"
            :aria-current="row.isToday ? 'date' : undefined">
            <time class="week-day" :datetime="row.day">{{ row.name }} {{ row.date }}</time>
            <span v-if="row.minutes" class="week-bar"><span :style="{ width: row.width }" /></span>
            <span v-if="row.minutes" class="week-value">{{ formatDuration(row.minutes, locale) }}</span>
            <span v-else class="week-note">{{ row.note }}</span>
          </li>
        </ol>
      </section>
      <aside class="today-side">
        <section v-if="month" aria-labelledby="this-month">
          <h2 id="this-month">{{ month.label }}</h2>
          <p class="month-total">{{ month.text }}</p>
          <p v-if="month.locked" class="month-closed">{{ text.monthClosed }}</p>
        </section>
        <nav v-if="links.length" :aria-label="text.apps" class="home-links">
          <h2>{{ text.apps }}</h2>
          <NuxtLink v-for="item in links" :key="item.id" :to="item.path">
            <strong>{{ item.label[locale] }}</strong>
            <span>{{ item.description[locale] }}</span>
          </NuxtLink>
        </nav>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.today { display: grid; gap: 2.25rem; }
.today-heading { align-items: end; display: flex; flex-wrap: wrap; gap: 1.25rem 2rem; justify-content: space-between; }
.today-date { color: var(--color-muted); font-size: .95rem; margin: 0; }
.today h1 { font-family: var(--font-display); font-size: clamp(2rem, 5vw, 3.25rem); font-weight: 600; letter-spacing: -.01em; line-height: 1.1; margin: .35rem 0 .5rem; }
.today-summary { color: var(--color-ink); font-size: 1.125rem; margin: 0; max-width: 40rem; }
.today-action { font-size: 1rem; padding: .85rem 1.4rem; }
.today-action:hover { background: var(--color-brand-strong); }
.today-status { margin: 0; }
.today-error { align-items: center; display: flex; flex-wrap: wrap; gap: .75rem 1rem; justify-content: space-between; margin: 0; }
.today-error p { color: inherit; margin: 0; }
.today-error .button { border: 0; font: inherit; }
.today-ruler { margin-top: .5rem; }
.today-entries ol { display: grid; gap: .1rem; list-style: none; margin: -1rem 0 0; padding: 0; }
.today-entries li { align-items: baseline; border-bottom: 1px solid var(--color-border); display: flex; flex-wrap: wrap; gap: .25rem 1rem; padding: .6rem 0; }
.entry-time { font-family: var(--font-display); font-variant-numeric: tabular-nums; font-weight: 500; min-width: 7.5rem; }
.entry-task { font-weight: 600; min-width: 0; overflow-wrap: anywhere; }
.entry-project { color: var(--color-muted); font-size: .9rem; }
.today-columns { display: grid; gap: 2.5rem 3.5rem; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr); }
.today h2 { font-family: var(--font-display); font-size: 1.2rem; font-weight: 600; margin: 0 0 .9rem; }
.week-list { display: grid; list-style: none; margin: 0; padding: 0; }
.week-list li { align-items: center; border-top: 1px solid var(--color-border); display: grid; gap: 1rem; grid-template-columns: 5.5rem minmax(0, 1fr) 7rem; min-height: 2.75rem; padding: 0 .6rem; }
.week-list li:last-child { border-bottom: 1px solid var(--color-border); }
.week-day { color: var(--color-muted); font-variant-numeric: tabular-nums; white-space: nowrap; }
.week-bar { background: var(--color-work-track); border-radius: .25rem; display: block; height: .6rem; overflow: hidden; }
.week-bar span { background: var(--color-brand); border-radius: .25rem; display: block; height: 100%; }
.week-value { font-family: var(--font-display); font-variant-numeric: tabular-nums; font-weight: 500; text-align: right; white-space: nowrap; }
.week-note { color: var(--color-muted); font-size: .9rem; grid-column: 2 / -1; overflow-wrap: anywhere; }
.week-list li.is-off { background: var(--color-day-off); }
.week-list li.is-today { box-shadow: inset 3px 0 0 var(--color-brand); }
.week-list li.is-today .week-day { color: var(--color-ink); font-weight: 700; }
.today-side { display: grid; gap: 2rem; align-content: start; }
.month-total { color: var(--color-ink); font-family: var(--font-display); font-size: 1.5rem; font-weight: 500; line-height: 1.3; margin: 0; }
.month-closed { font-size: .9rem; margin: .35rem 0 0; }
.home-links { display: grid; }
.home-links a { border-top: 1px solid var(--color-border); display: grid; gap: .15rem; padding: .75rem 0; text-decoration: none; }
.home-links a:last-child { border-bottom: 1px solid var(--color-border); }
.home-links a strong { color: var(--color-ink); }
.home-links a span { color: var(--color-muted); font-size: .9rem; }
.home-links a:hover strong { color: var(--color-brand); text-decoration: underline; text-underline-offset: .2em; }
.sr-only { border: 0; clip: rect(0 0 0 0); height: 1px; margin: -1px; overflow: hidden; padding: 0; position: absolute; white-space: nowrap; width: 1px; }
@media (max-width: 48rem) {
  .today-columns { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 40rem) {
  .today { gap: 1.75rem; }
  .today-action { text-align: center; width: 100%; }
  .week-list li { gap: .6rem; grid-template-columns: 5.25rem minmax(0, 1fr) 6.5rem; padding: 0 .4rem; }
}
</style>
