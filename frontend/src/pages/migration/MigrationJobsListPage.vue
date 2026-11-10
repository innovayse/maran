<script setup lang="ts">
/**
 * Migration jobs list screen: one server page of the whole-server migrations at a time, searched and sorted by
 * the panel (issue #197, phase 2). Administrators only; the route and the endpoint refuse everyone else. Renders
 * a `<section>`, not a `<main>`. Rows come from the migration-jobs store; the page never touches the API layer
 * (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search and sort live in the query.
 *
 * **Two kinds of nothing.** No jobs at all is the empty state with the way to start one; a search matching none is
 * the no-results state with the way back.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MigrationJobsTable from '../../components/migration/MigrationJobsTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useMigrationJobsStore } from '../../stores/migrationJobs'
import type { ListLoad, ListRequest } from '../../types/list'
import type { MigrationJob } from '../../types/migrationJob'

const { t } = useI18n()
const store = useMigrationJobsStore()
const labels = useListLabels()

const table = useServerTable<MigrationJob>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<MigrationJob>> => {
    return store.list(request)
  },
})

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * Clears the search, the way back from a question that matched nothing.
 * @returns Nothing; the list reads again after the box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('migrationJobs.list.heading')" :subtitle="t('migrationJobs.list.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'migration-jobs-new' }">{{ t('migrationJobs.list.addAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('migrationJobs.list.emptyTitle')"
      :description="t('migrationJobs.list.emptyDescription')"
    >
      <template #icon><UiIcon name="server" /></template>
      <UiButton :to="{ name: 'migration-jobs-new' }">{{ t('migrationJobs.list.addAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('migrationJobs.list.searchLabel')"
            :placeholder="t('migrationJobs.list.searchPlaceholder')"
            :clear-label="t('migrationJobs.list.searchClear')"
          />
        </UiToolbar>

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing"
            :title="t('migrationJobs.list.noResultsTitle')"
            :description="t('migrationJobs.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearSearch">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <MigrationJobsTable
              :rows="store.jobs"
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
