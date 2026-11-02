<script setup lang="ts">
/**
 * One run of a cron entry started by hand (issue #158): how it ended, its exit code, how long it took,
 * when it started, and what it printed on each stream. Opened by "Run now" — where it holds a spinner
 * for as long as the job runs — and by a row of the run history.
 *
 * The output is the customer's own program's output and is rendered as TEXT, never as markup. A run
 * one of the agent's ceilings stopped says which, because "stopped after two minutes" and "printed too
 * much" call for different fixes.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import CronRunOutcomeBadge from './CronRunOutcomeBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { formatSeconds } from '../../utils/formatSeconds'
import type { CronRun } from '../../types/cronRun'

/** Props accepted by {@link CronRunResultDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the caller. */
  open: boolean
  /** The run, or `null` while it is still running or being read, or when it failed. */
  run: CronRun | null
  /** The panel's already-localized refusal, `''` when no answer arrived, or `null` when nothing failed. */
  errorMessage: string | null
}>()

/** Events emitted by {@link CronRunResultDialog}. */
const emit = defineEmits<{
  /** The reader dismissed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The two streams that printed something, labelled; a stream that printed nothing is not drawn. */
const streams: ComputedRef<{ key: string; label: string; text: string }[]> = computed(() => {
  const run = props.run
  if (run === null) {
    return []
  }
  return [
    { key: 'stdout', label: t('cron.runs.stdout'), text: run.stdout },
    { key: 'stderr', label: t('cron.runs.stderr'), text: run.stderr },
  ].filter((stream) => {
    return stream.text !== ''
  })
})

/** Why a stopped run was stopped, or `null` for a run that ended on its own. */
const limitNote: ComputedRef<string | null> = computed(() => {
  const limit = props.run?.limit ?? null
  return limit === null ? null : t(`cron.runs.limit.${limit}`)
})

/**
 * Dismisses the dialog.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiModal :open="open" :title="t('cron.runs.resultTitle')" :close-label="t('common.close')" @close="close">
    <UiAlert v-if="errorMessage !== null" variant="error">
      {{ errorMessage === '' ? t('common.list.unreachable') : errorMessage }}
    </UiAlert>

    <UiSpinner v-else-if="run === null" :label="t('cron.runs.running')" />

    <div v-else class="flex min-w-0 flex-col gap-4">
      <UiDescriptionList class="rounded-control bg-surface-2 px-4 py-3">
        <UiDescriptionItem :term="t('cron.runs.columns.outcome')">
          <CronRunOutcomeBadge :outcome="run.outcome" />
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('cron.output.exitCode')" mono>
          <span data-testid="cron-run-exit-code">{{ run.exitCode ?? t('cron.output.notReported') }}</span>
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('cron.runs.columns.duration')" mono>
          {{ formatSeconds(run.durationMilliseconds, localeStore.current) }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('cron.runs.columns.startedAt')" mono>
          {{ formatIsoTimestamp(run.startedAt, localeStore.current) }}
        </UiDescriptionItem>
      </UiDescriptionList>

      <UiAlert v-if="limitNote !== null" variant="warning">{{ limitNote }}</UiAlert>

      <p v-if="streams.length === 0" class="text-base text-text-secondary">{{ t('cron.output.noOutput') }}</p>
      <!-- Interpolated as text, never `v-html`: this is whatever the customer's own command printed.
           Each stream scrolls inside itself and is focusable, so a keyboard user can scroll it. -->
      <div v-for="stream in streams" :key="stream.key" class="flex min-w-0 flex-col gap-1">
        <h3 class="text-sm font-medium text-text-secondary">{{ stream.label }}</h3>
        <pre
          tabindex="0"
          role="region"
          :aria-label="stream.label"
          class="m-0 max-h-72 min-w-0 overflow-auto rounded-control border border-border-subtle bg-surface-3 p-3 font-mono text-sm leading-relaxed whitespace-pre-wrap text-text-primary wrap-anywhere focus-visible:shadow-focus focus-visible:outline-none"
        >{{ stream.text }}</pre>
      </div>
    </div>
  </UiModal>
</template>
