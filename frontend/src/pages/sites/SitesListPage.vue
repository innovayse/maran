<script setup lang="ts">
/**
 * Sites list screen: one server page of the sites on this server at a time, searched, sorted and
 * filtered by the panel. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives
 * in the layout this page is nested under. Rows come from the sites store; the page never touches
 * the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the
 * `account`, `backend`, `phpVersion` and `status` filters all live in the query. `?account=<id>` is
 * the link the account page opens; it is read through `useAccountFilter().spec`, so a customer's
 * list ignores it exactly as every other account-filtered page does, and the account field is not
 * offered to them: their lists are already their own.
 *
 * **Nothing is filtered here.** The panel searches the domain and the owner's name, sorts, filters
 * and counts; this page shows the page it answers and the total across every page.
 *
 * **Two kinds of nothing.** No sites at all is the empty state, with the way to create the first.
 * A search or filter matching none is the no-results state, with the way back to every site.
 *
 * Lessons this page carries:
 * - The domain is a link, not text. Every action a site has — the PHP version, enable, disable,
 *   delete, its logs, its certificate — lives on the detail page, so a row that cannot be opened
 *   would put all of them out of reach. It is also the ONLY link in a row, and its name is the
 *   bare domain: the end-to-end specs find the way in by that name.
 * - The owner is named by the panel (`ownerName` on each row), never joined from an account list
 *   the SPA happens to hold: a page of sites can belong to any of thousands of accounts. It is
 *   shown to an administrator only; a customer owns every row.
 * - PHP runtimes are a property of this server that only sites consume, so the link to them lives
 *   here, beside the create action.
 * - The backend sends an empty PHP version for a non-PHP site. That is an absence, and it is shown
 *   as one rather than as a blank cell a reader would mistake for missing data.
 * - A server's error is rendered verbatim by the alert, and no table and no empty state is drawn
 *   beside it: "nothing here" and "could not ask" are different statements.
 *
 * The one thing that needs attention goes first: a disabled site is not serving its visitors, so
 * when there is one the list opens with an amber notice saying how many. The count is the panel's,
 * across every page of the account the list is narrowed to, never a count of the rows on screen.
 */
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import SiteStatusBadge from '../../components/sites/SiteStatusBadge.vue'
import { useAccountChoice, type AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useLocaleStore } from '../../stores/locale'
import { usePhpVersionsStore } from '../../stores/phpVersions'
import { useSitesStore } from '../../stores/sites'
import { formatDate } from '../../utils/formatDate'
import { takeFlashNotice } from '../../utils/flashNotice'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { Site, SiteBackendType, SiteStatus } from '../../types/site'

/** What can serve a site, mirroring the `SiteBackendType` union the panel serializes. */
const BACKEND_TYPES: readonly SiteBackendType[] = ['static', 'php', 'reverseProxy']

/** Whether a site serves, mirroring the `SiteStatus` union the panel serializes. */
const SITE_STATUSES: readonly SiteStatus[] = ['enabled', 'disabled']

const { t } = useI18n()
const store = useSitesStore()
const phpVersionsStore = usePhpVersionsStore()
const localeStore = useLocaleStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<Site>({
  filters: {
    account: accountSpec,
    backend: {
      /**
       * Honours only a backend the panel has, so a hand-edited link never reaches the server.
       * @param value The `?backend=` value.
       * @returns True for a known backend.
       */
      accepts: (value: string): boolean => {
        return (BACKEND_TYPES as readonly string[]).includes(value)
      },
    },
    phpVersion: {},
    status: {
      /**
       * Honours only a serving state the panel has.
       * @param value The `?status=` value.
       * @returns True for a known state.
       */
      accepts: (value: string): boolean => {
        return (SITE_STATUSES as readonly string[]).includes(value)
      },
    },
  },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Site>> => {
    return store.list(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

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
 * The account filter's picker: the page of accounts matching its search, after an "any account"
 * choice, which is counted into the total so the "more matches" line stays right. Administrators only.
 */
const accountFilterPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = accountPicker.value
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** The backends the filter offers, after an "any backend" choice. */
const backendOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...BACKEND_TYPES.map((backend) => {
      return { value: backend, label: t(`sites.backendType.${backend}`) }
    }),
  ]
})

/** The PHP versions installed on this server, as the panel reports them, after an "any" choice. */
const phpVersionOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...phpVersionsStore.phpVersions.map((installed) => {
      return { value: installed.version, label: installed.version }
    }),
  ]
})

/** The serving states the filter offers, after an "any state" choice. */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...SITE_STATUSES.map((status) => {
      return { value: status, label: t(`sites.status.${status}`) }
    }),
  ]
})

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The owner column is an administrator's; a customer's sites are all their own.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = isAdmin.value ? [{ key: 'owner', label: t('sites.list.columns.owner'), kind: 'mono' }] : []
  return [
    { key: 'domain', label: t('sites.list.columns.domain'), kind: 'identity', sortField: 'domain', minWidth: '12rem' },
    ...owner,
    { key: 'backendType', label: t('sites.list.columns.backendType'), kind: 'text' },
    { key: 'phpVersion', label: t('sites.list.columns.phpVersion'), kind: 'mono' },
    { key: 'status', label: t('sites.list.columns.status'), kind: 'badge', sortField: 'status' },
    { key: 'createdAt', label: t('sites.list.columns.createdAt'), kind: 'date', align: 'end', sortField: 'createdAt' },
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.account !== '') {
    result.push(
      // Named by the account's own read (useAccountFilter), so any account is named, not only those
      // on some page of choices; the raw id shows only while that read is in flight or if it failed.
      labels.chip('account', t('sites.list.filters.account'), accountFilterName.value),
    )
  }
  if (applied.backend !== '') {
    result.push(labels.chip('backend', t('sites.list.filters.backend'), t(`sites.backendType.${applied.backend}`)))
  }
  if (applied.phpVersion !== '') {
    result.push(labels.chip('phpVersion', t('sites.list.filters.phpVersion'), applied.phpVersion))
  }
  if (applied.status !== '') {
    result.push(labels.chip('status', t('sites.list.filters.status'), t(`sites.status.${applied.status}`)))
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
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(() => {
  notice.value = takeFlashNotice()
  // The PHP versions fill their filter without holding the list back; the account filter searches
  // its own choices when opened.
  void phpVersionsStore.loadPhpVersions()
})

// The disabled count follows the account the list is narrowed to, and only that: it is a notice
// about the account's sites, not about the page the search and other filters happen to show.
watch(
  (): string => {
    return table.filters.value.account
  },
  (accountId: string): void => {
    void store.loadDisabledCount(accountId)
  },
  { immediate: true },
)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('sites.list.heading')"
      :subtitle="t('sites.list.subtitle')"
    >
      <template #actions>
        <!-- PHP versions are an administrator's server setting; hidden from a customer as
             presentation only, the endpoint refuses them on its own (rules/security.md). -->
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'php-versions' }">{{ t('sites.list.phpVersionsLink') }}</UiButton>
        <UiButton :to="{ name: 'sites-new' }">{{ t('sites.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.disabledCount.state === 'counted' && store.disabledCount.count > 0" variant="warning">
      {{ t('sites.list.disabledNotice', { count: store.disabledCount.count }, store.disabledCount.count) }}
    </UiAlert>
    <UiAlert v-else-if="store.disabledCount.state === 'unavailable'" variant="info">
      {{ t('sites.list.disabledUnavailable') }}
    </UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('sites.list.emptyTitle')"
      :description="t('sites.list.emptyDescription')"
    >
      <template #icon><UiIcon name="earth" /></template>
      <UiButton :to="{ name: 'sites-new' }">{{ t('sites.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('sites.list.searchLabel')"
            :placeholder="t('sites.list.searchPlaceholder')"
            :clear-label="t('sites.list.searchClear')"
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
            :title="t('sites.list.noResultsTitle')"
            :description="t('sites.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <UiTable
              :caption="t('sites.list.tableCaption')"
              :columns="columns"
              :rows="store.sites"
              row-key="id"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
            >
              <template #cell-domain="{ row }">
                <div class="flex items-center gap-3">
                  <UiIconTile name="earth" />
                  <!-- The way in: everything that can be done to a site lives on its detail page. -->
                  <RouterLink
                    :to="{ name: 'site-detail', params: { id: row.id } }"
                    class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
                    >{{ row.domain }}</RouterLink
                  >
                </div>
              </template>
              <template #cell-owner="{ row }">{{ row.ownerName }}</template>
              <template #cell-backendType="{ row }">{{ t(`sites.backendType.${row.backendType}`) }}</template>
              <template #cell-phpVersion="{ row }">
                {{ row.phpVersion.length > 0 ? row.phpVersion : t('common.emptyValue') }}
              </template>
              <template #cell-status="{ row }"><SiteStatusBadge :status="row.status" /></template>
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
        <!-- A customer's lists are already their own account's; there is nothing to narrow to. -->
        <UiCombobox
          v-if="!isCustomer"
          v-model="draft.account"
          v-bind="accountFilterPicker"
          :label="t('sites.list.filters.account')"
          :placeholder="t('common.list.any')"
          :selected-label="accountFilterName"
          @search="searchAccounts"
        />
        <UiSelect v-model="draft.backend" :label="t('sites.list.filters.backend')" :options="backendOptions" />
        <UiSelect v-model="draft.phpVersion" :label="t('sites.list.filters.phpVersion')" :options="phpVersionOptions" />
        <UiSelect v-model="draft.status" :label="t('sites.list.filters.status')" :options="statusOptions" />
      </template>
    </UiFilterSheet>

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
