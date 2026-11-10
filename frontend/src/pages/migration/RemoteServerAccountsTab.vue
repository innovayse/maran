<script setup lang="ts">
/**
 * The accounts pane of a remote server: the control that runs discovery, what discovery could not
 * read, and one server page of the accounts it found, searched, sorted and filtered by the panel.
 * The URL is the list's state (`useServerTable`): page, page size, search, sort and the `has`
 * filter. Discovery only READS the remote server; nothing there changes.
 *
 * **Three kinds of nothing.** Discovery never ran: the empty state with the way to run it. It ran
 * and found no account: the empty state saying so. A search or filter matching none: the
 * no-results state with the way back. An account is opened in a dialog read on demand.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import DiscoveredAccountDetail from '../../components/migration/DiscoveredAccountDetail.vue'
import DiscoveredAccountsFilterSheet from '../../components/migration/DiscoveredAccountsFilterSheet.vue'
import DiscoveredAccountsTable from '../../components/migration/DiscoveredAccountsTable.vue'
import DiscoveryWarningList from '../../components/migration/DiscoveryWarningList.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useRemoteServersStore } from '../../stores/remoteServers'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { DiscoveredAccount, RemoteServerDetail } from '../../types/migrationSource'

/** Props accepted by {@link RemoteServerAccountsTab}. */
const props = defineProps<{
  /** The server whose accounts are shown. */
  server: RemoteServerDetail
}>()

const { t } = useI18n()
const store = useRemoteServersStore()
const localeStore = useLocaleStore()
const labels = useListLabels()

const table = useServerTable<DiscoveredAccount>({
  filters: { has: {} },
  /**
   * Reads one page of this server's discovered accounts through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<DiscoveredAccount>> => {
    return store.listAccounts(props.server.id, request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The account whose dialog is open (its login name), or `null` while none is. */
const opened: Ref<string | null> = ref(null)

/** Whether discovery has ever run on this server. */
const hasDiscovered: ComputedRef<boolean> = computed(() => {
  return props.server.discoveredAt !== null && props.server.discoveredAt !== undefined
})

/** Whether the server's state lets the panel read it: a pinned host key that was not refused since. */
const canDiscover: ComputedRef<boolean> = computed(() => {
  return props.server.state === 'ready' || props.server.state === 'pinned'
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
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.has !== '') {
    result.push(labels.chip('has', t('migration.accounts.filters.has'), t(`migration.accounts.has.${applied.has}`)))
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
 * Runs discovery, then reads the page again: it replaced every discovered row.
 * @returns Resolves once discovery and the page read have settled.
 */
const discover = async (): Promise<void> => {
  const result = await store.discover(props.server.id)
  if (result !== null) {
    await table.reload()
  }
}

/**
 * Opens an account's dialog and reads its detail.
 * @param account The row chosen.
 * @returns Resolves once the read has settled.
 */
const open = async (account: DiscoveredAccount): Promise<void> => {
  opened.value = account.name
  await store.loadAccount(props.server.id, account.id)
}

/**
 * Closes the account dialog.
 * @returns Nothing.
 */
const closeAccount = (): void => {
  opened.value = null
}

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('migration.accounts.title')" :subtitle="t('migration.accounts.subtitle')">
      <template #action>
        <UiButton :disabled="!canDiscover || store.busy !== null" @click="discover">
          {{
            store.busy === 'discover'
              ? t('migration.accounts.discovering')
              : hasDiscovered
                ? t('migration.accounts.rediscoverAction')
                : t('migration.accounts.discoverAction')
          }}
        </UiButton>
      </template>
    </UiSectionHeading>

    <p v-if="!canDiscover" class="text-sm text-text-muted">{{ t('migration.accounts.needsPin') }}</p>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
    <UiAlert v-if="store.discovery !== null" variant="success">
      {{ t('migration.accounts.discoveredNotice', { count: store.discovery.accountCount }) }}
    </UiAlert>

    <p v-if="hasDiscovered && server.discoveredAt" class="text-sm text-text-muted">
      {{ t('migration.accounts.discoveredAt') }}: {{ formatIsoTimestamp(server.discoveredAt, localeStore.current) }}
    </p>

    <DiscoveryWarningList :warnings="server.warnings" />

    <UiEmptyState
      v-if="!hasDiscovered"
      :title="t('migration.accounts.neverDiscovered')"
      :description="t('migration.accounts.neverDiscoveredDescription')"
    />

    <UiEmptyState
      v-else-if="answeredNothing && !isNarrowed"
      :title="t('migration.accounts.noneFoundTitle')"
      :description="t('migration.accounts.noneFoundDescription')"
    />

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('migration.accounts.searchLabel')"
            :placeholder="t('migration.accounts.searchPlaceholder')"
            :clear-label="t('migration.accounts.searchClear')"
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
            :title="t('migration.accounts.noResultsTitle')"
            :description="t('migration.accounts.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DiscoveredAccountsTable
              :rows="store.accounts"
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

    <DiscoveredAccountsFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      @apply="table.setFilters"
      @close="closeFilters"
    />

    <DiscoveredAccountDetail
      :open="opened !== null"
      :name="opened ?? ''"
      :state="store.accountState"
      :account="store.account"
      :error-message="store.accountErrorMessage"
      @close="closeAccount"
    />
  </div>
</template>
