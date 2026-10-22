<script setup lang="ts">
import { ref, watch } from 'vue'
import { injectTimesheetDates, injectTimesheetText, type EntryTemplate } from '../utils/timesheet'

/** The member's entry templates: Use fills the entry form; each can be renamed, replaced with what
 * the form holds, or deleted. Names are edited in place and saved from the row's menu. */
const props = defineProps<{ templates: EntryTemplate[] }>()
const emit = defineEmits<{ use: [template: EntryTemplate]; rename: [template: EntryTemplate]; replace: [template: EntryTemplate]; remove: [template: EntryTemplate] }>()
const { text, labels } = injectTimesheetText()
const dates = injectTimesheetDates()
// Typed names live here, not on the parent's list items, so a refused rename does not stay on show.
const drafts = ref<Record<string, string>>({})
watch(() => props.templates, () => { drafts.value = {} })
const named = (template: EntryTemplate): EntryTemplate => ({ ...template, name: drafts.value[template.id] ?? template.name })
// A menu choice closes its menu, so the row reads as it did before.
const close = (event: Event) => (event.currentTarget as HTMLElement).closest('details')?.removeAttribute('open')
</script>

<template>
  <section class="panel template-panel" aria-labelledby="templates-heading">
    <div class="section-heading"><div><h2 id="templates-heading">{{ labels.templates }}</h2><p class="subtle-hint">{{ labels.templatesHint }}</p></div><span class="count-badge">{{ templates.length }}</span></div>
    <ul class="task-list template-list">
      <li v-for="template in templates" :key="template.id">
        <span class="template-text"><input :value="drafts[template.id] ?? template.name" @input="drafts[template.id] = ($event.target as HTMLInputElement).value" :aria-label="labels.templateName" maxlength="100" required />
          <small>{{ dates.time(template.startTime) }}–{{ dates.time(template.endTime) }} · {{ template.taskName }}</small></span>
        <button class="use-task-button" type="button" :aria-label="labels.useTemplate.replace('{name}', template.name)" @click="emit('use', template)">{{ text.use }}</button>
        <details class="task-more"><summary :aria-label="labels.templateMore.replace('{name}', template.name)">···</summary><div class="task-menu">
          <button type="button" @click="close($event); emit('rename', named(template))">{{ labels.saveName }}</button>
          <button type="button" @click="close($event); emit('replace', named(template))">{{ labels.replaceWithForm }}</button>
          <button type="button" @click="close($event); emit('remove', template)">{{ text.remove }}</button>
        </div></details>
      </li>
      <li v-if="!templates.length" class="empty-state">{{ labels.noTemplates }}</li>
    </ul>
  </section>
</template>
