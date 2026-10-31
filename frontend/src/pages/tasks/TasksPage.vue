<script setup lang="ts">
/**
 * Background tasks screen: what the panel has been doing, and one task watched live.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. State comes exclusively from the tasks store, the same store the shell
 * header's badge reads; the page never touches the API layer (rules/vue.md).
 *
 * **The list is a server page** (`useServerTable`): page, page size, the search on the subject, the
 * sort (`startedAt`, `subject`) and the `account`, `status`, `kind`, `from` and `to` filters live in
 * the URL, and the panel searches, filters, sorts and counts. What needs attention — how many tasks
 * failed, across every page — is asked of the panel too, and its one action narrows the list to the
 * failed ones.
 *
 * **Customers see their own tasks (issue #201).** The panel scopes the list: a customer gets the
 * tasks started for their own account, with an empty log the panel marks `logIncluded: false`, and
 * no account field in the filters.
 *
 * Opening a task starts its stream and leaves it running when the pane is closed, because the badge
 * is still counting. The shell releases every stream when it tears down.
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
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import TaskFilterSheet from '../../components/tasks/TaskFilterSheet.vue'
import TaskLivePane from '../../components/tasks/TaskLivePane.vue'
import TaskTable from '../../components/tasks/TaskTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useTaskFilters } from '../../composables/useTaskFilters'
import { useTaskKindLabel } from '../../composables/useTaskKindLabel'
import { useTasksStore } from '../../stores/tasks'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { PanelTask } from '../../types/panelTask'

const { t } = useI18n()
const store = useTasksStore()
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()
const { isCustomer } = useCallerRole()
const labels = useListLabels()
const kindLabel = useTaskKindLabel()
const taskFilters = useTaskFilters(accountSpec)

const table = useServerTable<PanelTask>({
  filters: taskFilters.schema,
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<PanelTask>> => {
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

/** How many tasks failed, across every page, when the panel answered and some did; otherwise 0. */
const failedCount: ComputedRef<number> = computed(() => {
  const failures = store.failures
  return failures.state === 'answered' ? failures.count : 0
})

/** The applied operation as an operator reads it: the panel's name, or this bundle's word for it. */
const kindName: ComputedRef<string> = computed(() => {
  const applied = table.filters.value.kind ?? ''
  return store.kinds.find((choice) => {
    return choice.kind === applied
  })?.kindName ?? kindLabel(applied)
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return taskFilters.chips(table.filters.value, accountFilterName.value, kindName.value)
})

/**
 * Opens one task's live pane and starts watching it.
 * @param task The task to open.
 * @returns Nothing.
 */
const open = (task: PanelTask): void => {
  store.select(task)
}

/**
 * Closes the live pane, leaving the stream open.
 * @returns Nothing.
 */
const close = (): void => {
  store.deselect()
}

/**
 * Narrows the list to the failed tasks, the way in from the attention notice.
 * @returns Resolves once the filter reached the URL.
 */
const showFailed = async (): Promise<void> => {
  await table.setFilters({ ...table.filters.value, status: 'failed' })
}

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

onMounted(async () => {
  await Promise.all([store.loadFailures(), store.loadKinds()])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('tasks.list.eyebrow')"
      :title="t('tasks.list.heading')"
      :subtitle="t('tasks.list.subtitle')"
    />

    <!-- What needs attention goes first: tasks that failed, counted by the panel across every page. -->
    <UiAlert v-if="failedCount > 0" variant="warning">
      {{ t('tasks.list.failedNotice', { count: failedCount }, failedCount) }}
      <template #actions>
        <UiButton variant="secondary" @click="showFailed">{{ t('tasks.list.showFailed') }}</UiButton>
      </template>
    </UiAlert>

    <!-- Keyed by task, so opening another one starts a fresh pane — a fresh scroll position
         included — instead of reusing the previous task's. -->
    <TaskLivePane v-if="store.openTask !== null" :key="store.openTask.id" :task="store.openTask" @close="close" />

    <UiCard class="min-w-0">
      <UiSectionHeading class="mb-4" :title="t('tasks.list.recentHeading')" />

      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('tasks.list.searchLabel')"
            :placeholder="t('tasks.list.searchPlaceholder')"
            :clear-label="t('tasks.list.searchClear')"
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
            v-if="answeredNothing && !isNarrowed"
            :title="t('tasks.list.emptyTitle')"
            :description="t('tasks.list.emptyDescription')"
          >
            <template #icon><UiIcon name="listChecks" size="lg" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('tasks.list.noResultsTitle')"
            :description="t('tasks.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <TaskTable
              :rows="store.rows"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @open="open"
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

    <!-- A customer's tasks are already their own account's; there is no account to narrow to. -->
    <TaskFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :show-account="!isCustomer"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      :kinds="store.kinds"
      @update:model-value="table.setFilters"
      @close="closeFilters"
      @search-accounts="searchAccounts"
    />
  </section>
</template>
