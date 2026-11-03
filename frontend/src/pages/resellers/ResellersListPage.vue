<script setup lang="ts">
/**
 * Resellers list screen: one server page of the resellers at a time, searched, sorted and filtered
 * by the panel. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. Rows come from the resellers store; the page never touches the
 * API layer (rules/vue.md: API composables are called from stores only).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the
 * `status` filter all live in the query, so a list can be shared, reloaded and walked back through.
 * Nothing is filtered here: the search box, the filter sheet and the sort headers only change the
 * URL, and the panel answers with the matching page and the total across every page.
 *
 * **Two kinds of nothing.** The panel having no resellers at all is the empty state, with the way
 * to create the first one. A search or filter matching none is the no-results state, with the way
 * back to every reseller.
 *
 * The reseller's name is a link to its detail page, and the backend's own error text is rendered
 * verbatim, never replaced by SPA copy.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAvatar from '../../components/ui/UiAvatar.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiTable from '../../components/ui/UiTable.vue'
import ResellerStatusBadge from '../../components/resellers/ResellerStatusBadge.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useLocaleStore } from '../../stores/locale'
import { useResellersStore } from '../../stores/resellers'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { Reseller } from '../../types/reseller'
import type { UiTableColumn } from '../../types/tableColumn'
import { formatDate } from '../../utils/formatDate'
import { formatMegabytes } from '../../utils/formatBytes'

const { t } = useI18n()
const store = useResellersStore()
const localeStore = useLocaleStore()
const labels = useListLabels()

const table = useServerTable<Reseller>({
  filters: { status: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Reseller>> => {
    return store.list(request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * The states the filter offers, after an "any state" choice. The panel has no endpoint listing the
 * two states of a reseller, so the filter's own option names are chrome; every status a row or a
 * detail page prints is the panel's `statusName`.
 */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    { value: 'active', label: t('resellers.status.active') },
    { value: 'suspended', label: t('resellers.status.suspended') },
  ]
})

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('common.name'), kind: 'identity', sortField: 'name' },
    { key: 'contactEmail', label: t('resellers.list.columns.contactEmail'), kind: 'mono' },
    { key: 'status', label: t('resellers.list.columns.status'), kind: 'badge', sortField: 'status' },
    { key: 'accounts', label: t('resellers.list.columns.accounts'), kind: 'number' },
    { key: 'plans', label: t('resellers.list.columns.plans'), kind: 'number' },
    { key: 'disk', label: t('resellers.list.columns.disk'), kind: 'number' },
    { key: 'createdAt', label: t('resellers.list.columns.createdAt'), kind: 'date', align: 'end', sortField: 'createdAt' },
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.status !== '') {
    result.push(labels.chip('status', t('resellers.list.filters.status'), t(`resellers.status.${applied.status}`)))
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
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading :title="t('resellers.list.heading')" :subtitle="t('resellers.list.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'resellers-new' }">{{ t('resellers.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('resellers.list.emptyTitle')"
      :description="t('resellers.list.emptyDescription')"
    >
      <UiButton :to="{ name: 'resellers-new' }">{{ t('resellers.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('resellers.list.searchLabel')"
            :placeholder="t('resellers.list.searchPlaceholder')"
            :clear-label="t('resellers.list.searchClear')"
          />
          <UiFilterTrigger
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
            :title="t('resellers.list.noResultsTitle')"
            :description="t('resellers.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <UiTable
              :caption="t('resellers.list.tableCaption')"
              :columns="columns"
              :rows="store.resellers"
              row-key="id"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
            >
              <template #cell-name="{ row }">
                <div class="flex items-center gap-3">
                  <UiAvatar :name="row.name" size="sm" />
                  <!-- The name is the way in to the reseller's detail page. -->
                  <RouterLink
                    :to="{ name: 'reseller-detail', params: { id: row.id } }"
                    class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:underline"
                    >{{ row.name }}</RouterLink
                  >
                </div>
              </template>
              <template #cell-contactEmail="{ row }">{{ row.contactEmail }}</template>
              <template #cell-status="{ row }"><ResellerStatusBadge :status="row.status" :label="row.statusName" /></template>
              <template #cell-accounts="{ row }">{{ row.accountCount }}</template>
              <template #cell-plans="{ row }">{{ row.planCount }}</template>
              <template #cell-disk="{ row }">{{ formatMegabytes(row.diskAllocatedMb) }}</template>
              <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
            </UiTable>

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

    <UiFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :title="t('common.list.filters')"
      :close-label="t('common.close')"
      :apply-label="t('common.list.apply')"
      :reset-label="t('common.list.reset')"
      @update:model-value="table.setFilters"
      @close="closeFilters"
    >
      <template #default="{ draft }">
        <UiSelect v-model="draft.status" :label="t('resellers.list.filters.status')" :options="statusOptions" />
      </template>
    </UiFilterSheet>
  </section>
</template>
