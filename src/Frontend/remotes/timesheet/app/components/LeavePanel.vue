<script setup lang="ts">
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import DateField from '../../../../shared/components/DateField.vue'
import { decimalHours, injectTimesheetDates, injectTimesheetText, type Catalog, type Leave, type LeaveAmount, type LeaveBalance } from '../utils/timesheet'

/** Leave in the month, the form to add it, the year's balance per leave type, and the month's holidays.
 * With organization leave types a leave names one (`leaveKind` is its id); without, it is one of the built-in kinds. */
const props = defineProps<{ leaves: Leave[]; holidays: Catalog['holidays']; leaveTypes: Catalog['leaveTypes']; balances: LeaveBalance[]
  month: string; canEdit: boolean; monthStart: string; monthEnd: string; standardMinutes: number }>()
const leaveDate = defineModel<string>('leaveDate', { required: true })
const leaveKind = defineModel<string>('leaveKind', { required: true })
const leaveAmount = defineModel<LeaveAmount>('leaveAmount', { required: true })
const leaveHours = defineModel<number>('leaveHours', { required: true })
const emit = defineEmits<{ add: []; update: [leave: Leave]; remove: [id: string] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()
const monthHolidays = computed(() => props.holidays.filter(x => x.date.startsWith(props.month)))
const typed = computed(() => props.leaveTypes.length > 0)
const kinds = computed(() => typed.value ? props.leaveTypes.map(type => ({ value: type.id, label: type.name }))
  : (['annual', 'sick', 'other'] as const).map(kind => ({ value: kind, label: text.value[kind] })))
const amounts = computed(() => [{ value: 'day', label: labels.value.wholeDay }, { value: 'half', label: labels.value.halfDay },
  { value: 'hours', label: labels.value.someHours }])
const kindLabel = (kind: string) => kind === 'annual' || kind === 'sick' || kind === 'other' ? text.value[kind] : kind
const amountLabel = (leave: Leave) => leave.minutes ? labels.value.leaveHoursShort.replace('{hours}', decimalHours(leave.minutes)) : ''
const maxHours = computed(() => props.standardMinutes / 60)
const days = (value: number) => String(Math.round(value * 100) / 100)
const balanceLine = (balance: LeaveBalance) => balance.annualDays == null
  ? labels.value.balanceTaken.replace('{used}', days(balance.usedDays))
  : labels.value.balanceLeft.replace('{used}', days(balance.usedDays)).replace('{total}', days(balance.annualDays))
    .replace('{left}', days(balance.remainingDays ?? 0))
</script>

<template>
  <section class="panel support-panel" aria-labelledby="leave-heading"><div class="section-heading"><h2 id="leave-heading">{{ text.leaveAndHolidays }}</h2></div>
    <form v-if="canEdit" class="inline-form leave-form" @submit.prevent="emit('add')">
      <label>{{ text.date }}<DateField v-model="leaveDate" :label="text.date" :min="monthStart" :max="monthEnd" /></label>
      <label>{{ text.kind }}<AutoComplete v-model="leaveKind" :label="text.kind" :options="kinds" required /></label>
      <label>{{ labels.leaveAmount }}<AutoComplete v-model="leaveAmount" :label="labels.leaveAmount" :options="amounts" required /></label>
      <label v-if="leaveAmount === 'hours'">{{ labels.leaveHours }}<input v-model.number="leaveHours" type="number" min="0.25" :max="maxHours" step="0.25" required /></label>
      <button class="primary-button" type="submit">{{ text.add }}</button>
    </form>
    <ul class="simple-list"><li v-for="leave in leaves" :key="leave.id"><template v-if="canEdit"><DateField v-model="leave.date" :label="text.date" :min="monthStart" :max="monthEnd" /><AutoComplete v-if="typed && leave.leaveTypeId" v-model="leave.leaveTypeId" :label="text.kind" :options="kinds" required /><AutoComplete v-else-if="!typed" v-model="leave.kind" :label="text.kind" :options="kinds" required /><span v-else>{{ leave.kind }}</span><small v-if="leave.minutes" class="leave-amount">{{ amountLabel(leave) }}</small><button v-if="!typed || leave.leaveTypeId" class="quiet-button" type="button" @click="emit('update', leave)">{{ text.save }}</button><button class="danger-button" type="button" @click="emit('remove', leave.id)">{{ text.remove }}</button></template><template v-else><time :datetime="leave.date">{{ dates.date(leave.date) }}</time><span>{{ kindLabel(leave.kind) }}<small v-if="leave.minutes" class="leave-amount">{{ amountLabel(leave) }}</small></span></template></li><li v-if="!leaves.length" class="empty-state">{{ text.noLeavePlanned }}</li></ul>
    <div v-if="balances.length" class="leave-balances"><h3>{{ labels.leaveBalance.replace('{year}', month.slice(0, 4)) }}</h3><p v-for="balance in balances" :key="balance.leaveTypeId" :class="{ overdrawn: (balance.remainingDays ?? 0) < 0 }"><span>{{ balance.name }}</span><span>{{ balanceLine(balance) }}</span></p></div>
    <div v-if="monthHolidays.length" class="holiday-list"><h3>{{ text.holidays }}</h3><p v-for="holiday in monthHolidays" :key="holiday.id"><time :datetime="holiday.date">{{ dates.date(holiday.date) }}</time><span>{{ holiday.name }}</span></p></div>
  </section>
</template>
