<script setup lang="ts">
/**
 * The Available tab: the marketplace catalogue as a searchable list of {@link ModuleCard}s.
 *
 * Search is the panel's (`store.loadCatalogue({ search })`), sent once typing pauses
 * (`useDebouncedSearch`, which also holds back a term the panel would refuse for its length), and
 * more modules are read with "Show more" by the cursor the panel returned; the SPA never filters
 * the list itself. Every state is a designed one: placeholders while the first page loads, an empty
 * catalogue, no result for a search, a catalogue that could not be verified (the panel's message, and
 * every install button disabled), a marketplace that cannot be reached (the panel's message, with the
 * statement that the panel and the installed modules are not affected) and a refusal printed as
 * received. A withdrawn module and a module whose licence is missing are drawn by the card.
 *
 * It makes its one catalogue read when it mounts, and none on window focus or on a timer.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MarketplaceFailureNotice from '../../components/marketplace/MarketplaceFailureNotice.vue'
import ModuleCard from '../../components/marketplace/ModuleCard.vue'
import ModuleOperationDialog from '../../components/marketplace/ModuleOperationDialog.vue'
import { useDebouncedSearch } from '../../composables/useDebouncedSearch'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { LoadingState } from '../../types/loading'
import type { MarketplaceModule, ModuleOperationRequest } from '../../types/marketplace'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { SEARCH_TERM_MAX_LENGTH } from '../../utils/searchTermLimit'
import { useLocaleStore } from '../../stores/locale'

/** How long typing must pause before the search is sent, in milliseconds. */
const SEARCH_DELAY_MS = 300

/** How many placeholder cards are drawn while the first page loads. */
const PLACEHOLDER_COUNT = 3

const { t } = useI18n()
const store = useMarketplaceStore()
const localeStore = useLocaleStore()

/** The search box: its text reaches the store once typing pauses. */
const search = useDebouncedSearch(
  (text: string): void => {
    void store.loadCatalogue({ search: text })
  },
  SEARCH_DELAY_MS,
  true,
)

/** The operation awaiting confirmation, or `null`. */
const request: Ref<ModuleOperationRequest | null> = ref(null)

/** The loading region's state: placeholders only while the first page is read and nothing is held yet. */
const loadState: ComputedRef<LoadingState> = computed(() => {
  return store.catalogueLoading && store.items.length === 0 ? 'loading' : 'loaded'
})

/** The message under the search box while its text is longer than the panel accepts. */
const searchError: ComputedRef<string | null> = computed(() => {
  return search.tooLong.value ? t('common.list.searchTooLong', { max: SEARCH_TERM_MAX_LENGTH }) : null
})

/** Whether the last read ended in a refusal or no answer, in which case "empty" would be a false claim. */
const failed: ComputedRef<boolean> = computed(() => {
  return store.errorMessage !== null || store.unreachable
})

/** When the catalogue was fetched, formatted for the reader, or `null`. */
const fetchedAt: ComputedRef<string | null> = computed(() => {
  return store.fetchedAt === null ? null : formatIsoTimestamp(store.fetchedAt, localeStore.current)
})

/**
 * Whether an operation on a module is in flight (a request, or a task still being followed).
 * @param id The module's machine id.
 * @returns True while its install button must stay off.
 */
const isBusy = (id: string): boolean => {
  return store.busy[id] === true || store.operations[id] !== undefined
}

/**
 * Opens the install confirmation for a module.
 * @param module The module whose Install button was pressed.
 * @returns Nothing.
 */
const askInstall = (module: MarketplaceModule): void => {
  request.value = { kind: 'install', id: module.id, name: module.displayName, version: module.latestVersion }
}

/**
 * Reads the first page again, as the retry of a failed read.
 * @returns Resolves once the read settled.
 */
const retry = async (): Promise<void> => {
  await store.loadCatalogue({ reset: true })
}

/**
 * Reads the next page of the catalogue.
 * @returns Resolves once the read settled.
 */
const more = async (): Promise<void> => {
  await store.loadMore()
}

onMounted(() => {
  void store.loadCatalogue({ search: '', reset: true })
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <UiToolbar>
      <UiSearchInput
        :model-value="search.query.value"
        :label="t('marketplace.available.searchLabel')"
        :placeholder="t('marketplace.available.searchPlaceholder')"
        :clear-label="t('marketplace.available.searchClear')"
        :error="searchError"
        hide-label
        @update:model-value="search.type"
      />
    </UiToolbar>

    <MarketplaceFailureNotice
      :message="store.errorMessage"
      :unreachable="store.unreachable"
      :code="store.errorCode"
      @retry="retry"
    />

    <UiAlert v-if="store.verified === false" variant="warning" data-testid="catalogue-unverified">
      <p class="font-medium">{{ t('marketplace.unverified.title') }}</p>
      <p v-if="store.unverifiedMessage !== null">{{ store.unverifiedMessage }}</p>
      <p>{{ t('marketplace.unverified.installsOff') }}</p>
    </UiAlert>

    <UiLoadingRegion :state="loadState">
      <ul v-if="loadState === 'loading'" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" aria-hidden="true">
        <li v-for="n in PLACEHOLDER_COUNT" :key="n" class="min-w-0"><UiSkeleton shape="card" /></li>
      </ul>
      <template v-else-if="store.items.length > 0">
        <ul
          class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3"
          :aria-label="t('marketplace.available.listLabel')"
          data-testid="catalogue-list"
        >
          <li v-for="module in store.items" :key="module.id" class="min-w-0">
            <ModuleCard
              :module="module"
              :catalogue-verified="store.verified !== false"
              :busy="isBusy(module.id)"
              @install="askInstall"
            />
          </li>
        </ul>
        <div class="mt-4 flex flex-col items-center gap-2">
          <UiButton v-if="store.nextCursor !== null" variant="secondary" :disabled="store.loadingMore" @click="more">
            {{ store.loadingMore ? t('marketplace.available.loadingMore') : t('marketplace.available.loadMore') }}
          </UiButton>
          <p v-if="fetchedAt !== null" class="text-sm text-text-muted">
            {{ t('marketplace.available.fetchedAt', { time: fetchedAt }) }}
          </p>
        </div>
      </template>
      <UiEmptyState
        v-else-if="!failed && !store.catalogueLoading"
        :title="search.query.value.trim() === '' ? t('marketplace.available.emptyTitle') : t('marketplace.available.noResultsTitle')"
        :description="
          search.query.value.trim() === ''
            ? t('marketplace.available.emptyDescription')
            : t('marketplace.available.noResultsDescription')
        "
      />
    </UiLoadingRegion>

    <ModuleOperationDialog :request="request" @close="request = null" />
  </div>
</template>
