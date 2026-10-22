<script setup lang="ts">
import AutoComplete from '../../../../shared/components/AutoComplete.vue'
import { dragTrayItem, injectTimesheetText, type Catalog, type Task, type TaskStatus, type TrayItem } from '../utils/timesheet'

defineProps<{ tasks: Task[]; catalog: Catalog }>()
// The parent clears the new task name only after the API accepts it, so a failed add keeps the text.
const taskName = defineModel<string>('taskName', { required: true })
const emit = defineEmits<{ add: []; status: [task: Task, status: TaskStatus]; rename: [task: Task]; remove: [id: string]; use: [item: TrayItem] }>()
const { text, labels } = injectTimesheetText()

const statuses = computed(() => (['todo', 'doing', 'done'] as const).map(status => ({ value: status, label: text.value[status] })))

function taskItem(task: Task): TrayItem {
  return { name: task.name, projectId: task.projectId, categoryId: task.categoryId, personalTaskId: task.id }
}
</script>

<template>
  <aside class="panel task-panel" aria-labelledby="saved-tasks-heading">
    <div class="section-heading"><div><h2 id="saved-tasks-heading">{{ text.savedTasks }}</h2><p class="subtle-hint">{{ text.startFromSaved }}</p></div><span class="count-badge">{{ tasks.length }}</span></div>
    <form class="add-task-form" @submit.prevent="emit('add')"><input v-model="taskName" :aria-label="text.task" maxlength="200" :placeholder="labels.taskPlaceholder" /><button class="icon-button add-task-button" type="submit" :aria-label="text.add">＋</button></form>
    <ul class="task-list"><li v-for="task in tasks" :key="task.id" draggable="true" @dragstart="dragTrayItem($event, taskItem(task))"><span class="task-grip" aria-hidden="true">⠿</span><input v-model="task.name" :aria-label="text.task" maxlength="200" /><button class="use-task-button" type="button" @click="emit('use', taskItem(task))">{{ text.use }}</button><details class="task-more"><summary :aria-label="text.status">···</summary><div class="task-menu"><label>{{ text.status }}<AutoComplete :model-value="task.status" :label="text.status" :options="statuses" required @change="status => emit('status', task, status as TaskStatus)" /></label><button type="button" @click="emit('rename', task)">{{ text.save }}</button><button type="button" @click="emit('remove', task.id)">{{ text.remove }}</button></div></details></li><li v-if="!tasks.length" class="empty-state">{{ text.taskHint }}</li></ul>
    <div v-if="catalog.projects.length" class="quick-picks"><h3>{{ text.project }}</h3><button v-for="project in catalog.projects" :key="project.id" type="button" draggable="true" @dragstart="dragTrayItem($event, { name: project.name, projectId: project.id })" @click="emit('use', { name: project.name, projectId: project.id })">{{ project.name }}</button></div>
    <div v-if="catalog.categories.length" class="quick-picks"><h3>{{ text.category }}</h3><button v-for="category in catalog.categories" :key="category.id" type="button" draggable="true" @dragstart="dragTrayItem($event, { name: category.name, categoryId: category.id })" @click="emit('use', { name: category.name, categoryId: category.id })">{{ category.name }}</button></div>
  </aside>
</template>
