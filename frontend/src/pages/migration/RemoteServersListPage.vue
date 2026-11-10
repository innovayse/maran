<script setup lang="ts">
/**
 * Remote servers list screen: one server page of the migration sources at a time, searched, sorted
 * and filtered by the panel. Administrators only; the route and the endpoint refuse everyone else.
 * Renders a `<section>`, not a `<main>`. Rows come from the remote servers store; the page never
 * touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and the
 * `state` filter live in the query.
 *
 * **Two kinds of nothing.** No servers at all is the empty state, with the way to add the first.
 * A search or filter matching none is the no-results state, with the way back to every server.
 *
 * **Removing is confirmed, then explained.** After the panel removes a server it answers with the
 * public key it had generated, and the dialog tells the administrator to take that key off the
 * remote server, because removing the server here changes nothing there.
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
import RemoteServersFilterSheet from '../../components/migration/RemoteServersFilterSheet.vue'
import RemoteServersTable from '../../components/migration/RemoteServersTable.vue'
import RemoveRemoteServerDialog from '../../components/migration/RemoveRemoteServerDialog.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useRemoteServersStore } from '../../stores/remoteServers'
import { takeFlashNotice } from '../../utils/flashNotice'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { RemovedRemoteServer, RemoteServer } from '../../types/migrationSource'

const { t } = useI18n()
const store = useRemoteServersStore()
const labels = useListLabels()

const table = useServerTable<RemoteServer>({
  filters: { state: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<RemoteServer>> => {
    return store.list(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The server awaiting a removal confirmation, or `null` when none is. */
const pending: Ref<RemoteServer | null> = ref(null)

/** The key the panel answered with after removing a server, or `null` while none is being shown. */
const removed: Ref<RemovedRemoteServer | null> = ref(null)

/** The removed server's name, kept for the reminder's title. */
const removedName: Ref<string> = ref('')

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
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.state !== '') {
    result.push(labels.chip('state', t('migration.list.filters.state'), t(`migration.states.${applied.state}`)))
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
 * Starts a removal, which then waits for confirmation.
 * @param server The server the person chose to remove.
 * @returns Nothing.
 */
const ask = (server: RemoteServer): void => {
  pending.value = server
}

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed removal. The reminder to take the key off the remote server opens when
 * the panel has answered with it; a refusal stays above the list in the panel's own words.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const server = pending.value
  if (server === null) {
    return
  }
  const answer = await store.remove(server.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pending.value = null
  if (answer !== null) {
    removedName.value = server.name
    removed.value = answer
  }
}

/**
 * Closes the reminder and reads the page again, since the total and the rows that move up from
 * the next page are the server's to say.
 * @returns Resolves once the page settled.
 */
const finishRemoval = async (): Promise<void> => {
  notice.value = t('migration.list.removed', { name: removedName.value })
  removed.value = null
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
  notice.value = takeFlashNotice()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('migration.list.heading')" :subtitle="t('migration.list.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'remote-servers-new' }">{{ t('migration.list.addAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('migration.list.emptyTitle')"
      :description="t('migration.list.emptyDescription')"
    >
      <template #icon><UiIcon name="server" /></template>
      <UiButton :to="{ name: 'remote-servers-new' }">{{ t('migration.list.addAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('migration.list.searchLabel')"
            :placeholder="t('migration.list.searchPlaceholder')"
            :clear-label="t('migration.list.searchClear')"
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
            :title="t('migration.list.noResultsTitle')"
            :description="t('migration.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <RemoteServersTable
              :rows="store.servers"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @remove="ask"
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

    <RemoteServersFilterSheet :open="filtersOpen" :filters="table.filters.value" @apply="table.setFilters" @close="closeFilters" />

    <RemoveRemoteServerDialog
      :pending="pending"
      :acting="store.busy === 'remove'"
      :removed="removed"
      :removed-name="removedName"
      @confirm="confirm"
      @cancel="cancel"
      @done="finishRemoval"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
