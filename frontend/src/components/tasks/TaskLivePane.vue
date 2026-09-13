<script setup lang="ts">
/**
 * One task as it runs: what it is, what it acts on, how far it has got, and everything reported
 * about it so far.
 *
 * The values here are whatever the store last merged — the listing's row, then every frame the
 * task's stream delivered — so the pane and the shell's running-tasks badge are two views of one
 * array and cannot disagree.
 *
 * **The log is rendered as text, never as markup.** It carries whatever the instrumented operation
 * reported, including names a caller chose; the module escapes it into JSON so a newline cannot
 * forge a stream frame, and this panel completes the job by never handing it to `v-html`.
 *
 * The correlation id is shown because it is the thread that ties this task to the request that
 * started it and to the panel's own logs — it is the one value an operator reporting a problem
 * actually needs.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import TaskStatusBadge from './TaskStatusBadge.vue'
import { formatDate } from '../../utils/formatDate'
import { useTaskKindLabel } from '../../composables/useTaskKindLabel'
import { useLocaleStore } from '../../stores/locale'
import type { PanelTask } from '../../types/panelTask'

/** Props accepted by {@link TaskLivePane}. */
const props = defineProps<{
  /** The task being watched. */
  task: PanelTask
}>()

/** Events emitted by {@link TaskLivePane}. */
const emit = defineEmits<{
  /**
   * The operator closed the pane. The stream is left open: the badge is still counting.
   * @param e The event name.
   */
  (e: 'close'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
const kindLabel = useTaskKindLabel()

/**
 * The progress bar's width, clamped to the range it claims to be in.
 *
 * Clamped rather than trusted: the bar is drawn from a number, and a value outside 0-100 would draw
 * a bar wider than its own track rather than reporting anything useful.
 */
const percentWidth: ComputedRef<string> = computed(() => {
  return `${String(Math.min(Math.max(props.task.percent, 0), 100))}%`
})

/** When the task started, in the operator's language. */
const startedAt: ComputedRef<string> = computed(() => {
  return formatDate(props.task.startedAt, localeStore.current)
})

/**
 * Closes the pane.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiCard class="mb-6">
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <h2 class="text-lg font-semibold text-text-primary">{{ kindLabel(task.kind) }}</h2>
      <TaskStatusBadge :status="task.status" />
      <span class="flex-1"></span>
      <UiButton variant="secondary" @click="close">{{ t('common.close') }}</UiButton>
    </div>

    <!-- The kit's list, not a hand-rolled one: the `<dl>` grid was retyped here and the three
         pairs were the only ones in the panel that did not carry `UiDescriptionItem`'s `mono`
         treatment for machine text (rules/vue.md — the kit owns the markup). -->
    <UiDescriptionList class="mb-3">
      <UiDescriptionItem class="break-all" :term="t('tasks.columns.subject')" mono>
        {{ task.subject }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('tasks.columns.startedAt')" mono>
        {{ startedAt }}
      </UiDescriptionItem>
      <UiDescriptionItem class="break-all" :term="t('tasks.pane.correlationId')" mono>
        {{ task.correlationId ?? t('tasks.pane.notReported') }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <div
      class="mb-3 h-2 w-full overflow-hidden rounded-full bg-surface-3"
      role="progressbar"
      :aria-label="t('tasks.pane.progressLabel')"
      :aria-valuenow="task.percent"
      :aria-valuemin="0"
      :aria-valuemax="100"
    >
      <!-- Positioned from a measured value, which no set of utility classes can express. -->
      <div class="h-full rounded-full bg-accent transition-all" :style="{ width: percentWidth }"></div>
    </div>

    <!-- The failure code is machine-stable and is shown as such: it is what an operator quotes,
         not a sentence to read. The panel holds no translation for it (rules/vue.md). -->
    <UiAlert v-if="task.errorCode !== null" variant="error" class="mb-3">
      {{ t('tasks.pane.failedWith', { code: task.errorCode }) }}
    </UiAlert>

    <p v-if="task.log.length === 0" class="text-base text-text-secondary">
      {{ t('tasks.pane.noLog') }}
    </p>
    <pre
      v-else
      class="max-h-80 overflow-auto rounded-lg bg-surface-3 p-3 font-mono text-sm whitespace-pre-wrap break-words text-text-primary"
    >{{ task.log }}</pre>
  </UiCard>
</template>
