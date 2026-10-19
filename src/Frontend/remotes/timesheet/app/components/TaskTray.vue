<script setup lang="ts">
import { dragTrayItem, injectTimesheetText, type Catalog, type Task, type TrayItem } from '../utils/timesheet'

defineProps<{ tasks: Task[]; catalog: Catalog }>()
// The parent clears the new task name only after the API accepts it, so a failed add keeps the text.
const taskName = defineModel<string>('taskName', { required: true })
const emit = defineEmits<{ add: []; status: [task: Task, status: string]; rename: [task: Task]; remove: [id: string]; use: [item: TrayItem] }>()
const { text, labels } = injectTimesheetText()

function taskItem(task: Task): TrayItem {
  return { name: task.name, projectId: task.projectId, categoryId: task.categoryId, personalTaskId: task.id }
}
</script>

<template>
  <aside class="panel task-panel">
    <div class="panel-heading"><div><span class="eyebrow">{{ text.tasks }}</span><h2>{{ text.startFromSaved }}</h2></div><span class="count-badge">{{ tasks.length }}</span></div>
    <form class="add-task-form" @submit.prevent="emit('add')"><input v-model="taskName" :aria-label="text.task" maxlength="200" :placeholder="labels.taskPlaceholder" /><button class="icon-button add-task-button" type="submit" :aria-label="text.add">＋</button></form>
    <ul class="task-list"><li v-for="task in tasks" :key="task.id" draggable="true" @dragstart="dragTrayItem($event, taskItem(task))"><span class="task-grip" aria-hidden="true">⠿</span><input v-model="task.name" :aria-label="text.task" maxlength="200" /><button class="use-task-button" type="button" @click="emit('use', taskItem(task))">{{ text.use }}</button><details class="task-more"><summary :aria-label="text.status">···</summary><div class="task-menu"><label>{{ text.status }}<select :value="task.status" @change="emit('status', task, ($event.target as HTMLSelectElement).value)"><option value="todo">{{ text.todo }}</option><option value="doing">{{ text.doing }}</option><option value="done">{{ text.done }}</option></select></label><button type="button" @click="emit('rename', task)">{{ text.save }}</button><button type="button" @click="emit('remove', task.id)">{{ text.remove }}</button></div></details></li><li v-if="!tasks.length" class="empty-state">{{ text.taskHint }}</li></ul>
    <div v-if="catalog.projects.length" class="quick-picks"><h3>{{ text.project }}</h3><button v-for="project in catalog.projects" :key="project.id" type="button" draggable="true" @dragstart="dragTrayItem($event, { name: project.name, projectId: project.id })" @click="emit('use', { name: project.name, projectId: project.id })">{{ project.name }}</button></div>
    <div v-if="catalog.categories.length" class="quick-picks"><h3>{{ text.category }}</h3><button v-for="category in catalog.categories" :key="category.id" type="button" draggable="true" @dragstart="dragTrayItem($event, { name: category.name, categoryId: category.id })" @click="emit('use', { name: category.name, categoryId: category.id })">{{ category.name }}</button></div>
  </aside>
</template>
