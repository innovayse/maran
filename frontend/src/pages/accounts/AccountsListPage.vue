<script setup lang="ts">
/**
 * Accounts list screen: one server page of the hosting accounts at a time, searched, sorted and
 * filtered by the panel. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives
 * in the layout this page is nested under. Rows come from the accounts store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the `plan`
 * and `status` filters all live in the query, so a list can be shared, reloaded and walked back
 * through. `?plan=<id>` is the link the plans screen opens when a plan cannot be deleted; it is one
 * filter of the schema like the other, shown as a removable chip.
 *
 * **Nothing is filtered here.** The search box, the filter sheet and the sort headers only change
 * the URL; the panel answers with the matching page and the total across every page.
 *
 * **Two kinds of nothing.** The panel reporting no accounts at all is the empty state, with the way
 * to create the first one. A search or filter matching none is the no-results state, with the way
 * back to every account. The first claims the server is empty; the second only that the question
 * found nothing, and confusing them would tell an operator their accounts were gone.
 *
 * Lessons kept from the earlier page:
 * - The account's name is a link to its detail page. A row that shows an account but cannot open
 *   it leaves suspend, reactivate and delete unreachable from the panel entirely.
 * - The name cell holds ONLY the link as its accessible name (the avatar is `aria-hidden`), and the
 *   domain has its own cell: a cell named "alpha alpha.example.com" is not the cell named "alpha"
 *   the specs and a screen-reader user look for.
 * - The backend's own error text is rendered verbatim, never replaced by SPA copy.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
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
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import AccountStatusBadge from '../../components/accounts/AccountStatusBadge.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { Account, AccountStatus } from '../../types/account'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

/**
 * The states an account can be in, mirroring the `AccountStatus` union the panel serializes. The
 * filter offers them by the same translated names the status badge shows.
 */
const ACCOUNT_STATUSES: readonly AccountStatus[] = ['active', 'suspended']

const { t } = useI18n()
const store = useAccountsStore()
const localeStore = useLocaleStore()
const labels = useListLabels()

const table = useServerTable<Account>({
  filters: {
    plan: {},
    status: {
      /**
       * Honours only a state the panel has, so a hand-edited link never reaches the server.
       * @param value The `?status=` value.
       * @returns True for a known account state.
       */
      accepts: (value: string): boolean => {
        return (ACCOUNT_STATUSES as readonly string[]).includes(value)
      },
    },
  },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Account>> => {
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

/** The plans the filter offers, by the names the panel reports, after an "any plan" choice. */
const planOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...store.plans.map((plan) => {
      return { value: plan.id, label: plan.displayName }
    }),
  ]
})

/** The account states the filter offers, after an "any state" choice. */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...ACCOUNT_STATUSES.map((status) => {
      return { value: status, label: t(`accounts.status.${status}`) }
    }),
  ]
})

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('common.name'), kind: 'identity', sortField: 'name' },
    { key: 'primaryDomain', label: t('accounts.list.columns.primaryDomain'), kind: 'mono' },
    { key: 'status', label: t('accounts.list.columns.status'), kind: 'badge', sortField: 'status' },
    { key: 'createdAt', label: t('accounts.list.columns.createdAt'), kind: 'date', align: 'end', sortField: 'createdAt' },
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.plan !== '') {
    result.push(labels.chip('plan', t('accounts.list.filters.plan'), planName(applied.plan)))
  }
  if (applied.status !== '') {
    result.push(labels.chip('status', t('accounts.list.filters.status'), t(`accounts.status.${applied.status}`)))
  }
  return result
})

/**
 * Names a plan by the name the panel reports; the id while the plans have not arrived.
 * @param id The plan's identity.
 * @returns The plan's display name, or the id.
 */
const planName = (id: string): string => {
  return (
    store.plans.find((plan) => {
      return plan.id === id
    })?.displayName ?? id
  )
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

// The plans name the plan filter's chip and fill its choices; the list does not wait for them.
onMounted(() => {
  void store.loadPlans()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('accounts.list.eyebrow')"
      :title="t('accounts.list.heading')"
      :subtitle="t('accounts.list.subtitle')"
    >
      <template #actions>
        <UiButton :to="{ name: 'accounts-new' }">{{ t('accounts.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('accounts.list.emptyTitle')"
      :description="t('accounts.list.emptyDescription')"
    >
      <UiButton :to="{ name: 'accounts-new' }">{{ t('accounts.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('accounts.list.searchLabel')"
            :placeholder="t('accounts.list.searchPlaceholder')"
            :clear-label="t('accounts.list.searchClear')"
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
            :title="t('accounts.list.noResultsTitle')"
            :description="t('accounts.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <UiTable
              :caption="t('accounts.list.tableCaption')"
              :columns="columns"
              :rows="store.accounts"
              row-key="id"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
            >
              <template #cell-name="{ row }">
                <div class="flex items-center gap-3">
                  <UiAvatar :name="row.name" size="sm" />
                  <!-- The name is the way in to the account's detail page. -->
                  <RouterLink
                    :to="{ name: 'account-detail', params: { id: row.id } }"
                    class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:underline"
                    >{{ row.name }}</RouterLink
                  >
                </div>
              </template>
              <template #cell-primaryDomain="{ row }">{{ row.primaryDomain }}</template>
              <template #cell-status="{ row }"><AccountStatusBadge :status="row.status" /></template>
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
        <UiSelect v-model="draft.plan" :label="t('accounts.list.filters.plan')" :options="planOptions" />
        <UiSelect v-model="draft.status" :label="t('accounts.list.filters.status')" :options="statusOptions" />
      </template>
    </UiFilterSheet>
  </section>
</template>
