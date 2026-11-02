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
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { formatUnixTimestamp } from '../../utils/formatUnixTimestamp'
import { useLocaleStore } from '../../stores/locale'
import type { CronEntryOutput } from '../../types/cronEntry'

/**
 * How the last-run reading stands. Four states, each named: a reading in flight, a refused one, an
 * entry that has never run, and a run with something to show.
 */
export type CronOutputRead =
  | {
      /** The reading is in flight. */
      state: 'loading'
    }
  | {
      /** The panel refused the read. */
      state: 'failed'
      /** The panel's own already-localized message, shown verbatim. */
      message: string
    }
  | {
      /** The panel answered that this entry has never run. */
      state: 'neverRan'
    }
  | {
      /** The entry has run. */
      state: 'ran'
      /** What the run left behind. */
      output: CronEntryOutput
    }

/** Props accepted by {@link CronEntryOutputDialog}; the caller mounts it only while a reading is on show. */
const props = defineProps<{
  /** How the reading stands. */
  read: CronOutputRead
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

/** The run's output, or `null` when the agent reported none (or there is no run). */
const outputText: ComputedRef<string | null> = computed(() => {
  return props.read.state === 'ran' ? props.read.output.output : null
})

/** The exit code as text, or `null` when the agent reported none. */
const exitCode: ComputedRef<string | null> = computed(() => {
  const code = props.read.state === 'ran' ? props.read.output.lastExitCode : undefined
  return code === undefined || code === null ? null : String(code)
})

/** When the run finished, in the operator's language, or `null` when the agent reported none. */
const lastRunAt: ComputedRef<string | null> = computed(() => {
  const seconds = props.read.state === 'ran' ? props.read.output.lastRunAtUnix : undefined
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
    :open="true"
    :title="t('cron.output.title')"
    :close-label="t('common.close')"
    @close="close"
  >
    <UiSpinner v-if="read.state === 'loading'" :label="t('cron.output.loading')" />

    <UiAlert v-else-if="read.state === 'failed'" variant="error">{{ read.message }}</UiAlert>

    <p v-else-if="read.state === 'neverRan'" class="text-base text-text-secondary">
      {{ t('cron.output.neverRan') }}
    </p>

    <div v-else class="flex min-w-0 flex-col gap-4">
      <!-- The exit code carries a named test hook (`cron-exit-code`) rather than being found by its
           position in the list, so reordering these rows cannot silently change what the spec reads. -->
      <UiDescriptionList class="rounded-control bg-surface-2 px-4 py-3">
        <UiDescriptionItem :term="t('cron.output.exitCode')" mono>
          <span data-testid="cron-exit-code" :class="exitCode !== null && exitCode !== '0' ? 'text-danger' : ''">
            {{ exitCode ?? t('cron.output.notReported') }}
          </span>
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('cron.output.lastRunAt')" mono>
          {{ lastRunAt ?? t('cron.output.notReported') }}
        </UiDescriptionItem>
      </UiDescriptionList>

      <p v-if="outputText === null" class="text-base text-text-secondary">
        {{ t('cron.output.noOutput') }}
      </p>
      <!-- Interpolated as text, never `v-html`: this is whatever the customer's own command wrote.
           It scrolls inside itself (both ways, so a long line does not widen the dialog or the page at
           390px), and is focusable so a keyboard user can scroll it. `min-w-0` lets the flex column
           shrink below the longest line. -->
      <pre
        v-else
        tabindex="0"
        role="region"
        :aria-label="t('cron.output.title')"
        class="m-0 max-h-72 min-w-0 overflow-auto rounded-control border border-border-subtle bg-surface-3 p-3 font-mono text-sm leading-relaxed whitespace-pre-wrap text-text-primary wrap-anywhere focus-visible:shadow-focus focus-visible:outline-none"
      >{{ outputText }}</pre>

      <p class="text-sm text-text-muted">{{ t('cron.output.provenance') }}</p>
    </div>
  </UiModal>
</template>
