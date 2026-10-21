<script setup lang="ts">
import { computed } from 'vue'

export type RulerBand = { start: number; end: number; label: string }

/**
 * One day drawn along its hours: the working hours as a tinted track, the break hatched, and
 * logged time as solid bands. All positions are minutes after midnight. It is a picture only;
 * the page lists the same entries as text. Shared by the Shell's home page and the Timesheet
 * remote; each defines the --font-display and --color-* tokens it reads.
 */
const props = defineProps<{
  workStart: number
  workEnd: number
  breakStart?: number | null
  breakEnd?: number | null
  bands: RulerBand[]
  now?: number | null
  nowLabel?: string
  hourLabel: (hour: number) => string
  /** Grow the bands in once, when the day first appears. */
  animate?: boolean
}>()

// Whole hours around the working day, widened to fit anything logged outside it.
const from = computed(() => Math.floor(Math.min(props.workStart, ...props.bands.map(band => band.start)) / 60) * 60)
const to = computed(() => Math.ceil(Math.max(props.workEnd, ...props.bands.map(band => band.end)) / 60) * 60)
const hours = computed(() => Array.from({ length: (to.value - from.value) / 60 + 1 }, (_, index) => from.value / 60 + index))
const at = (minutes: number) => `${((minutes - from.value) / (to.value - from.value)) * 100}%`
const span = (start: number, end: number) => ({ left: at(start), width: `calc(${at(end)} - ${at(start)})` })
const hasBreak = computed(() => props.breakStart != null && props.breakEnd != null)
const showNow = computed(() => props.now != null && props.now >= from.value && props.now <= to.value)
</script>

<template>
  <div class="ruler" :class="{ 'is-animated': animate }" aria-hidden="true">
    <ol class="ruler-hours">
      <li v-for="(hour, index) in hours" :key="hour"
        :class="{ 'is-working': hour * 60 >= workStart && hour * 60 <= workEnd, 'is-minor': index % 2 === 1 && hours.length > 9 }"
        :style="{ left: at(hour * 60) }">{{ hourLabel(hour) }}</li>
    </ol>
    <div class="ruler-track"><div class="ruler-lane">
      <span class="ruler-work" :style="span(workStart, workEnd)" />
      <span v-if="hasBreak" class="ruler-break" :style="span(breakStart!, breakEnd!)" />
      <span v-for="(band, index) in bands" :key="`${band.start}-${index}`" class="ruler-band"
        :style="{ ...span(band.start, band.end), '--order': index }"><span>{{ band.label }}</span></span>
      <span v-if="showNow" class="ruler-now" :style="{ left: at(now!) }"><span>{{ nowLabel }}</span></span>
    </div></div>
  </div>
</template>

<style scoped>
.ruler { padding-bottom: 1.6rem; position: relative; }
/* The hours and the lane are inset by the same amount, so the first and last hour can be centred on their marks. */
.ruler-hours { height: 2.4rem; list-style: none; margin: 0 1rem; padding: 0; position: relative; }
.ruler-hours li {
  color: var(--color-hour-off); font-family: var(--font-display); font-size: clamp(1.05rem, 2.4vw, 1.6rem);
  font-variant-numeric: tabular-nums; font-weight: 500; line-height: 1; position: absolute; top: 0; transform: translateX(-50%);
}
.ruler-hours li.is-working { color: var(--color-ink); }
.ruler-track { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: .5rem; height: 3.5rem; padding: 0 1rem; }
.ruler-lane { height: 100%; position: relative; }
.ruler-work, .ruler-break, .ruler-band { bottom: .45rem; position: absolute; top: .45rem; }
.ruler-work { background: var(--color-work-track); border-radius: .3rem; }
.ruler-break {
  background: repeating-linear-gradient(135deg, var(--color-break) 0 2px, transparent 2px 7px);
  border-radius: .3rem; opacity: .7;
}
.ruler-band {
  align-items: center; background: var(--color-brand); border-radius: .3rem; box-shadow: inset 0 0 0 1px rgb(255 255 255 / 35%);
  color: white; display: flex; font-size: .78rem; font-weight: 600; min-width: 3px; overflow: hidden; padding: 0 .45rem;
}
.ruler-band span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ruler-now { border-left: 2px solid var(--color-ink); bottom: -1.6rem; position: absolute; top: -.45rem; }
.ruler-now span { bottom: 0; color: var(--color-ink); font-size: .75rem; font-weight: 600; left: .3rem; position: absolute; white-space: nowrap; }
.is-animated .ruler-band { animation: ruler-grow 520ms cubic-bezier(.2, .7, .2, 1) both; animation-delay: calc(var(--order) * 70ms + 120ms); transform-origin: left center; }
@keyframes ruler-grow { from { transform: scaleX(0); } to { transform: scaleX(1); } }
@media (prefers-reduced-motion: reduce) { .is-animated .ruler-band { animation: none; } }
@media (max-width: 40rem) {
  .ruler-hours li.is-minor { display: none; }
  .ruler-track { height: 3rem; }
  .ruler-band { font-size: .7rem; padding: 0 .3rem; }
}
</style>
