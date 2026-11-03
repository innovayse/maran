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
 * The refresh button reads the same page again; it asks the panel, which still never refreshes the
 * package lists.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import PendingUpdatesTable from '../../components/monitoring/PendingUpdatesTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { usePendingUpdatesStore } from '../../stores/pendingUpdates'
import type { ListLoad, ListRequest } from '../../types/list'
import type { PendingUpdate } from '../../types/monitoring'

const { t } = useI18n()
const store = usePendingUpdatesStore()
const labels = useListLabels()

const table = useServerTable<PendingUpdate>({
  /**
   * Reads one page of pending updates through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<PendingUpdate>> => {
    return store.listPendingUpdates(request)
  },
})

/** Whether the latest page arrived and holds no update at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether a search narrows the list, so an empty answer means "no match", not "up to date". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/**
 * Clears the search, the way back from a search that matched no package.
 * @returns Nothing; the search reaches the URL after the box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

/**
 * Reads the current page again.
 * @returns Resolves once the page has settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  await table.reload()
}
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

    <UiCard class="min-w-0">
      <div class="flex flex-col gap-4">
        <UiSearchInput
          v-model="table.search.value"
          class="min-w-55 max-w-85"
          :label="t('monitoring.updates.searchLabel')"
          :placeholder="t('monitoring.updates.searchPlaceholder')"
          :clear-label="t('monitoring.updates.searchClear')"
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
            <UiButton variant="secondary" @click="clearSearch">{{ t('monitoring.updates.searchClear') }}</UiButton>
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
  </section>
</template>
