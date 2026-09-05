<script setup lang="ts">
/**
 * The monitoring screen: what this machine has been doing over the last day or week, whether the
 * services it runs are up, and how much of each account's allowance is spent.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. All state comes from the monitoring store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * One page rather than three, because the three panels are read together: an operator judging a
 * spike in the disk chart is looking for the account that caused it in the same breath, and a
 * service that stopped is the first explanation for a load average that fell off a cliff.
 *
 * The range toggle changes what the panel is ASKED for; it never re-slices what is already held.
 * The two ranges come back bucketed differently — five minutes over a day, thirty over a week — so
 * a week cannot be assembled from a day, and a day shown out of a week would be six times coarser
 * than the one the operator selected.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import AccountDiskTable from '../../components/monitoring/AccountDiskTable.vue'
import MonitoringCharts from '../../components/monitoring/MonitoringCharts.vue'
import ServiceStatusBadges from '../../components/monitoring/ServiceStatusBadges.vue'
import { useMonitoringStore } from '../../stores/monitoring'
import type { ChartRange } from '../../types/monitoring'

const { t } = useI18n()
const store = useMonitoringStore()

/** The two ranges the panel offers, in the order they read. */
const rangeOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'lastDay', label: t('monitoring.range.day') },
    { value: 'lastWeek', label: t('monitoring.range.week') },
  ]
})

/** Whether the panel answered the services read successfully and reported no watched service. */
const hasNoServices: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.errorMessage === null && store.services.length === 0
})

/** Whether the panel answered the disk read successfully and reported no account at all. */
const hasNoAccounts: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.diskErrorMessage === null && store.accountDiskUsage.length === 0
})

/**
 * Switches the charts to another range, which re-reads them from the panel.
 *
 * The segmented control speaks in strings, since it is a generic primitive; the cast is confined to
 * this one function rather than widening the store's parameter, so nothing else in the SPA can hand
 * the API a range the panel's validator would refuse.
 * @param value The chosen segment's value, which is a {@link ChartRange} by construction of
 * {@link rangeOptions}.
 * @returns Resolves once the re-read has settled.
 */
const changeRange = async (value: string): Promise<void> => {
  await store.selectRange(value as ChartRange)
}

onMounted(() => {
  void store.load()
})
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 class="text-3xl font-semibold tracking-title text-text-primary">{{ t('monitoring.heading') }}</h1>
        <p class="mt-1 text-base text-text-secondary">{{ t('monitoring.subtitle') }}</p>
      </div>
      <UiSegmentedControl
        :model-value="store.range"
        :options="rangeOptions"
        :label="t('monitoring.range.label')"
        @update:model-value="changeRange"
      />
    </div>

    <UiAlert v-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiSpinner v-if="store.loading" :label="t('monitoring.loading')" />

    <template v-else>
      <UiCard>
        <h2 class="mb-3 text-lg font-semibold text-text-primary">{{ t('monitoring.services.title') }}</h2>
        <UiEmptyState
          v-if="hasNoServices"
          :title="t('monitoring.services.emptyTitle')"
          :description="t('monitoring.services.emptyDescription')"
        />
        <ServiceStatusBadges v-else :statuses="store.services" />
      </UiCard>

      <MonitoringCharts :buckets="store.buckets" />

      <UiCard>
        <h2 class="mb-3 text-lg font-semibold text-text-primary">{{ t('monitoring.disk.title') }}</h2>
        <UiAlert v-if="store.diskErrorMessage !== null" variant="error">{{ store.diskErrorMessage }}</UiAlert>
        <UiEmptyState
          v-else-if="hasNoAccounts"
          :title="t('monitoring.disk.emptyTitle')"
          :description="t('monitoring.disk.emptyDescription')"
        />
        <AccountDiskTable v-else :rows="store.accountDiskUsage" />
      </UiCard>
    </template>
  </section>
</template>
