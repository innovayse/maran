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
 *
 * **What needs attention goes first.** A failed read is an error notice above everything. Once the
 * readings are in, the single most urgent fact — a watched service that is stopped, otherwise an
 * account at or past an allowance this server enforces — is a warning notice (amber, never blue)
 * above the panels, so an operator does not have to scan three cards to find it. Only ONE such
 * notice is drawn: two warnings side by side is a list, and a list is not "the one thing".
 * A service whose state is merely not known is never counted as stopped (a socket-activated unit is
 * inactive from boot), and an account whose quota is not enforced is never counted as over it.
 *
 * Every panel is a {@link UiCard}, sixteen pixels apart, on the content panel; the page itself
 * holds no markup of its own beyond the kit's.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
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
 * The localized names of the watched services that are stopped, in the panel's own order. A service
 * whose state is not known is deliberately left out: see the component doc comment.
 */
const stoppedServiceNames: ComputedRef<string[]> = computed(() => {
  return store.services
    .filter((status) => {
      return status.state === 'stopped'
    })
    .map((status) => {
      return status.name
    })
})

/**
 * The usernames of accounts whose measured use has reached an allowance this server enforces. An
 * unmeasured account, a zero allowance and an unenforced allowance are all left out: none of them
 * is a measurement that says the account is over anything.
 */
const overQuotaUsernames: ComputedRef<string[]> = computed(() => {
  return store.accountDiskUsage
    .filter((row) => {
      return row.enforceable && row.quotaBytes > 0 && row.usedBytes !== null && row.usedBytes >= row.quotaBytes
    })
    .map((row) => {
      return row.username
    })
})

/**
 * The one warning the page leads with, or `null` when nothing needs attention. Stopped services
 * outrank accounts over their allowance: a stopped service explains the rest.
 */
const attentionText: ComputedRef<string | null> = computed(() => {
  if (stoppedServiceNames.value.length > 0) {
    return t('monitoring.attention.servicesDown', { names: stoppedServiceNames.value.join(', ') })
  }
  if (overQuotaUsernames.value.length > 0) {
    return t('monitoring.attention.overQuota', { names: overQuotaUsernames.value.join(', ') })
  }
  return null
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
  <section class="flex flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('monitoring.eyebrow')"
      :title="t('monitoring.heading')"
      :subtitle="t('monitoring.subtitle')"
    >
      <template #actions>
        <UiSegmentedControl
          :model-value="store.range"
          :options="rangeOptions"
          :label="t('monitoring.range.label')"
          @update:model-value="changeRange"
        />
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiSpinner v-if="store.loading" :label="t('monitoring.loading')" />

    <template v-else>
      <UiAlert v-if="attentionText !== null" variant="warning">{{ attentionText }}</UiAlert>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('monitoring.services.title')" />
        <UiEmptyState
          v-if="hasNoServices"
          :title="t('monitoring.services.emptyTitle')"
          :description="t('monitoring.services.emptyDescription')"
        />
        <ServiceStatusBadges v-else :statuses="store.services" />
      </UiCard>

      <MonitoringCharts :buckets="store.buckets" />

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('monitoring.disk.title')" />
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
