<script setup lang="ts">
/**
 * The audit journal (spec §10): who did what, when, from where, and whether it worked. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under.
 *
 * Read-only by construction. Entries are written by the backend from inside the handlers that
 * perform an action, so there is nothing here to edit and no route that could amend one. The
 * administrator's endpoint is administrators-only; a customer who reaches this URL sees the panel's
 * own refusal, rendered verbatim like any other.
 *
 * **One screen, two journals.** An administrator reads the whole journal. A reseller reads its own
 * part of it (issue #370): the entries about its accounts and its login, from an endpoint that
 * answers in the same shape. The page asks for the scope its role has and shows the same table and
 * filters; the export and the upkeep cards (retention, integrity) are the administrator's and are
 * not drawn for a reseller. The scope is advice about which endpoint to call — what each caller may
 * read is the panel's decision on every request.
 *
 * **The journal is a server page** (`useServerTable`): page, page size, the search (the actor, the
 * subject or the address), the sort (`occurredAt`, `actor`, `action`) and the `action`, `outcome`,
 * `from`, `to`, `actor` and `account` filters live in the URL, and the panel searches, filters,
 * sorts and counts. A failed action is found with the outcome filter rather than counted off
 * whatever page is on screen. The account filter narrows to the entries the panel could attribute
 * to an account; the others carry none.
 *
 * **The export is the journal's, not the page's.** {@link ../../components/audit/AuditExportMenu.vue}
 * downloads everything the current search and filters match, as CSV or JSON. The panel's refusal
 * (an export too large to hand over, an exhausted allowance) is shown here in its own words.
 *
 * **Upkeep sits under the journal:** how long entries are kept
 * ({@link ../../components/audit/AuditRetentionCard.vue}) and whether the tamper-evidence chain
 * still holds ({@link ../../components/audit/AuditIntegrityCard.vue}). Both are read and written
 * through their own store; the table is unaffected by them.
 *
 * The parts are their own components, props in and events out:
 * {@link ../../components/audit/AuditTable.vue} and
 * {@link ../../components/audit/AuditFilterSheet.vue}.
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
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import AuditExportMenu from '../../components/audit/AuditExportMenu.vue'
import AuditFilterSheet from '../../components/audit/AuditFilterSheet.vue'
import AuditIntegrityCard from '../../components/audit/AuditIntegrityCard.vue'
import AuditRetentionCard from '../../components/audit/AuditRetentionCard.vue'
import AuditTable from '../../components/audit/AuditTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useAuditFilters } from '../../composables/useAuditFilters'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAuditStore } from '../../stores/audit'
import type { AuditEvent, AuditScope } from '../../types/audit'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const auditStore = useAuditStore()
const labels = useListLabels()
const auditFilters = useAuditFilters(() => {
  return auditStore.outcomes
})
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
// The name is read only for an id the journal's own filter honours, so a hand-edited link never
// makes the page ask the panel for an account the filter treats as absent.
const { name: accountFilterName } = useAccountFilter(auditFilters.schema.account?.accepts)
const { isReseller } = useCallerRole()

/** Whose journal this page reads: a reseller its own part of it, anyone else the whole one. */
const scope: ComputedRef<AuditScope> = computed(() => {
  return isReseller.value ? 'reseller' : 'all'
})

const table = useServerTable<AuditEvent>({
  filters: auditFilters.schema,
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<AuditEvent>> => {
    return auditStore.list(request, scope.value)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The success toast's text after an export, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the journal is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * The applied action as an operator reads it: the panel's name when the journal's actions are
 * known, the machine constant otherwise.
 */
const actionName: ComputedRef<string> = computed(() => {
  const applied = table.filters.value.action ?? ''
  return auditStore.actions.find((choice) => {
    return choice.action === applied
  })?.actionName ?? applied
})

/** The applied outcome as an administrator reads it: the panel's name, or the value itself while the list is unknown. */
const outcomeName: ComputedRef<string> = computed(() => {
  const applied = table.filters.value.outcome ?? ''
  return auditStore.outcomes.find((choice) => {
    return choice.outcome === applied
  })?.outcomeName ?? applied
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return auditFilters.chips(table.filters.value, actionName.value, outcomeName.value, accountFilterName.value)
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
 * Says the export reached the browser.
 * @returns Nothing.
 */
const exported = (): void => {
  notice.value = t('app.audit.export.done')
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
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
  // Feedback belongs to the visit (issue #508): an export refusal from an earlier visit is not current.
  auditStore.clearFeedback()
  await Promise.all([auditStore.loadActions(scope.value), auditStore.loadOutcomes(scope.value)])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :title="t('app.audit.heading')"
      :subtitle="isReseller ? t('app.audit.resellerSubtitle') : t('app.audit.subtitle')"
    >
      <template v-if="!isReseller" #actions>
        <AuditExportMenu :request="table.request.value" @exported="exported" />
      </template>
    </UiPageHeading>

    <!-- A refused export, in the panel's own words: too large to hand over, or the allowance used. -->
    <UiAlert v-if="auditStore.exportState.state === 'failed'" variant="error">
      {{ auditStore.exportMessage ?? t('common.list.unreachable') }}
    </UiAlert>

    <UiCard class="min-w-0">
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('app.audit.searchLabel')"
            :placeholder="t('app.audit.searchPlaceholder')"
            :clear-label="t('app.audit.searchClear')"
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

          <!-- Two kinds of nothing: an empty journal, and a search or filter matching none. -->
          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('app.audit.emptyTitle')"
            :description="t('app.audit.emptyDescription')"
          >
            <template #icon><UiIcon name="fileText" size="lg" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('app.audit.noResultsTitle')"
            :description="t('app.audit.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <AuditTable
              :rows="auditStore.events"
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

    <AuditFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :actions="auditStore.actions"
      :outcomes="auditStore.outcomes"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      @update:model-value="table.setFilters"
      @close="closeFilters"
      @search="searchAccounts"
    />

    <div v-if="!isReseller" class="grid gap-4 lg:grid-cols-2">
      <AuditRetentionCard />
      <AuditIntegrityCard />
    </div>

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
