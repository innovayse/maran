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
 * **The disk table is a server page** (`useServerTable`), like every other list: page, page size,
 * the search on the user name and the sort (`username`, `quotaBytes`) live in the URL, and the panel
 * searches, sorts, counts — and measures only the accounts on the page it answers, because a
 * measurement walks a home directory and a host can hold thousands. That is also why the over-quota
 * notice speaks for the accounts on the page on screen, not for the whole host: nothing has measured
 * the rest. The table loads on its own, so a refused disk read never blanks the charts.
 *
 * Every panel is a {@link UiCard}, sixteen pixels apart, on the content panel; the page itself
 * holds no markup of its own beyond the kit's.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import AccountDiskTable from '../../components/monitoring/AccountDiskTable.vue'
import MonitoringCharts from '../../components/monitoring/MonitoringCharts.vue'
import ServiceStatusBadges from '../../components/monitoring/ServiceStatusBadges.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useMonitoringStore } from '../../stores/monitoring'
import type { ListLoad, ListRequest } from '../../types/list'
import type { AccountDiskUsage, ChartRange } from '../../types/monitoring'

const { t } = useI18n()
const store = useMonitoringStore()
const labels = useListLabels()

const diskTable = useServerTable<AccountDiskUsage>({
  /**
   * Reads one page of the accounts' disk use through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<AccountDiskUsage>> => {
    return store.listAccountDiskUsage(request)
  },
})

/** The two ranges the panel offers, in the order they read. */
const rangeOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'lastDay', label: t('monitoring.range.day') },
    { value: 'lastWeek', label: t('monitoring.range.week') },
  ]
})

/** Whether the screen is still on its way, so every section draws placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loading'
})

/** Whether the panel answered the services read successfully and reported no watched service. */
const hasNoServices: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loaded' && store.services.length === 0
})

/** Whether the latest disk page arrived and holds no account at all. */
const diskAnsweredNothing: ComputedRef<boolean> = computed(() => {
  return diskTable.state.value.status === 'loaded' && diskTable.total.value === 0
})

/** Whether the disk list is narrowed by a search, so an empty answer means "no match", not "no accounts". */
const diskIsNarrowed: ComputedRef<boolean> = computed(() => {
  return diskTable.request.value.search !== ''
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
 * The usernames of accounts on the disk page on screen whose measured use has reached an allowance
 * this server enforces. An unmeasured account, a zero allowance and an unenforced allowance are all
 * left out: none of them is a measurement that says the account is over anything. Only the page on
 * screen is measured, so only the page on screen can be named.
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

/**
 * Clears the disk list's search, the way back from a search that matched no account.
 * @returns Nothing; the search reaches the URL after the box's pause.
 */
const clearDiskSearch = (): void => {
  diskTable.search.value = ''
}

onMounted(() => {
  void store.load()
})
</script>

<template>
  <section class="flex flex-col gap-4">
    <UiPageHeading
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

    <!-- A refused range switch; a refused first read is the region's own error below. -->
    <UiAlert v-if="store.loadState === 'loaded' && store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <!-- While the screen is read, every card is drawn with its real heading and a placeholder
           where its content will be, so the answer lands in the space already held. -->
      <div class="flex flex-col gap-4">
        <UiAlert v-if="!isLoading && attentionText !== null" variant="warning">{{ attentionText }}</UiAlert>

        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('monitoring.services.title')" />
          <UiSkeleton v-if="isLoading" shape="block" />
          <UiEmptyState
            v-else-if="hasNoServices"
            :title="t('monitoring.services.emptyTitle')"
            :description="t('monitoring.services.emptyDescription')"
          />
          <ServiceStatusBadges v-else :statuses="store.services" />
        </UiCard>

        <MonitoringCharts :buckets="store.buckets" :loading="isLoading" />

        <UiCard class="min-w-0">
          <UiSectionHeading class="mb-4" :title="t('monitoring.disk.title')" />
          <div class="flex flex-col gap-4">
            <UiSearchInput
              v-model="diskTable.search.value"
              class="min-w-55 max-w-85"
              :label="t('monitoring.disk.searchLabel')"
              :placeholder="t('monitoring.disk.searchPlaceholder')"
              :clear-label="t('monitoring.disk.searchClear')"
            />

            <UiLoadingRegion :state="diskTable.loadingState.value">
              <!-- A refusal is the backend's own localized sentence, so the SPA holds no copy of it. -->
              <template #error>
                <UiAlert v-if="diskTable.state.value.status === 'refused'" variant="error">
                  {{ diskTable.state.value.message }}
                </UiAlert>
                <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
              </template>

              <UiEmptyState
                v-if="diskAnsweredNothing && !diskIsNarrowed"
                :title="t('monitoring.disk.emptyTitle')"
                :description="t('monitoring.disk.emptyDescription')"
              />
              <UiEmptyState
                v-else-if="diskAnsweredNothing"
                :title="t('monitoring.disk.noResultsTitle')"
                :description="t('monitoring.disk.noResultsDescription')"
              >
                <UiButton variant="secondary" @click="clearDiskSearch">{{ t('monitoring.disk.searchClear') }}</UiButton>
              </UiEmptyState>
              <div v-else class="flex flex-col gap-4">
                <AccountDiskTable
                  :rows="store.accountDiskUsage"
                  :loading="diskTable.state.value.status === 'loading'"
                  :skeleton-rows="diskTable.pageSize.value"
                  :sort="diskTable.sort.value"
                  @sort="diskTable.toggleSort"
                />
                <UiPagination
                  v-if="diskTable.state.value.status === 'loaded'"
                  :page="diskTable.page.value"
                  :page-size="diskTable.pageSize.value"
                  :total="diskTable.total.value"
                  :page-sizes="diskTable.pageSizes"
                  :labels="labels.pagination.value"
                  @update:page="diskTable.setPage"
                  @update:page-size="diskTable.setPageSize"
                />
              </div>
            </UiLoadingRegion>
          </div>
        </UiCard>
      </div>
    </UiLoadingRegion>
  </section>
</template>
