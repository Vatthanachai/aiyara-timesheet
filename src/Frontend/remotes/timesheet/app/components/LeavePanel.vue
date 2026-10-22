<script setup lang="ts">
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import DateField from '../../../../shared/components/DateField.vue'
import { injectTimesheetDates, injectTimesheetText, type Catalog, type Leave } from '../utils/timesheet'

const props = defineProps<{ leaves: Leave[]; holidays: Catalog['holidays']; month: string; canEdit: boolean; monthStart: string; monthEnd: string }>()
const leaveDate = defineModel<string>('leaveDate', { required: true })
const leaveKind = defineModel<string>('leaveKind', { required: true })
const emit = defineEmits<{ add: []; update: [leave: Leave]; remove: [id: string] }>()
const { text } = injectTimesheetText()
const dates = injectTimesheetDates()
const monthHolidays = computed(() => props.holidays.filter(x => x.date.startsWith(props.month)))
const kinds = computed(() => (['annual', 'sick', 'other'] as const).map(kind => ({ value: kind, label: text.value[kind] })))
const kindLabel = (kind: string) => kind === 'annual' || kind === 'sick' || kind === 'other' ? text.value[kind] : kind
</script>

<template>
  <section class="panel support-panel" aria-labelledby="leave-heading"><div class="section-heading"><h2 id="leave-heading">{{ text.leaveAndHolidays }}</h2></div>
    <form v-if="canEdit" class="inline-form" @submit.prevent="emit('add')"><label>{{ text.date }}<DateField v-model="leaveDate" :label="text.date" :min="monthStart" :max="monthEnd" /></label><label>{{ text.kind }}<AutoComplete v-model="leaveKind" :label="text.kind" :options="kinds" required /></label><button class="primary-button" type="submit">{{ text.add }}</button></form>
    <ul class="simple-list"><li v-for="leave in leaves" :key="leave.id"><template v-if="canEdit"><DateField v-model="leave.date" :label="text.date" :min="monthStart" :max="monthEnd" /><AutoComplete v-model="leave.kind" :label="text.kind" :options="kinds" required /><button class="quiet-button" type="button" @click="emit('update', leave)">{{ text.save }}</button><button class="danger-button" type="button" @click="emit('remove', leave.id)">{{ text.remove }}</button></template><template v-else><time :datetime="leave.date">{{ dates.date(leave.date) }}</time><span>{{ kindLabel(leave.kind) }}</span></template></li><li v-if="!leaves.length" class="empty-state">{{ text.noLeavePlanned }}</li></ul>
    <div v-if="monthHolidays.length" class="holiday-list"><h3>{{ text.holidays }}</h3><p v-for="holiday in monthHolidays" :key="holiday.id"><time :datetime="holiday.date">{{ dates.date(holiday.date) }}</time><span>{{ holiday.name }}</span></p></div>
  </section>
</template>
