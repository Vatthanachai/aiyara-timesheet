<script setup lang="ts">
import { toRef } from 'vue'
import type { Locale } from '../../../../shared/api-response'
import { useLocaleText } from '../../../../shared/i18n'

/** One member's MFA status as a badge, and for another member who has an authenticator, the Reset MFA action. */
const props = defineProps<{ locale: Locale; email: string; enabled: boolean; canReset: boolean; id: string }>()
const emit = defineEmits<{ reset: [] }>()
const copy = useLocaleText(toRef(props, 'locale'), {
  th: { on: 'MFA เปิดอยู่', off: 'MFA ปิดอยู่', reset: 'รีเซ็ต MFA' },
  en: { on: 'MFA on', off: 'MFA off', reset: 'Reset MFA' }
})
</script>

<template>
  <div class="member-mfa">
    <span class="mfa-badge" :class="enabled ? 'is-on' : 'is-off'">{{ enabled ? copy.on : copy.off }}</span>
    <button v-if="canReset && enabled" :id="`mfa-reset-button-${id}`" class="button button-danger-text" type="button"
      :aria-label="`${copy.reset}: ${email}`" @click="emit('reset')">{{ copy.reset }}</button>
  </div>
</template>

<style scoped>
.member-mfa { align-items: center; display: flex; flex-wrap: wrap; gap: .25rem .5rem; margin-top: .35rem; }
.mfa-badge { border-radius: 999px; font-size: .75rem; font-weight: 700; padding: .1rem .55rem; white-space: nowrap; }
.mfa-badge.is-on { background: var(--color-success-soft); color: var(--color-success); }
.mfa-badge.is-off { background: var(--color-surface-muted); color: var(--color-muted); }
</style>
