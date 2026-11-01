<script setup lang="ts">
/**
 * The history of one cron entry's runs started by hand (issue #158): one server page at a time,
 * searched in what the runs printed, narrowed by how they ended through the filter sheet (a modal on
 * a desktop, a bottom sheet on a phone — `UiFilterSheet` decides), and sorted by the panel. Renders a
 * `<section>`, not a `<main>`. State comes from the stores; the page never touches the API layer
 * (rules/vue.md).
 *
 * **The account rides in the URL** (`?account=<id>`), as on the cron screen's own deep link, because
 * an entry id means nothing until it is asked of one account's crontab. A customer owns exactly one
 * account, so for them it is their own, whatever the URL says.
 *
 * The history holds the runs the panel started — the newest twenty per entry. What cron itself ran on
 * the schedule is still the cron screen's "Last run output".
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import CronRunResultDialog from '../../components/cron/CronRunResultDialog.vue'
import CronRunsTable from '../../components/cron/CronRunsTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useCronStore } from '../../stores/cron'
import { useCronRunsStore } from '../../stores/cronRuns'
import { CRON_RUN_OUTCOMES, type CronRunOutcome, type CronRunSummary } from '../../types/cronRun'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const route = useRoute()
const store = useCronRunsStore()
const cronStore = useCronStore()
const labels = useListLabels()
const { fixedAccountId } = useAccountChoice()

/** The entry whose runs are shown, from the route. */
const entryId: ComputedRef<string> = computed(() => {
  return String(route.params.entryId)
})

/** The account the entry belongs to: the customer's own, or the one the URL names. */
const accountId: ComputedRef<string> = computed(() => {
  const account = route.query.account
  return fixedAccountId.value ?? (typeof account === 'string' ? account : '')
})

const table = useServerTable<CronRunSummary>({
  filters: {
    outcome: {
      /**
       * Honours only an outcome the panel knows, so a hand-edited link never reaches the server.
       * @param value The value found in the URL.
       * @returns Whether it names an outcome.
       */
      accepts: (value: string): boolean => {
        return (CRON_RUN_OUTCOMES as readonly string[]).includes(value)
      },
    },
  },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<CronRunSummary>> => {
    return store.list(entryId.value, accountId.value, request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The entry's command, once the account's crontab has been read; its id until then. */
const subtitle: ComputedRef<string> = computed(() => {
  const entry = cronStore.entries.find((candidate) => {
    return candidate.entryId === entryId.value
  })
  return entry?.command ?? entryId.value
})

/** The outcomes the filter offers, after an "any outcome" choice. */
const outcomeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...CRON_RUN_OUTCOMES.map((outcome) => {
      return { value: outcome, label: t(`cron.runs.outcome.${outcome}`) }
    }),
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const outcome = table.filters.value.outcome as CronRunOutcome | ''
  return outcome !== ''
    ? [labels.chip('outcome', t('cron.runs.filters.outcome'), t(`cron.runs.outcome.${outcome}`))]
    : []
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether the history is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Where "Back" leads: the cron screen, on the same account. */
const backLink: ComputedRef<{ name: string; query: Record<string, string> }> = computed(() => {
  return { name: 'cron', query: fixedAccountId.value === null ? { account: accountId.value } : {} }
})

/**
 * Opens the dialog on one run and reads what it printed.
 * @param run The row asked about.
 * @returns Resolves once the read has settled.
 */
const view = async (run: CronRunSummary): Promise<void> => {
  await store.open(entryId.value, accountId.value, run.id)
}

/**
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
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

// The command in the subtitle comes from the account's crontab; read it when the cron screen has not.
onMounted(async () => {
  if (accountId.value !== '' && cronStore.accountId !== accountId.value) {
    await cronStore.selectAccount(accountId.value)
  }
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('cron.runs.heading')" :subtitle="subtitle" :note="t('cron.runs.note')">
      <template #actions>
        <UiLink :to="backLink" icon="chevronLeft">{{ t('cron.runs.back') }}</UiLink>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('cron.runs.emptyTitle')"
      :description="t('cron.runs.emptyDescription')"
    >
      <template #icon><UiIcon name="clock" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('cron.runs.searchLabel')"
            :placeholder="t('cron.runs.searchPlaceholder')"
            :clear-label="t('cron.runs.searchClear')"
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
            :title="t('cron.runs.noResultsTitle')"
            :description="t('cron.runs.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <CronRunsTable
              :rows="store.runs"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @view="view"
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
        <UiSelect v-model="draft.outcome" :label="t('cron.runs.filters.outcome')" :options="outcomeOptions" />
      </template>
    </UiFilterSheet>

    <CronRunResultDialog
      :open="store.isShownOpen"
      :run="store.shown"
      :error-message="store.shownErrorMessage"
      @close="store.close"
    />
  </section>
</template>
