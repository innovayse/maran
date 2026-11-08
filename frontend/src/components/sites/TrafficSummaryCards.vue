<script setup lang="ts">
/**
 * The three figures a traffic window opens with: requests answered, bytes sent and the days the
 * window covers. Props in, nothing out: the tab owns the window and the read.
 *
 * Figures are formatted for the interface language; the byte count goes through the panel's one
 * byte formatter so it reads like every other size on the screen.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatDate } from '../../utils/formatDate'
import type { SiteTraffic } from '../../types/siteTraffic'

/** Props accepted by {@link TrafficSummaryCards}. */
const props = defineProps<{
  /** The traffic summary whose totals and period are shown. */
  traffic: SiteTraffic
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The request count with the interface language's digit grouping. */
const requestsText: ComputedRef<string> = computed(() => {
  return props.traffic.totals.requests.toLocaleString(localeStore.current)
})

/** The period as "first day to last day". */
const periodText: ComputedRef<string> = computed(() => {
  return t('siteTraffic.summary.period', {
    from: formatDate(props.traffic.from, localeStore.current),
    to: formatDate(props.traffic.to, localeStore.current),
  })
})
</script>

<template>
  <div class="grid min-w-0 gap-4 sm:grid-cols-3">
    <UiCard class="flex min-w-0 flex-col gap-1">
      <span class="text-sm text-text-muted">{{ t('siteTraffic.summary.requests') }}</span>
      <span class="font-mono text-2xl font-semibold text-text-primary tabular-nums">{{ requestsText }}</span>
    </UiCard>
    <UiCard class="flex min-w-0 flex-col gap-1">
      <span class="text-sm text-text-muted">{{ t('siteTraffic.summary.bytes') }}</span>
      <span class="font-mono text-2xl font-semibold text-text-primary tabular-nums">
        {{ formatBytes(traffic.totals.bytes) }}
      </span>
    </UiCard>
    <UiCard class="flex min-w-0 flex-col gap-1">
      <span class="text-sm text-text-muted">{{ t('siteTraffic.summary.window') }}</span>
      <span class="text-base font-semibold text-text-primary">
        {{ t('siteTraffic.summary.days', { count: traffic.days }, traffic.days) }}
      </span>
      <span class="text-sm text-text-secondary">{{ periodText }}</span>
    </UiCard>
  </div>
</template>
