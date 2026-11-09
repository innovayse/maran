<script setup lang="ts">
/**
 * The API keys screen: one server page of an owner's keys, searched, sorted and filtered by state by
 * the panel, with a create dialog and a revoke confirmation. Renders a `<section>`, not a `<main>`;
 * rows come from the API keys store, and the page never touches the API layer (rules/vue.md).
 *
 * **One page, three owners** (`ApiKeyOwner`), told apart by who is signed in and by the route, and
 * never by a second copy of the screen: an administrator sees the administrator's own keys; a
 * reseller sees its own (Settings, API keys, issue #435); and an administrator who arrives from a
 * reseller's page (`id` is set) sees THAT reseller's keys and may revoke them but not create
 * one, so no create button is drawn. The role is advice about which endpoint to call: the panel
 * answers by role on every request, whatever this page asks.
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and `?status=`.
 * The state filter opens as a modal from `sm` up and a bottom sheet below it (`UiFilterSheet`).
 *
 * **The secret is shown once.** A create answers with the key, the dialog shows it, and closing the
 * dialog or leaving the page forgets it: the store's copy is cleared on both.
 *
 * **Two kinds of nothing.** No keys at all is the empty state; a search or filter matching none is
 * the no-results state with the way back to every key. A server's error is rendered verbatim.
 */
import { computed, onBeforeUnmount, ref, type ComputedRef, type Ref } from 'vue'
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
import ApiKeyCreateDialog from '../../components/api-keys/ApiKeyCreateDialog.vue'
import ApiKeyCreatedDialog from '../../components/api-keys/ApiKeyCreatedDialog.vue'
import ApiKeyRevokeConfirm from '../../components/api-keys/ApiKeyRevokeConfirm.vue'
import ApiKeysFilterSheet from '../../components/api-keys/ApiKeysFilterSheet.vue'
import ApiKeysTable from '../../components/api-keys/ApiKeysTable.vue'
import UiLink from '../../components/ui/UiLink.vue'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useIntegrationKeyActions } from '../../composables/useIntegrationKeyActions'
import { useReloadOnLocale } from '../../composables/useReloadOnLocale'
import { useServerTable } from '../../composables/useServerTable'
import { useApiKeysStore } from '../../stores/apiKeys'
import type { ApiKey, ApiKeyOwner } from '../../types/apiKey'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

/** Props accepted by {@link ApiKeysPage}, bound from the route. */
const props = defineProps<{
  /** The reseller whose keys an administrator oversees, from `/resellers/:id/api-keys`; absent on the owner's own screen. */
  id?: string
}>()

/** The words of one owner: the heading, the subtitle and the empty-state description, as translation keys. */
interface OwnerWords {
  heading: string
  subtitle: string
  emptyDescription: string
}

/** The words for each kind of owner, so a new kind of owner cannot be added without its words. */
const WORDS_BY_OWNER: Record<ApiKeyOwner['kind'], OwnerWords> = {
  overseen: {
    heading: 'apiKeys.overseen.heading',
    subtitle: 'apiKeys.overseen.subtitle',
    emptyDescription: 'apiKeys.overseen.emptyDescription',
  },
  reseller: {
    heading: 'apiKeys.heading',
    subtitle: 'apiKeys.reseller.subtitle',
    emptyDescription: 'apiKeys.reseller.emptyDescription',
  },
  administrator: {
    heading: 'apiKeys.heading',
    subtitle: 'apiKeys.subtitle',
    emptyDescription: 'apiKeys.emptyDescription',
  },
}

const { t } = useI18n()
const store = useApiKeysStore()
const labels = useListLabels()
const { isReseller } = useCallerRole()

/** Whose keys this page manages: the reseller named in the route, else the signed-in reseller's own, else the administrator's own. */
const owner: ComputedRef<ApiKeyOwner> = computed(() => {
  if (props.id !== undefined) {
    return { kind: 'overseen', resellerId: props.id }
  }
  return isReseller.value ? { kind: 'reseller' } : { kind: 'administrator' }
})

/** Whether the owner creates keys here, which is every case but an administrator overseeing a reseller. */
const canCreate: ComputedRef<boolean> = computed(() => {
  return owner.value.kind !== 'overseen'
})

/** The translation keys for the words that differ by owner: the heading, the subtitle and the empty state. */
const words: ComputedRef<OwnerWords> = computed(() => {
  return WORDS_BY_OWNER[owner.value.kind]
})

const table = useServerTable<ApiKey>({
  filters: { status: { accepts: store.acceptsStatus } },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<ApiKey>> => {
    return store.list(owner.value, request)
  },
})

const { createOpen, pending, notice, openCreate, closeCreate, create, askRevoke, cancelRevoke, confirmRevoke, dismissNotice } =
  useIntegrationKeyActions(() => {
    return owner.value
  }, table.reload)

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

/** One removable chip per applied filter, the state under the panel's name. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const status = table.filters.value.status ?? ''
  return status === '' ? [] : [labels.chip('status', t('apiKeys.filters.status'), store.statusName(status))]
})

/**
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
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

// The names of scopes and states are the panel's, in the language the request carried, so they are
// read again whenever the interface language changes.
useReloadOnLocale(async (): Promise<void> => {
  const current = owner.value
  // The scopes are for the create dialog, which an overseeing administrator does not have.
  await Promise.all([current.kind === 'overseen' ? Promise.resolve() : store.loadScopes(current), store.loadStatuses(current)])
})

// The secret must not survive a navigation; the store outlives this page.
onBeforeUnmount((): void => {
  store.dismissKey()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t(words.heading)" :subtitle="t(words.subtitle)">
      <template #actions>
        <UiLink v-if="props.id !== undefined" :to="{ name: 'reseller-detail', params: { id: props.id } }" icon="chevronLeft">
          {{ t('apiKeys.overseen.back') }}
        </UiLink>
        <UiButton v-if="canCreate" @click="openCreate">
          <UiIcon name="plus" size="sm" />
          {{ t('apiKeys.create') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.revokeErrorMessage !== null" variant="error">{{ store.revokeErrorMessage }}</UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('apiKeys.emptyTitle')"
      :description="t(words.emptyDescription)"
    >
      <template #icon><UiIcon name="keyRound" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('apiKeys.searchLabel')"
            :placeholder="t('apiKeys.searchPlaceholder')"
            :clear-label="t('apiKeys.searchClear')"
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
            :title="t('apiKeys.noResultsTitle')"
            :description="t('apiKeys.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <ApiKeysTable
              :rows="store.keys"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              :scope-name="store.scopeName"
              @sort="table.toggleSort"
              @revoke="askRevoke"
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

    <ApiKeysFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :statuses="store.statuses"
      @apply="table.setFilters"
      @close="closeFilters"
    />

    <ApiKeyCreateDialog
      v-if="createOpen && canCreate"
      :scopes="store.scopes"
      :submitting="store.creating"
      :error-message="store.createErrorMessage"
      @submit="create"
      @close="closeCreate"
    />

    <ApiKeyRevokeConfirm
      v-if="pending !== null"
      :pending="pending"
      :acting="store.revoking"
      @close="cancelRevoke"
      @confirm="confirmRevoke"
    />

    <ApiKeyCreatedDialog v-if="store.revealedKey !== null" :revealed="store.revealedKey" @close="store.dismissKey" />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
