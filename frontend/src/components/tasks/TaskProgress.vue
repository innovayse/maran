<script setup lang="ts">
/**
 * How far a task got: the one place that decides how a task's progress is drawn, so a row and its
 * live pane cannot disagree (#222).
 *
 * **A failed task draws no bar.** A meter stopped at 20 % beside FAILED reads as work still under
 * way, so a failed task keeps only its figure, muted, as a record of how far it got. A running task
 * draws the accent bar and a completed one the full green bar, which reads as done.
 *
 * Two sizes, the same rule: `row` is a table cell's small decorative bar beside the figure (the
 * figure is the cell's text, so the bar is hidden from assistive technology and a page of rows is not
 * a page of progressbars); `pane` is the live pane's announced progressbar with the figure as large
 * display text beside it, and a failed task's figure reads "Stopped at 20%".
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiMeter, { type UiMeterStatus } from '../ui/UiMeter.vue'
import type { PanelTaskStatus } from '../../types/panelTask'

/** Props accepted by {@link TaskProgress}. */
const props = defineProps<{
  /** The task's status, which decides whether a bar is drawn and its colour. */
  status: PanelTaskStatus
  /** How far the task got, 0 to 100. */
  percent: number
  /** Where it is drawn: a table row's cell, or the live pane. */
  size: 'row' | 'pane'
}>()

const { t } = useI18n()

/** Whether the task ended in failure, which is the one state that draws no bar. */
const failed: ComputedRef<boolean> = computed(() => {
  return props.status === 'failed'
})

/** The meter's status: the accent while it runs, green when it is done. */
const meterStatus: ComputedRef<UiMeterStatus> = computed(() => {
  return props.status === 'completed' ? 'success' : 'running'
})

/** The figure as text, the same words a screen reader hears for the bar. */
const figure: ComputedRef<string> = computed(() => {
  return t('tasks.list.percentValue', { percent: props.percent })
})
</script>

<template>
  <div v-if="size === 'row'" class="flex items-center justify-end gap-3">
    <div class="w-16 shrink-0" aria-hidden="true">
      <UiMeter
        v-if="!failed"
        tone="progress"
        :status="meterStatus"
        :value="percent"
        :max="100"
        :label="t('tasks.columns.percent')"
        :value-text="figure"
        :show-value="false"
      />
    </div>
    <span class="min-w-10">{{ figure }}</span>
  </div>

  <p
    v-else-if="failed"
    class="font-display text-lg font-black text-text-muted tabular-nums"
    data-testid="task-failed-progress"
  >
    {{ t('tasks.pane.stoppedAt', { percent }) }}
  </p>
  <div v-else class="flex items-center gap-3">
    <UiMeter
      class="flex-1"
      tone="progress"
      :status="meterStatus"
      :value="percent"
      :max="100"
      :label="t('tasks.pane.progressLabel')"
      :value-text="figure"
      :show-value="false"
    />
    <!-- The figure is visible text; the bar above already announces it, so it is not read twice. -->
    <span class="font-display text-lg font-black text-text-primary tabular-nums" aria-hidden="true">
      {{ figure }}
    </span>
  </div>
</template>
