<script setup lang="ts">
/**
 * Where an install, update, removal or rollback stands, drawn from the task the store follows
 * (`store.operationTasks[id]`). It is a card with the operation's name, a labelled progress bar, the
 * step lines the task reported so far and, when the task ended, its outcome.
 *
 * The task is the panel's: the step lines are its log, the failure is `failure.message` printed as
 * received with its code beneath (behaviour and support only, never a text key), and a failed task
 * draws no bar, because a bar stopped part-way beside a failure reads as work still under way. The
 * store re-finds a running task after a reload, so this card comes back with the page. A running
 * operation cannot be dismissed; a finished one is dismissed by the administrator, and a failed one
 * offers a retry (the screen reopens the confirmation) and a link to the task in the task list.
 * The words "Modules that were running keep running" are the SPA's own reassurance and the panel's
 * rule: an operation that fails leaves what runs untouched.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiMeter, { type UiMeterStatus } from '../ui/UiMeter.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { PanelTask } from '../../types/panelTask'

/** Props accepted by {@link ModuleOperationProgress}. */
const props = defineProps<{
  /** The module's machine id; names the card for tests and assistive technology. */
  moduleId: string
  /** The module's name, localized by the panel. */
  moduleName: string
  /** The latest snapshot of the task, or `null` before the first read answered. */
  task: PanelTask | null
  /** True while the store is still following the task (it has not ended). */
  following: boolean
}>()

/** Events emitted by {@link ModuleOperationProgress}. */
const emit = defineEmits<{
  /** The administrator wants to try a failed operation again. */
  (e: 'retry'): void
  /** The administrator is done reading a finished operation's outcome. */
  (e: 'dismiss'): void
}>()

/** The locale key of each operation's title, by the task kind the panel names. */
const TITLE_KEYS: Readonly<Record<string, string>> = {
  ModuleInstall: 'marketplace.operation.install',
  ModuleUpdate: 'marketplace.operation.update',
  ModuleRemove: 'marketplace.operation.remove',
  ModuleRollback: 'marketplace.operation.rollback',
}

const { t } = useI18n()

/** The card's title: what is being done to which module; the neutral label for a kind this screen does not know. */
const title: ComputedRef<string> = computed(() => {
  const key = props.task === null ? undefined : TITLE_KEYS[props.task.kind]
  return key === undefined
    ? t('marketplace.operation.progressLabel', { name: props.moduleName })
    : t(key, { name: props.moduleName })
})

/** How far the task got, 0 to 100. */
const percent: ComputedRef<number> = computed(() => {
  return props.task?.percent ?? 0
})

/** The task's step lines, one per non-empty line of its log. */
const lines: ComputedRef<string[]> = computed(() => {
  return (props.task?.log ?? '').split('\n').filter((line) => {
    return line.trim() !== ''
  })
})

/** Whether the task ended in failure. */
const failed: ComputedRef<boolean> = computed(() => {
  return props.task?.failure !== null && props.task?.failure !== undefined
})

/** Whether the task ended without failing. */
const succeeded: ComputedRef<boolean> = computed(() => {
  return props.task !== null && props.task.finishedAt !== null && !failed.value
})

/** The meter's colour: green once done, the accent while it runs. */
const meterStatus: ComputedRef<UiMeterStatus> = computed(() => {
  return succeeded.value ? 'success' : 'running'
})

/** The figure as text, printed beside the bar. */
const figure: ComputedRef<string> = computed(() => {
  return t('tasks.list.percentValue', { percent: percent.value })
})
</script>

<template>
  <UiCard :data-testid="`module-operation-${moduleId}`">
    <div class="flex flex-col gap-3">
      <UiSectionHeading :title="title" />

      <div v-if="!failed" class="flex items-center gap-3">
        <UiMeter
          class="flex-1"
          tone="progress"
          :status="meterStatus"
          :value="percent"
          :max="100"
          :label="t('marketplace.operation.progressLabel', { name: moduleName })"
          :value-text="figure"
          :show-value="false"
        />
        <span class="font-display text-lg font-black text-text-primary tabular-nums" aria-hidden="true">{{ figure }}</span>
      </div>

      <ol
        v-if="lines.length > 0"
        class="max-h-40 list-decimal space-y-0.5 overflow-y-auto pl-5 text-sm text-text-secondary"
        :aria-label="t('marketplace.operation.logLabel')"
      >
        <li v-for="(line, index) in lines" :key="index" class="wrap-anywhere">{{ line }}</li>
      </ol>

      <UiAlert v-if="failed && task?.failure" variant="error" data-testid="module-operation-failure">
        <p class="font-medium">{{ t('marketplace.operation.failedTitle') }}</p>
        <p>{{ task.failure.message }}</p>
        <p class="text-text-muted">{{ t('marketplace.operation.code', { code: task.failure.code }) }}</p>
        <p>{{ t('marketplace.operation.failedKept') }}</p>
      </UiAlert>
      <UiAlert v-else-if="succeeded" variant="success">{{ t('marketplace.operation.done') }}</UiAlert>

      <div v-if="!following" class="flex flex-wrap justify-end gap-2">
        <UiButton v-if="failed" :to="{ name: 'tasks' }" variant="secondary">
          {{ t('marketplace.operation.openTasks') }}
        </UiButton>
        <UiButton v-if="failed" variant="secondary" @click="emit('retry')">{{ t('marketplace.operation.retry') }}</UiButton>
        <UiButton variant="secondary" @click="emit('dismiss')">{{ t('marketplace.operation.dismiss') }}</UiButton>
      </div>
    </div>
  </UiCard>
</template>
