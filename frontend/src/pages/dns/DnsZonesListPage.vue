<script setup lang="ts">
/**
 * DNS zones list screen: one server page of the zones the caller may see at a time, searched,
 * sorted and filtered by the panel. An administrator reaches it as "Zones" and sees every
 * account's zones; a customer reaches it as "My zones" and sees their own. Both routes show this
 * page, the way the tasks list is one page in two menus. Renders a `<section>`, not a `<main>`.
 * Rows come from the zones store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the
 * `account` filter live in the query. The filter is not offered to a customer, whose list is
 * already their own account's.
 *
 * **Two kinds of nothing.** No zones at all is the empty state, with the way to create the first.
 * A search or filter matching none is the no-results state, with the way back to every zone.
 *
 * **Deleting is confirmed** and says what it does: the records go with the zone and the name
 * server stops answering for the name.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import DnsZonesFilterSheet from '../../components/dns/DnsZonesFilterSheet.vue'
import DnsZonesTable from '../../components/dns/DnsZonesTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useDnsZonesStore } from '../../stores/dnsZones'
import { takeFlashNotice } from '../../utils/flashNotice'
import type { DnsZone } from '../../types/dns'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useDnsZonesStore()
const labels = useListLabels()
const { isCustomer } = useCallerRole()
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<DnsZone>({
  filters: { account: accountSpec },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<DnsZone>> => {
    return store.list(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The zone awaiting a delete confirmation, or `null` when none is. */
const pending: Ref<DnsZone | null> = ref(null)

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.account !== '') {
    result.push(labels.chip('account', t('dns.zones.filters.account'), accountFilterName.value))
  }
  return result
})

/**
 * Opens the filter sheet.
 * @returns Nothing.
 */
const openFilters = (): void => {
  filtersOpen.value = true
}

/**
 * Closes the filter sheet.
 * @returns Nothing.
 */
const closeFilters = (): void => {
  filtersOpen.value = false
}

/**
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

/**
 * Starts a delete, which then waits for confirmation.
 * @param zone The zone the person chose to delete.
 * @returns Nothing.
 */
const ask = (zone: DnsZone): void => {
  pending.value = zone
}

/**
 * Abandons a pending delete.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed delete and reads the page again, since the total and the rows that
 * move up from the next page are the server's to say.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const zone = pending.value
  if (zone === null) {
    return
  }
  const deleted = await store.remove(zone.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pending.value = null
  if (deleted) {
    notice.value = t('dns.zones.deleted', { name: zone.name })
    await table.reload()
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(() => {
  store.clearErrors()
  notice.value = takeFlashNotice()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('dns.zones.heading')" :subtitle="t('dns.zones.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'dns-zone-new' }">{{ t('dns.zones.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('dns.zones.emptyTitle')"
      :description="t('dns.zones.emptyDescription')"
    >
      <template #icon><UiIcon name="globe" /></template>
      <UiButton :to="{ name: 'dns-zone-new' }">{{ t('dns.zones.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('dns.zones.searchLabel')"
            :placeholder="t('dns.zones.searchPlaceholder')"
            :clear-label="t('dns.zones.searchClear')"
          />
          <!-- The account is the list's one filter, and a customer's list is already their own
               account's: there is nothing to narrow to, so the trigger is not offered. -->
          <UiFilterTrigger
            v-if="!isCustomer"
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
        </div>

        <UiActiveFilter
          v-if="chips.length > 0"
          :chips="chips"
          :clear-label="t('common.list.clearAll')"
          @remove="table.clearFilter"
          @clear="table.clearFilters"
        />

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing"
            :title="t('dns.zones.noResultsTitle')"
            :description="t('dns.zones.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DnsZonesTable
              :rows="store.zones"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              :show-owner="!isCustomer"
              @sort="table.toggleSort"
              @remove="ask"
            />

            <UiPagination
              v-if="table.state.value.status === 'loaded'"
              :page="table.page.value"
              :page-size="table.pageSize.value"
              :total="table.total.value"
              :page-sizes="table.pageSizes"
              :labels="labels.pagination.value"
              @update:page="table.setPage"
              @update:page-size="table.setPageSize"
            />
          </div>
        </UiLoadingRegion>
      </div>
    </UiCard>

    <DnsZonesFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      @apply="table.setFilters"
      @close="closeFilters"
      @search="searchAccounts"
    />

    <UiConfirm
      :open="pending !== null"
      :title="t('dns.zones.confirmDeleteTitle', { name: pending?.name ?? '' })"
      :question="t('dns.zones.confirmDelete')"
      :confirm-label="t('dns.zones.confirmDeleteAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('common.saving')"
      @close="cancel"
      @confirm="confirm"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
