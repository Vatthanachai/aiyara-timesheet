<script setup lang="ts">
import { injectTimesheetDates, injectTimesheetText, type Catalog, type Leave } from '../utils/timesheet'

const props = defineProps<{ leaves: Leave[]; holidays: Catalog['holidays']; month: string; canEdit: boolean; monthStart: string; monthEnd: string }>()
const leaveDate = defineModel<string>('leaveDate', { required: true })
const leaveKind = defineModel<string>('leaveKind', { required: true })
const emit = defineEmits<{ add: []; update: [leave: Leave]; remove: [id: string] }>()
const { text } = injectTimesheetText()
const dates = injectTimesheetDates()
const monthHolidays = computed(() => props.holidays.filter(x => x.date.startsWith(props.month)))
</script>

<template>
  <section class="panel support-panel"><div class="panel-heading"><div><span class="eyebrow">{{ text.leaves }}</span><h2>{{ text.planTimeOff }}</h2></div></div>
    <form v-if="canEdit" class="inline-form" @submit.prevent="emit('add')"><label>{{ text.date }}<input v-model="leaveDate" type="date" :aria-label="text.date" :min="monthStart" :max="monthEnd" /></label><label>{{ text.kind }}<select v-model="leaveKind"><option value="annual">{{ text.annual }}</option><option value="sick">{{ text.sick }}</option><option value="other">{{ text.other }}</option></select></label><button class="primary-button" type="submit">{{ text.add }}</button></form>
    <ul class="simple-list"><li v-for="leave in leaves" :key="leave.id"><template v-if="canEdit"><input v-model="leave.date" type="date" :aria-label="text.date" :min="monthStart" :max="monthEnd" /><select v-model="leave.kind" :aria-label="text.kind"><option value="annual">{{ text.annual }}</option><option value="sick">{{ text.sick }}</option><option value="other">{{ text.other }}</option></select><button class="quiet-button" type="button" @click="emit('update', leave)">{{ text.save }}</button><button class="danger-button" type="button" @click="emit('remove', leave.id)">{{ text.remove }}</button></template><template v-else><time :datetime="leave.date">{{ dates.date(leave.date) }}</time><span>{{ leave.kind }}</span></template></li><li v-if="!leaves.length" class="empty-state">{{ text.noLeavePlanned }}</li></ul>
    <div v-if="monthHolidays.length" class="holiday-list"><h3>{{ text.holidays }}</h3><p v-for="holiday in monthHolidays" :key="holiday.id"><time :datetime="holiday.date">{{ dates.date(holiday.date) }}</time><span>{{ holiday.name }}</span></p></div>
  </section>
</template>
