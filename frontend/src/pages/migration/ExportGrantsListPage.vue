<script setup lang="ts">
/**
 * The pull grants list: one server page of the grants issued for exports, searched by the exported
 * account's name, narrowed to a state and sorted by the panel. Administrators only; the route and the
 * endpoint refuse everyone else. Renders a `<section>`, not a `<main>`. Rows come from the account
 * exports store; the page never touches the API layer (rules/vue.md). No row carries a token: it was
 * shown once, when the grant was issued.
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the `state`
 * filter live in the query, so the export's screen can link here already searched by its account.
 * The filter is edited in a sheet opened from its trigger.
 *
 * **Revoking is confirmed**, then the page reads its page again: the panel says what state the grant
 * is in now, and which rows move up from the next page.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import type { SelectOption } from '../../components/ui/UiSelect.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import ExportGrantsTable from '../../components/migration/ExportGrantsTable.vue'
import MigrationStateFilterSheet from '../../components/migration/MigrationStateFilterSheet.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAccountExportsStore } from '../../stores/accountExports'
import type { ExportGrantState, ExportGrantSummary } from '../../types/accountMove'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

/** The states a grant can be in, in the order a grant moves through them. */
const STATES: readonly ExportGrantState[] = ['Live', 'Used', 'Revoked', 'Expired']

const { t } = useI18n()
const store = useAccountExportsStore()
const labels = useListLabels()

const table = useServerTable<ExportGrantSummary>({
  filters: { state: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<ExportGrantSummary>> => {
    return store.listGrants(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The grant awaiting a revoke confirmation, or `null` when none is. */
const pending: Ref<ExportGrantSummary | null> = ref(null)

/** The state choices, each named in the administrator's language. */
const stateChoices: ComputedRef<SelectOption[]> = computed(() => {
  return STATES.map((state) => {
    return { value: state, label: t(`accountMove.export.grants.states.${state}`) }
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
  return [labels.chip('state', t('accountMove.export.grants.filters.state'), t(`accountMove.export.grants.states.${applied}`))]
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
 * Starts a revoke, which then waits for confirmation.
 * @param grant The grant the person chose to revoke.
 * @returns Nothing.
 */
const ask = (grant: ExportGrantSummary): void => {
  pending.value = grant
}

/**
 * Abandons a pending revoke.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed revoke and reads the page again; a refusal stays above the list in the
 * panel's own words.
 * @returns Resolves once the request and the page read have settled.
 */
const confirm = async (): Promise<void> => {
  const grant = pending.value
  if (grant === null) {
    return
  }
  const revoked = await store.revokeGrant(grant.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pending.value = null
  if (revoked) {
    notice.value = t('accountMove.export.grants.revokedNotice')
  }
  await table.reload()
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('accountMove.export.grants.heading')" :subtitle="t('accountMove.export.grants.subtitle')" />

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('accountMove.export.grants.emptyTitle')"
      :description="t('accountMove.export.grants.emptyDescription')"
    >
      <template #icon><UiIcon name="package" /></template>
      <UiButton :to="{ name: 'account-exports' }">{{ t('accountMove.export.grants.toExports') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('accountMove.export.grants.searchLabel')"
            :placeholder="t('accountMove.export.grants.searchPlaceholder')"
            :clear-label="t('accountMove.export.grants.searchClear')"
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
            :title="t('accountMove.export.grants.noResultsTitle')"
            :description="t('accountMove.export.grants.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <ExportGrantsTable
              :rows="store.grantRows"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @revoke="ask"
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
      :label="t('accountMove.export.grants.filters.state')"
      :states="stateChoices"
      @apply="table.setFilters"
      @close="closeFilters"
    />

    <UiConfirm
      :open="pending !== null"
      :title="t('accountMove.export.grants.revokeTitle')"
      :question="t('accountMove.export.grants.revokeQuestion')"
      :confirm-label="t('accountMove.export.grants.revokeAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.busy === 'revoke'"
      :acting-label="t('common.saving')"
      destructive
      @close="cancel"
      @confirm="confirm"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
