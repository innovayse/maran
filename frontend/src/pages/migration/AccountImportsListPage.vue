<script setup lang="ts">
/**
 * The account imports list: one server page of the imports, searched by the account's or the sending
 * server's name, narrowed to a state and sorted by the panel. Administrators only; the route and the
 * endpoint refuse everyone else. Renders a `<section>`, not a `<main>`. Rows come from the account
 * imports store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the `state`
 * filter live in the query. The filter is edited in a sheet opened from its trigger.
 *
 * **Two kinds of nothing.** No imports at all is the empty state, with the way to make the first. A
 * search or filter matching none is the no-results state, with the way back to every import.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import type { SelectOption } from '../../components/ui/UiSelect.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import AccountImportsTable from '../../components/migration/AccountImportsTable.vue'
import MigrationStateFilterSheet from '../../components/migration/MigrationStateFilterSheet.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAccountImportsStore } from '../../stores/accountImports'
import type { AccountImport, AccountImportState } from '../../types/accountMove'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

/** The states an import can be in, in the order an import moves through them. */
const STATES: readonly AccountImportState[] = ['Staged', 'Running', 'Completed', 'RolledBack', 'RolledBackIncomplete', 'Discarded']

const { t } = useI18n()
const store = useAccountImportsStore()
const labels = useListLabels()

const table = useServerTable<AccountImport>({
  filters: { state: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<AccountImport>> => {
    return store.list(request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The state choices, each named in the administrator's language. */
const stateChoices: ComputedRef<SelectOption[]> = computed(() => {
  return STATES.map((state) => {
    return { value: state, label: t(`accountMove.import.states.${state}`) }
  })
})

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
  const applied = table.filters.value.state ?? ''
  if (applied === '') {
    return []
  }
  return [labels.chip('state', t('accountMove.import.list.filters.state'), t(`accountMove.import.states.${applied}`))]
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

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('accountMove.import.list.heading')" :subtitle="t('accountMove.import.list.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'account-import' }">{{ t('accountMove.import.list.addAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('accountMove.import.list.emptyTitle')"
      :description="t('accountMove.import.list.emptyDescription')"
    >
      <template #icon><UiIcon name="package" /></template>
      <UiButton :to="{ name: 'account-import' }">{{ t('accountMove.import.list.addAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('accountMove.import.list.searchLabel')"
            :placeholder="t('accountMove.import.list.searchPlaceholder')"
            :clear-label="t('accountMove.import.list.searchClear')"
          />
          <UiFilterTrigger
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
        </UiToolbar>

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
            :title="t('accountMove.import.list.noResultsTitle')"
            :description="t('accountMove.import.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <AccountImportsTable
              :rows="store.imports"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
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

    <MigrationStateFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :label="t('accountMove.import.list.filters.state')"
      :states="stateChoices"
      @apply="table.setFilters"
      @close="closeFilters"
    />
  </section>
</template>
