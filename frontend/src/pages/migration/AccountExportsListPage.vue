<script setup lang="ts">
/**
 * The account exports list: one server page of the exports an administrator made, searched by the
 * exported account's name and sorted by the panel. Administrators only; the route and the endpoint
 * refuse everyone else. Renders a `<section>`, not a `<main>`. Rows come from the account exports
 * store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search and sort live in the
 * query. The list has no filter, so it has no filter trigger.
 *
 * **Two kinds of nothing.** No exports at all is the empty state, with the way to make the first. A
 * search matching none is the no-results state, with the way back to every export.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import AccountExportsTable from '../../components/migration/AccountExportsTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAccountExportsStore } from '../../stores/accountExports'
import type { AccountExport } from '../../types/accountMove'
import type { ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useAccountExportsStore()
const labels = useListLabels()

const table = useServerTable<AccountExport>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<AccountExport>> => {
    return store.listExports(request)
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
 * @returns Nothing; the page follows after the box's pause.
 */
const clearAll = (): void => {
  table.search.value = ''
}

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('accountMove.export.list.heading')" :subtitle="t('accountMove.export.list.subtitle')">
      <template #actions>
        <UiLink :to="{ name: 'export-grants' }">{{ t('accountMove.export.list.grantsLink') }}</UiLink>
        <UiButton :to="{ name: 'account-export' }">{{ t('accountMove.export.list.addAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('accountMove.export.list.emptyTitle')"
      :description="t('accountMove.export.list.emptyDescription')"
    >
      <template #icon><UiIcon name="package" /></template>
      <UiButton :to="{ name: 'account-export' }">{{ t('accountMove.export.list.addAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('accountMove.export.list.searchLabel')"
            :placeholder="t('accountMove.export.list.searchPlaceholder')"
            :clear-label="t('accountMove.export.list.searchClear')"
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
            :title="t('accountMove.export.list.noResultsTitle')"
            :description="t('accountMove.export.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <AccountExportsTable
              :rows="store.exports"
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
