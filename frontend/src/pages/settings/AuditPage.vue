<script setup lang="ts">
/**
 * The audit journal (spec §10): who did what, when, from where, and whether it worked. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under.
 *
 * Read-only by construction. Entries are written by the backend from inside the handlers that
 * perform an action, so there is nothing here to edit and no route that could amend one. The
 * endpoint is administrators-only; a customer who reaches this URL sees the panel's own refusal,
 * rendered verbatim like any other.
 *
 * **The journal is a server page** (`useServerTable`): page, page size, the search (the actor, the
 * subject or the address), the sort (`occurredAt`, `actor`, `action`) and the `action`, `outcome`,
 * `from` and `to` filters live in the URL, and the panel searches, filters, sorts and counts. The
 * journal records no account, so there is no account filter; a failed action is found with the
 * outcome filter rather than counted off whatever page is on screen.
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
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import AuditFilterSheet from '../../components/audit/AuditFilterSheet.vue'
import AuditTable from '../../components/audit/AuditTable.vue'
import { useAuditFilters } from '../../composables/useAuditFilters'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAuditStore } from '../../stores/audit'
import type { AuditEvent } from '../../types/audit'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const auditStore = useAuditStore()
const labels = useListLabels()
const auditFilters = useAuditFilters()

const table = useServerTable<AuditEvent>({
  filters: auditFilters.schema,
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<AuditEvent>> => {
    return auditStore.list(request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

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

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return auditFilters.chips(table.filters.value, actionName.value)
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

onMounted(async () => {
  await auditStore.loadActions()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('app.audit.eyebrow')"
      :title="t('app.audit.heading')"
      :subtitle="t('app.audit.subtitle')"
    />

    <UiCard class="min-w-0">
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
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
      @update:model-value="table.setFilters"
      @close="closeFilters"
    />
  </section>
</template>
