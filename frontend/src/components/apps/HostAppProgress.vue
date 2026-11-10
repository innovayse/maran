<script setup lang="ts">
/**
 * The progress of one install or removal: a labelled meter fed by the task that carries it, and the
 * panel's own sentence when the task failed. It draws nothing when there is no task yet, so a
 * caller can place it unconditionally.
 *
 * The percent and the failure arrive from the task the panel reports (the polled snapshot, or a
 * newer frame of the shell's running-tasks stream); nothing here estimates progress.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiMeter, { type UiMeterStatus } from '../ui/UiMeter.vue'
import type { PanelTask } from '../../types/panelTask'

/** Props accepted by {@link HostAppProgress}. */
const props = defineProps<{
  /** The app's name as a reader reads it, for the meter's accessible name. */
  name: string
  /** The task carrying the operation, or `null` before the first read of it. */
  task: PanelTask | null
}>()

const { t } = useI18n()

/** The meter's colour: red once the task failed, green once it finished, otherwise running. */
const status: ComputedRef<UiMeterStatus> = computed(() => {
  if (props.task === null) {
    return 'running'
  }
  if (props.task.failure !== null) {
    return 'danger'
  }
  return props.task.finishedAt === null ? 'running' : 'success'
})
</script>

<template>
  <div v-if="task !== null" class="flex min-w-0 flex-col gap-2">
    <UiMeter
      tone="progress"
      :status="status"
      :value="task.percent"
      :max="100"
      :label="t('apps.progress.label', { name })"
      :value-text="t('apps.progress.value', { percent: task.percent })"
    />
    <UiAlert v-if="task.failure !== null" variant="error">{{ task.failure.message }}</UiAlert>
  </div>
</template>
