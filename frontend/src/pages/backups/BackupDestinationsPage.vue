<script setup lang="ts">
/**
 * The backup destinations screen: where this server records that its copies live. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. State comes exclusively from the destinations store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * **The screen reads, and offers nothing to add or remove, because the panel can do neither.**
 * `BackupDestinationsController` publishes no delete at all, and its `POST` has no success arm on
 * this build: every request it can be given is answered with a refusal, for a remote destination or
 * for a second local one. A control here would be a promise the product cannot keep — the same rule
 * the restore dialog follows when it offers restore on a completed backup and on no other, read in
 * the other direction.
 *
 * **The remote kind is described rather than hidden, and described rather than offered.** The panel
 * knows two kinds of storage and can act on one; the "kinds" section below says exactly that, with
 * no control attached. Leaving it out would make the SPA quieter than the backend, which publishes
 * a named code and a localized sentence precisely so that "you asked for S3 and this build cannot"
 * is sayable. Putting a button on it would make the SPA louder than the backend, which stores
 * nothing.
 *
 * **What the screen must not imply.** A destination is not a choice being offered; there is one,
 * and it is the default that every backup taken so far already went to. Its path is what the agent
 * said on this request and not an instruction — the agent refuses to be told a root and writes to
 * its own — so it is shown as machine text, is not editable anywhere in the panel, and is stated as
 * not established when the panel could not ask. The copies there are not encrypted at rest. All
 * three are said on the screen, because a page listing a storage location reads as a page where
 * storage is configured.
 *
 * **No credential is displayed, because none is received.** No destination on this build holds one;
 * `BackupDestination` declares no secret member, so a key the server one day started sending would
 * not reach this template at all. Should a destination ever carry credentials, they stay write-only:
 * never rendered back after saving and never logged.
 *
 * **The destinations are a server page** (`useServerTable`), like every other list: page, page size,
 * the search on the name, the sort (`name`, `createdAt`) and the `kind` filter live in the URL, and
 * the panel searches, sorts, filters and counts. The list is small, so the panel pages it in memory;
 * that is its business, and the contract here is the same as for a list of thousands.
 *
 * **Layout (redesign 2026-10).** The header carries the module name as its eyebrow; the recorded
 * destinations are one table in a card, and the kinds of storage and the "what this does not cover"
 * lines are `UiCard` panels in a two-column grid from 1024px up and one column below it. A failed
 * read takes the table's place. The default destination carries its badge in its name cell, and
 * its stored name under the heading name whenever the two differ.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useBackupDestinationKindText } from '../../composables/useBackupDestinationKindText'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useBackupDestinationsStore } from '../../stores/backupDestinations'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { BackupDestination, BackupDestinationKind } from '../../types/backupDestination'

/**
 * The kinds of storage the panel knows, and whether this build writes to each. The second half is
 * a statement about this build's code rather than about any row, which is why it is not read from
 * a destination: the screen states it even on a server that has recorded none.
 */
const KINDS: readonly { kind: BackupDestinationKind; admits: boolean }[] = [
  { kind: 'local', admits: true },
  { kind: 's3', admits: false },
]

const { t } = useI18n()
const kindText = useBackupDestinationKindText()
const store = useBackupDestinationsStore()
const localeStore = useLocaleStore()
const labels = useListLabels()

const table = useServerTable<BackupDestination>({
  filters: {
    kind: {
      /**
       * Honours only a kind the panel knows, so a hand-edited link never reaches the server.
       * @param value The `?kind=` value.
       * @returns True for a known kind.
       */
      accepts: (value: string): boolean => {
        return KINDS.some((entry) => {
          return entry.kind === value
        })
      },
    },
  },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<BackupDestination>> => {
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

/** Whether the page on screen holds the default destination, which the hint below the table explains. */
const hasDefault: ComputedRef<boolean> = computed(() => {
  return store.destinations.some((destination) => {
    return destination.isDefault
  })
})

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('backups.destinations.columns.name'), kind: 'identity', sortField: 'name' },
    { key: 'kind', label: t('backups.destinations.columns.kind'), kind: 'text' },
    { key: 'path', label: t('backups.destinations.columns.path'), kind: 'mono' },
    {
      key: 'recordedAt',
      label: t('backups.destinations.columns.recordedAt'),
      kind: 'date',
      align: 'end',
      sortField: 'createdAt',
    },
  ]
})

/** The kinds the filter offers, after an "any kind" choice. */
const kindOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...KINDS.map((entry) => {
      return { value: entry.kind, label: kindText.label(entry.kind) }
    }),
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  // The URL value was honoured only if it names a known kind, so it is found here when it is set.
  const applied = KINDS.find((entry) => {
    return entry.kind === table.filters.value.kind
  })
  return applied === undefined
    ? []
    : [labels.chip('kind', t('backups.destinations.filters.kind'), kindText.label(applied.kind))]
})

/**
 * When the panel first recorded a destination, in the reader's language.
 * @param at The instant as the panel sent it.
 * @returns The formatted instant.
 */
const recordedAt = (at: string): string => {
  return formatIsoTimestamp(at, localeStore.current)
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
 * Clears the search and the filter, the way back from a question that matched nothing.
 * @returns Resolves once the filter left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('backups.list.heading')"
      :title="t('backups.destinations.heading')"
      :subtitle="t('backups.destinations.subtitle')"
      :note="t('backups.destinations.readOnlyNote')"
    />

    <UiCard class="min-w-0">
      <UiSectionHeading class="mb-4" :title="t('backups.destinations.sectionHeading')" />

      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('backups.destinations.searchLabel')"
            :placeholder="t('backups.destinations.searchPlaceholder')"
            :clear-label="t('backups.destinations.searchClear')"
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
          <!-- A refusal is the backend's own localized sentence, so the SPA holds no copy of it. -->
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('backups.destinations.emptyTitle')"
            :description="t('backups.destinations.emptyDescription')"
          >
            <template #icon><UiIcon name="archive" aria-hidden="true" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('backups.destinations.noResultsTitle')"
            :description="t('backups.destinations.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <UiTable
              :caption="t('backups.destinations.tableCaption')"
              :columns="columns"
              :rows="store.destinations"
              row-key="id"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
            >
              <template #cell-name="{ row }">
                <div class="flex min-w-48 items-center gap-3">
                  <UiIconTile name="hardDrive" />
                  <div class="flex min-w-0 flex-col">
                    <!-- The heading is the backend's localized name; the stored one follows whenever
                         the two differ — for exactly one row, the destination this panel seeded and
                         named itself in English — because the stored name is what `psql` prints and
                         a support ticket names. -->
                    <span class="flex flex-wrap items-center gap-2">
                      {{ row.displayName }}
                      <UiBadge v-if="row.isDefault" variant="info">{{ t('backups.destinations.default') }}</UiBadge>
                    </span>
                    <span v-if="row.displayName !== row.name" class="font-mono text-xs text-text-muted">
                      {{ t('common.list.chip', { filter: t('backups.destinations.fields.storedName'), value: row.name }) }}
                    </span>
                  </div>
                </div>
              </template>
              <template #cell-kind="{ row }">{{ kindText.label(row.kind) }}</template>
              <template #cell-path="{ row }">
                <!-- Keyed on the KIND, not on the value: "we could not ask the agent where it
                     writes" is the fact an operator most needs during the incident that stopped the
                     agent, and an empty cell would read as a screen that lost interest. -->
                <span v-if="row.path !== null" class="break-all">{{ row.path }}</span>
                <span v-else-if="row.kind === 'local'" class="font-sans whitespace-normal text-text-secondary">{{
                  t('backups.destinations.pathUnestablished')
                }}</span>
                <span v-else>{{ t('common.emptyValue') }}</span>
              </template>
              <template #cell-recordedAt="{ row }">{{ recordedAt(row.createdAt) }}</template>
            </UiTable>

            <p v-if="hasDefault" class="text-sm text-text-secondary">{{ t('backups.destinations.defaultHint') }}</p>

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

    <div class="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:items-start">
      <UiCard>
        <UiSectionHeading
          class="mb-4"
          :title="t('backups.destinations.kindsHeading')"
          :subtitle="t('backups.destinations.kindsSubtitle')"
        />
        <!-- Both kinds the panel knows about, and which of them this build writes to. There is no
             control here on purpose: the endpoint that would record a destination refuses every
             request it can be given, so a button would offer a choice that does not exist. -->
        <div
          v-for="entry in KINDS"
          :key="entry.kind"
          class="border-t border-border-subtle py-3 first:border-t-0 first:pt-0 last:pb-0"
        >
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-sm font-semibold text-text-primary">{{ kindText.label(entry.kind) }}</span>
            <UiBadge :variant="entry.admits ? 'success' : 'neutral'">
              {{ entry.admits ? t('backups.destinations.available') : t('backups.destinations.unavailable') }}
            </UiBadge>
          </div>
          <p class="mt-1 text-sm text-text-secondary">{{ kindText.description(entry.kind) }}</p>
        </div>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('backups.destinations.aboutHeading')" />
        <!-- Each of these is something a page listing a storage location is read as promising and
             does not. They are on the screen rather than in a doc comment because the operator
             reading a path is the person who would otherwise believe all three. -->
        <p class="text-sm text-text-secondary">{{ t('backups.destinations.about.notEditable') }}</p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.destinations.about.notEncrypted') }}</p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.destinations.about.adminOnly') }}</p>
      </UiCard>
    </div>

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
        <UiSelect v-model="draft.kind" :label="t('backups.destinations.filters.kind')" :options="kindOptions" />
      </template>
    </UiFilterSheet>
  </section>
</template>
