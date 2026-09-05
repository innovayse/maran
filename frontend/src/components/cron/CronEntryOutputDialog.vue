<script setup lang="ts">
/**
 * What one cron entry's most recent run left behind: its output, the code it exited with, and when
 * it finished.
 *
 * **"Never run" is a real answer here, and it is stated rather than dressed up.** The module's
 * endpoint answers 200 with a `null` BODY for an entry that has never run, and every field of a
 * reading has a plausible-looking default — an empty string is a run that printed nothing, zero is
 * a successful exit, epoch is a real instant — so an invented reading would tell somebody their job
 * ran when it never has. That is precisely the question an operator debugging a job that never
 * fires is asking.
 *
 * The output is the customer's own program's output and is rendered as TEXT, never as markup: it is
 * whatever their command wrote to a file under their home, so it is untrusted by construction.
 *
 * It is also not evidence. The output file, the exit code and the timestamp all live under the
 * account's home where the account can write them, so this is the account's own report of its runs.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { formatUnixTimestamp } from '../../utils/formatUnixTimestamp'
import { useLocaleStore } from '../../stores/locale'
import type { CronEntryOutput } from '../../types/cronEntry'

/** Props accepted by {@link CronEntryOutputDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the caller. */
  open: boolean
  /** The reading, or `null` for an entry that has never run (or one still being read). */
  output: CronEntryOutput | null
  /** True while the reading is in flight. */
  loading: boolean
  /** The panel's own already-localized message from a failed read, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link CronEntryOutputDialog}. */
const emit = defineEmits<{
  /**
   * The operator dismissed the dialog.
   * @param e The event name.
   */
  (e: 'close'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * Whether the panel answered and reported that this entry has never run.
 *
 * Distinguished from "still loading" and from "the read failed", because the three mean entirely
 * different things to somebody whose job is not firing.
 */
const hasNeverRun: ComputedRef<boolean> = computed(() => {
  return !props.loading && props.errorMessage === null && props.output === null
})

/** The run's output, or `null` when the agent reported none. */
const outputText: ComputedRef<string | null> = computed(() => {
  return props.output?.output ?? null
})

/** The exit code as text, or `null` when the agent reported none. */
const exitCode: ComputedRef<string | null> = computed(() => {
  const code = props.output?.lastExitCode
  return code === undefined || code === null ? null : String(code)
})

/** When the run finished, in the operator's language, or `null` when the agent reported none. */
const lastRunAt: ComputedRef<string | null> = computed(() => {
  const seconds = props.output?.lastRunAtUnix
  return seconds === undefined || seconds === null
    ? null
    : formatUnixTimestamp(seconds, localeStore.current)
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
  <UiModal
    :open="open"
    :title="t('cron.output.title')"
    :close-label="t('cron.output.close')"
    @close="close"
  >
    <UiSpinner v-if="loading" :label="t('cron.output.loading')" />

    <UiAlert v-else-if="errorMessage !== null" variant="error">{{ errorMessage }}</UiAlert>

    <p v-else-if="hasNeverRun" class="text-base text-text-secondary">
      {{ t('cron.output.neverRan') }}
    </p>

    <div v-else>
      <dl class="mb-3 grid grid-cols-2 gap-2 text-sm">
        <dt class="text-text-muted">{{ t('cron.output.exitCode') }}</dt>
        <dd class="font-mono text-text-primary">{{ exitCode ?? t('cron.output.notReported') }}</dd>
        <dt class="text-text-muted">{{ t('cron.output.lastRunAt') }}</dt>
        <dd class="font-mono text-text-primary">{{ lastRunAt ?? t('cron.output.notReported') }}</dd>
      </dl>

      <p v-if="outputText === null" class="text-base text-text-secondary">
        {{ t('cron.output.noOutput') }}
      </p>
      <!-- Interpolated as text, never `v-html`: this is whatever the customer's own command wrote. -->
      <pre
        v-else
        class="max-h-80 overflow-auto rounded-lg bg-surface-3 p-3 font-mono text-sm whitespace-pre-wrap break-words text-text-primary"
      >{{ outputText }}</pre>

      <p class="mt-3 text-sm text-text-muted">{{ t('cron.output.provenance') }}</p>
    </div>
  </UiModal>
</template>
