<script setup lang="ts">
/**
 * Live tail of one of a site's two logs, over SSE (spec §17: live data over SSE).
 *
 * Two things this pane refuses to do, and they are the reason it exists rather than being a
 * `<pre>` bound to an array:
 *
 * 1. **It never shows a truncation as a normal end.** The stream names how it finished —
 *    `completed`, `dropped`, `idle`, `failed`, `truncated`, `cancelled` — and the ones that mean
 *    lines are missing are rendered as an error, not as a quiet grey note. A pane that simply
 *    stops updating looks identical in all six cases, and an operator would go on watching it.
 * 2. **It says when its own scrollback has dropped lines.** The store holds a bounded number of
 *    lines; once that bound is hit the oldest are discarded, so the top of this pane is no
 *    longer the top of the log. That is a truncation too, and it is stated.
 *
 * A log line is customer-supplied text. It reaches the DOM through interpolation only — never
 * `v-html`, never a string of markup (rules/vue.md: raw HTML is an XSS hole in a panel that
 * renders exactly this kind of content).
 *
 * The stream is stopped on unmount and whenever the source changes: a stream nobody aborts
 * keeps its connection open for the life of the tab.
 */
import { computed, onBeforeUnmount, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import { useSitesStore } from '../../stores/sites'
import type { SiteLogEndReason, SiteLogSource } from '../../types/siteLog'

/**
 * The endings that mean the operator is looking at an incomplete log. They are shown in the
 * error tone; the other three are shown as information. `truncated` is in this set by
 * definition, and `dropped` is here because the panel fell behind and lines were skipped.
 */
const INCOMPLETE_ENDINGS: readonly SiteLogEndReason[] = ['dropped', 'failed', 'truncated']

/** Props accepted by {@link SiteLogsTab}. */
const props = defineProps<{
  /** The site whose log is tailed. */
  siteId: string
}>()

const { t } = useI18n()
const store = useSitesStore()

/** Which of the site's two logs is selected. */
const source: Ref<SiteLogSource> = ref('access')

/** The two logs a site has, as the contract's `SiteLogSource` defines them. */
const sourceOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'access', label: t('sites.detail.logs.source.access') },
    { value: 'error', label: t('sites.detail.logs.source.error') },
  ]
})

/** Whether a stream is currently open. */
const isStreaming: ComputedRef<boolean> = computed(() => {
  return store.logStatus === 'streaming'
})

/** The sentence naming how the last stream ended, or `null` while one is open or none has run. */
const endText: ComputedRef<string | null> = computed(() => {
  const reason = store.logEndReason
  if (reason === null) {
    return null
  }
  // The panel's own sentence names the KIND of ending; the backend's message, when it sent one,
  // is appended verbatim because only the server may word what went wrong on the server.
  const explanation = store.logEndMessage
  const named = t(`sites.detail.logs.endReason.${reason}`)
  return explanation === null ? named : `${named} ${explanation}`
})

/** Whether the ending means lines are missing, which decides the tone it is shown in. */
const endIsIncomplete: ComputedRef<boolean> = computed(() => {
  const reason = store.logEndReason
  return reason !== null && INCOMPLETE_ENDINGS.includes(reason)
})

/** Whether the pane has never been started for this site. */
const isUntouched: ComputedRef<boolean> = computed(() => {
  return store.logStatus === 'idle' && store.logLines.length === 0
})

/**
 * Opens a tail of the selected log, replacing any stream already open.
 *
 * Awaited rather than fired and forgotten: the promise settles when the stream ends, and an
 * unhandled rejection from a dropped connection would otherwise surface as a console error the
 * operator cannot act on.
 * @returns Resolves once the stream has ended.
 */
const start = async (): Promise<void> => {
  await store.startLogTail(props.siteId, source.value)
}

/**
 * Stops the open stream and releases its connection.
 * @returns Nothing.
 */
const stop = (): void => {
  store.stopLogTail()
}

/**
 * Switches which log is tailed. A stream already open is stopped first: the two logs are
 * different files, and interleaving them would be a lie about both.
 * @param value The source the control reported.
 * @returns Resolves once a replacement stream has ended, or immediately when none was open.
 */
const onSourceChange = async (value: string): Promise<void> => {
  const wasStreaming = isStreaming.value
  stop()
  source.value = value as SiteLogSource
  if (wasStreaming) {
    await start()
  }
}

onBeforeUnmount(stop)
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex flex-wrap items-center gap-2">
      <UiSegmentedControl
        :model-value="source"
        :options="sourceOptions"
        :label="t('sites.detail.logs.sourceLabel')"
        @update:model-value="onSourceChange"
      />
      <UiButton v-if="isStreaming" variant="secondary" @click="stop">
        {{ t('sites.detail.logs.stop') }}
      </UiButton>
      <UiButton v-else @click="start">{{ t('sites.detail.logs.start') }}</UiButton>
      <span v-if="isStreaming" class="text-sm text-text-muted">{{ t('sites.detail.logs.streaming') }}</span>
      <span v-if="store.logLines.length > 0" class="ml-auto font-mono text-xs text-text-muted">
        {{ t('sites.detail.logs.lineCount', { count: store.logLines.length }) }}
      </span>
    </div>

    <!-- The view's own truncation, stated rather than implied by a line count. -->
    <UiAlert v-if="store.logScrollbackTruncated" variant="info">
      {{ t('sites.detail.logs.truncatedNotice') }}
    </UiAlert>

    <UiAlert v-if="endText !== null" :variant="endIsIncomplete ? 'error' : 'info'">
      {{ endText }}
    </UiAlert>

    <UiEmptyState
      v-if="isUntouched"
      :title="t('sites.detail.logs.emptyTitle')"
      :description="t('sites.detail.logs.emptyDescription')"
    />

    <p v-else-if="store.logLines.length === 0 && isStreaming" class="text-sm text-text-muted">
      {{ t('sites.detail.logs.waiting') }}
    </p>

    <ol
      v-else-if="store.logLines.length > 0"
      class="max-h-96 overflow-auto rounded-xl border border-border-subtle bg-surface-1 p-3"
    >
      <li
        v-for="(line, index) in store.logLines"
        :key="index"
        class="flex items-start gap-2 py-0.5 font-mono text-sm text-text-secondary"
      >
        <UiBadge v-if="line.historical" variant="neutral">{{ t('sites.detail.logs.historical') }}</UiBadge>
        <!-- Interpolation, always: this is a customer-controlled string. -->
        <span class="whitespace-pre-wrap break-all">{{ line.line }}</span>
      </li>
    </ol>
  </div>
</template>
