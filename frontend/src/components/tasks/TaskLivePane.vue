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
 * forge a stream frame, and this panel completes the job by never handing it to `v-html`. The
 * interpolation sits flush against the `<pre>` tags on purpose: any whitespace inside them would be
 * rendered, and the log must reach the screen character for character.
 *
 * **The log scrolls inside its own box.** A long log, or a long unbroken line in it, must neither
 * stretch the card nor push the page sideways at 390px: the box has a height cap, scrolls on both
 * axes, wraps at any character, and is focusable so a keyboard user can scroll it. While the task
 * runs the box follows the newest line — but only when the reader was already at the bottom, so a
 * person scrolled up to read an earlier line is never yanked away from it.
 *
 * **A withheld log is said to be withheld.** The panel sends a customer their own task without its
 * log, because the log is written for the operator, and says so in `logIncluded`. The pane then
 * shows a short note that the log stays with the server's administrators, in place of "nothing has
 * been reported yet" — which would read as a task that is stuck (issue #201).
 *
 * **How far it got is drawn by {@link TaskProgress}**, the one rule a row and this pane share: a
 * failed task draws no bar, only its figure (#222).
 *
 * **A failure reads as a sentence, with the code as a detail.** The module reports a stable code
 * (`AcmeAccountRejected`), not text, because the task outlives the request that would have carried
 * the operator's language. The known codes have a sentence here; an unknown one falls back to a
 * generic sentence, and the code is always printed beneath it, because it is what an operator quotes
 * in a support ticket.
 *
 * The correlation id is shown because it is the thread that ties this task to the request that
 * started it and to the panel's own logs — it is the one value an operator reporting a problem
 * actually needs.
 */
import { computed, nextTick, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import TaskProgress from './TaskProgress.vue'
import TaskStatusBadge from './TaskStatusBadge.vue'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
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

/**
 * How close to the bottom, in pixels, still counts as "reading the newest line". A pixel or two of
 * rounding in `scrollTop` must not read as having scrolled away.
 */
const FOLLOW_TOLERANCE_PX = 24

const i18n = useI18n()
const { t } = i18n
const localeStore = useLocaleStore()
const kindLabel = useTaskKindLabel()

/** The log's scroll box, once there is a log to show. */
const logBox: Ref<HTMLElement | null> = ref(null)

/** The failure as a sentence: the known code's own, or the generic one for a code the panel does not know. */
const failureSentence: ComputedRef<string> = computed(() => {
  const key = `tasks.failures.${props.task.errorCode ?? ''}`
  return props.task.errorCode !== null && i18n.te(key) ? t(key) : t('tasks.failures.generic')
})

/** When the task started, in the operator's language — the instant, not only the day. */
const startedAt: ComputedRef<string> = computed(() => {
  return formatIsoTimestamp(props.task.startedAt, localeStore.current)
})

/**
 * Whether a scroll box is showing its newest line.
 * @param box The log's scroll box.
 * @returns True when the box sits at, or within a few pixels of, its bottom.
 */
const isAtBottom = (box: HTMLElement): boolean => {
  return box.scrollHeight - box.scrollTop - box.clientHeight <= FOLLOW_TOLERANCE_PX
}

/**
 * Scrolls the log to its newest line.
 * @returns Resolves once the DOM has caught up with the state.
 */
const scrollToNewest = async (): Promise<void> => {
  await nextTick()
  if (logBox.value !== null) {
    logBox.value.scrollTop = logBox.value.scrollHeight
  }
}

/**
 * Closes the pane.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}

onMounted(scrollToNewest)

// The decision to follow is made BEFORE the new text lands (this watcher flushes ahead of the DOM
// update), because afterwards the box is taller and the reader always looks "scrolled up".
watch(
  () => {
    return props.task.log
  },
  async () => {
    const follow = logBox.value === null || isAtBottom(logBox.value)
    if (follow) {
      await scrollToNewest()
    }
  },
)
</script>

<template>
  <UiCard>
    <div class="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2">
      <h2 class="min-w-0 font-display text-lg font-extrabold text-text-primary wrap-anywhere">
        {{ kindLabel(task.kind) }}
      </h2>
      <TaskStatusBadge :status="task.status" />
      <span class="hidden flex-1 sm:block"></span>
      <UiButton variant="secondary" @click="close">{{ t('common.close') }}</UiButton>
    </div>

    <TaskProgress class="mb-4" :status="task.status" :percent="task.percent" size="pane" />

    <!-- The kit's list, not a hand-rolled one: it owns the grid and the `mono` treatment for machine
         text, which a subject, an instant and a correlation id all are. -->
    <UiDescriptionList class="mb-4">
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

    <!-- The sentence first, the machine-stable code beneath it as the detail an operator quotes. -->
    <UiAlert v-if="task.errorCode !== null" variant="error" class="mb-4">
      <span class="block wrap-anywhere">{{ failureSentence }}</span>
      <span class="block font-mono text-sm wrap-anywhere">
        {{ t('tasks.pane.failureCode', { code: task.errorCode }) }}
      </span>
    </UiAlert>

    <p v-if="!task.logIncluded" class="text-sm text-text-secondary">
      {{ t('tasks.pane.logWithheld') }}
    </p>
    <p v-else-if="task.log.length === 0" class="text-sm text-text-secondary">
      {{ t('tasks.pane.noLog') }}
    </p>
    <pre
      v-else
      ref="logBox"
      class="max-h-80 w-full max-w-full overflow-auto rounded-control border border-border-subtle bg-surface-2 p-3 font-mono text-sm whitespace-pre-wrap text-text-primary wrap-anywhere"
      tabindex="0"
      role="region"
      :aria-label="t('tasks.pane.logLabel')"
    >{{ task.log }}</pre>
  </UiCard>
</template>
