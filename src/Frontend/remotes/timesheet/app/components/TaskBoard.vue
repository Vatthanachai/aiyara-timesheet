<script setup lang="ts">
import { injectTimesheetText, taskStatuses, type Catalog, type Task, type TaskStatus, type TrayItem } from '../utils/timesheet'

const props = defineProps<{ tasks: Task[]; catalog: Catalog; canLog: boolean }>()
// The parent clears the new task name only after the API accepts it, so a failed add keeps the text.
const taskName = defineModel<string>('taskName', { required: true })
const emit = defineEmits<{ add: []; status: [task: Task, status: TaskStatus]; remove: [id: string]; log: [item: TrayItem] }>()
const { text } = injectTimesheetText()
const cardType = 'application/x-aiyara-task-card'
const dropTarget = ref<TaskStatus | null>(null)

const columns = computed(() => taskStatuses.map(status => ({
  status, title: text.value[status], tasks: props.tasks.filter(task => task.status === status)
})))
const names = computed(() => new Map([...props.catalog.projects, ...props.catalog.categories].map(item => [item.id, item.name])))

function tags(task: Task) {
  return [task.projectId, task.categoryId].map(id => id && names.value.get(id)).filter(Boolean) as string[]
}
function move(task: Task, offset: number) {
  const next = taskStatuses[taskStatuses.indexOf(task.status as TaskStatus) + offset]
  if (next) emit('status', task, next)
}
function dragCard(event: DragEvent, task: Task) {
  event.dataTransfer?.setData(cardType, task.id)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}
function dragOver(event: DragEvent, status: TaskStatus) {
  if (!event.dataTransfer?.types.includes(cardType)) return
  event.preventDefault()
  dropTarget.value = status
}
function dropCard(event: DragEvent, status: TaskStatus) {
  dropTarget.value = null
  const task = props.tasks.find(item => item.id === event.dataTransfer?.getData(cardType))
  if (task && task.status !== status) emit('status', task, status)
}
</script>

<template>
  <section class="panel board-panel" :aria-label="text.board">
    <div class="panel-heading">
      <div><span class="eyebrow">{{ text.tasks }}</span><h2>{{ text.board }}</h2><p class="subtle-hint">{{ text.boardHint }}</p></div>
      <form class="add-task-form board-add" @submit.prevent="emit('add')"><input v-model="taskName" :aria-label="text.task" maxlength="200" :placeholder="text.newTask" /><button class="icon-button add-task-button" type="submit" :aria-label="text.add">＋</button></form>
    </div>
    <div class="board-columns">
      <section v-for="(column, index) in columns" :key="column.status" class="board-column" :class="[`board-${column.status}`, { 'drop-active': dropTarget === column.status }]"
        :aria-label="column.title" @dragover="dragOver($event, column.status)" @dragleave="dropTarget = null" @drop.prevent="dropCard($event, column.status)">
        <h3>{{ column.title }}<span class="count-badge">{{ column.tasks.length }}</span></h3>
        <ul class="board-cards">
          <li v-for="task in column.tasks" :key="task.id" class="board-card" draggable="true" @dragstart="dragCard($event, task)" @dragend="dropTarget = null">
            <strong>{{ task.name }}</strong>
            <div v-if="tags(task).length" class="board-tags"><span v-for="tag in tags(task)" :key="tag">{{ tag }}</span></div>
            <div class="board-actions">
              <button v-if="index > 0" class="quiet-button board-move" type="button" :aria-label="`${text.moveTo} ${columns[index - 1].title}`" @click="move(task, -1)">‹</button>
              <button v-if="index < columns.length - 1" class="quiet-button board-move" type="button" :aria-label="`${text.moveTo} ${columns[index + 1].title}`" @click="move(task, 1)">›</button>
              <button v-if="canLog" class="use-task-button" type="button" @click="emit('log', { name: task.name, projectId: task.projectId, categoryId: task.categoryId, personalTaskId: task.id })">{{ text.logTime }}</button>
              <button class="quiet-button board-remove" type="button" :aria-label="`${text.remove} ${task.name}`" @click="emit('remove', task.id)">✕</button>
            </div>
          </li>
          <li v-if="!column.tasks.length" class="empty-state">{{ text.emptyColumn }}</li>
        </ul>
      </section>
    </div>
  </section>
</template>
