<script setup lang="ts">
/**
 * Servers > Updates: the operating-system packages that have an update available, read from the
 * server's package cache.
 *
 * Renders a `<section>`, not a `<main>` (the layout owns that landmark). All state comes from the
 * pending-updates store; the page never touches the API layer (rules/vue.md).
 *
 * **"Nothing to update" is said only when the read succeeded and the total is zero.** A failed read
 * (the agent could not read the package cache) shows the backend's own message and no empty state:
 * an unreadable cache is not a server that is up to date. The list is a server page
 * (`useServerTable`): page, page size, the search on the package name and the sort live in the URL.
 * The classification filter lives in the URL beside them, behind the filter trigger. The refresh
 * button reads the same page again; it asks the panel, which still never refreshes the
 * package lists.
 */
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import PendingUpdatesFilterSheet from '../../components/monitoring/PendingUpdatesFilterSheet.vue'
import PendingUpdatesSummary from '../../components/monitoring/PendingUpdatesSummary.vue'
import PendingUpdatesTable from '../../components/monitoring/PendingUpdatesTable.vue'
import { useReloadOnLocale } from '../../composables/useReloadOnLocale'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useLocaleStore } from '../../stores/locale'
import { usePendingUpdatesStore } from '../../stores/pendingUpdates'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { PendingUpdate } from '../../types/monitoring'

const { t } = useI18n()
const store = usePendingUpdatesStore()
const labels = useListLabels()
const localeStore = useLocaleStore()

const table = useServerTable<PendingUpdate>({
  filters: {
    classification: {
      /**
       * Honours only a kind the panel listed, so a hand-edited link never reaches the server as a
       * filter the page cannot name. Permissive until the list has arrived, and the endpoint
       * refuses an unknown kind regardless.
       * @param value The `?classification=` value.
       * @returns True for a kind the panel has, or while the panel's list is not read yet.
       */
      accepts: store.acceptsClassification,
    },
  },
  /**
   * Reads one page of pending updates through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<PendingUpdate>> => {
    return store.listPendingUpdates(request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** Whether the latest page arrived and holds no update at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether a search or a filter narrows the list, so an empty answer means "no match", not "up to date". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value.classification
  if (applied === '') {
    return []
  }
  const name = store.classificationName(applied)
  if (name === null) {
    // A kind the panel has not named (the list is unread, or it never listed it) gets no chip: the
    // SPA holds no name for it and a raw value is not one.
    return []
  }
  return [labels.chip('classification', t('monitoring.updates.columnClassification'), name)]
})

/** Whether the list holds an update, or a search or filter narrows it; false means the empty state is the page. */
const showsUpdates: ComputedRef<boolean> = computed(() => {
  return !answeredNothing.value || isNarrowed.value
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
 * Clears the search and every filter, the way back from a question that matched no package.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

/**
 * Reads the current page again.
 * @returns Resolves once the page has settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([table.reload(), store.loadSummary()])
}

onMounted(() => {
  void store.loadSummary()
})

// The kinds and the rows' kind names come in the interface language, so a language switch asks again.
useReloadOnLocale(store.loadClassifications)
watch(
  () => {
    return localeStore.current
  },
  () => {
    void table.reload()
  },
)
</script>

<template>
  <section class="flex flex-col gap-4">
    <UiPageHeading :title="t('monitoring.updates.heading')" :subtitle="t('monitoring.updates.subtitle')">
      <template #actions>
        <UiButton variant="secondary" :disabled="table.state.value.status === 'loading'" @click="refresh">
          {{ t('monitoring.updates.refresh') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <PendingUpdatesSummary
      :security-count="store.securityCount"
      :reboot-required="store.rebootRequired"
      :has-updates="showsUpdates"
    />

    <UiCard class="min-w-0">
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('monitoring.updates.searchLabel')"
            :placeholder="t('monitoring.updates.searchPlaceholder')"
            :clear-label="t('monitoring.updates.searchClear')"
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
          <!-- A refusal is the backend's own localized sentence, so the SPA holds no copy of it. -->
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('monitoring.updates.emptyTitle')"
            :description="t('monitoring.updates.emptyDescription')"
          />
          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('monitoring.updates.noResultsTitle')"
            :description="t('monitoring.updates.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>
          <div v-else class="flex flex-col gap-4">
            <PendingUpdatesTable
              :rows="store.items"
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

    <PendingUpdatesFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      @update:model-value="table.setFilters"
      @close="closeFilters"
    />
  </section>
</template>
