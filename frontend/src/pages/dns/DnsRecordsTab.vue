<script setup lang="ts">
/**
 * The records pane of a zone: one server page of the zone's records at a time, searched, sorted
 * and filtered by the panel, with the add, change and delete actions. The URL is the list's state
 * (`useServerTable`): page, page size, search, sort and the `type` filter.
 *
 * **A record the system manages is read-only** (the panel says which rows are `editable`); the
 * table shows a note in place of the menu, and the server refuses a change to it regardless.
 *
 * **Every change is applied by the panel before it answers**, so a refusal is the server's own
 * localized sentence: it is shown inside the dialog for a save and above the list for a delete,
 * as it was sent. After a change the page is read again, since where a row falls is the server's
 * to say, and the zone's figures (record count, serial) are refreshed through `changed`.
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
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import DnsRecordDialog from '../../components/dns/DnsRecordDialog.vue'
import DnsRecordsFilterSheet from '../../components/dns/DnsRecordsFilterSheet.vue'
import DnsRecordsTable from '../../components/dns/DnsRecordsTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useDnsCatalogStore } from '../../stores/dnsCatalog'
import { useDnsRecordsStore } from '../../stores/dnsRecords'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { DnsRecord, DnsRecordRequest, DnsRecordRow } from '../../types/dnsRecord'

/** Props accepted by {@link DnsRecordsTab}. */
const props = defineProps<{
  /** The zone whose records are shown. */
  zoneId: string
}>()

/** Events emitted by {@link DnsRecordsTab}. */
const emit = defineEmits<{
  /** A record was added, changed or deleted, so the zone's own figures are out of date. */
  (e: 'changed'): void
}>()

const { t } = useI18n()
const store = useDnsRecordsStore()
const catalogStore = useDnsCatalogStore()
const labels = useListLabels()

const table = useServerTable<DnsRecord>({
  filters: { type: {} },
  /**
   * Reads one page of this zone's records through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<DnsRecord>> => {
    return store.list(props.zoneId, request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The record the dialog edits (`null` inside means a new one), or `null` while it is closed. */
const editing: Ref<{ record: DnsRecord | null } | null> = ref(null)

/** The record awaiting a delete confirmation, or `null` when none is. */
const pending: Ref<DnsRecordRow | null> = ref(null)

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether the reference data the dialog needs has arrived, so a record cannot be added blind. */
const canEdit: ComputedRef<boolean> = computed(() => {
  return catalogStore.catalog !== null
})

/** The label of the pending record's type, for the confirmation's title. */
const pendingTypeLabel: ComputedRef<string> = computed(() => {
  const type = pending.value?.type ?? ''
  const info = catalogStore.catalog?.types.find((candidate) => {
    return candidate.type === type
  })
  return info?.label ?? type
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.type !== '') {
    const info = catalogStore.catalog?.types.find((candidate) => {
      return candidate.type === applied.type
    })
    result.push(labels.chip('type', t('dns.records.filters.type'), info?.label ?? applied.type))
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
 * Opens the dialog for a new record.
 * @returns Nothing.
 */
const startAdd = (): void => {
  store.clearErrors()
  editing.value = { record: null }
}

/**
 * Opens the dialog for an existing record. The row the table emits is the record the page holds.
 * @param row The row the person chose.
 * @returns Nothing.
 */
const startEdit = (row: DnsRecordRow): void => {
  store.clearErrors()
  const held = store.records.find((record) => {
    return record.id === row.id
  })
  // A row the page no longer holds (the list was read again under the person's hand) is not
  // opened as a new record: that would present an add form for a change they asked for.
  if (held === undefined) {
    return
  }
  editing.value = { record: held }
}

/**
 * Closes the record dialog without saving.
 * @returns Nothing.
 */
const closeDialog = (): void => {
  editing.value = null
}

/**
 * Saves the record the dialog collected. On success the dialog closes and the page is read again;
 * on failure the store holds the server's own message and the dialog renders it verbatim.
 * @param request The record, already read by the dialog's client-side checks.
 * @returns Resolves once the attempt has settled.
 */
const save = async (request: DnsRecordRequest): Promise<void> => {
  const target = editing.value
  if (target === null) {
    return
  }
  const wasNew = target.record === null
  if (await store.save(props.zoneId, target.record?.id ?? null, request)) {
    editing.value = null
    notice.value = wasNew ? t('dns.records.added') : t('dns.records.saved')
    emit('changed')
    await table.reload()
  }
}

/**
 * Starts a delete, which then waits for confirmation.
 * @param row The record the person chose to delete.
 * @returns Nothing.
 */
const ask = (row: DnsRecordRow): void => {
  pending.value = row
}

/**
 * Abandons a pending delete.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed delete and reads the page again.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const row = pending.value
  if (row === null) {
    return
  }
  const deleted = await store.remove(props.zoneId, row.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pending.value = null
  if (deleted) {
    notice.value = t('dns.records.deleted')
    emit('changed')
    await table.reload()
  }
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
  void catalogStore.load()
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <UiCard>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('dns.records.searchLabel')"
            :placeholder="t('dns.records.searchPlaceholder')"
            :clear-label="t('dns.records.searchClear')"
          />
          <UiFilterTrigger
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
          <UiButton :disabled="!canEdit" @click="startAdd">{{ t('dns.records.addAction') }}</UiButton>
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
            v-if="answeredNothing && !isNarrowed"
            :title="t('dns.records.emptyTitle')"
            :description="t('dns.records.emptyDescription')"
          >
            <template #icon><UiIcon name="globe" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('dns.records.noResultsTitle')"
            :description="t('dns.records.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DnsRecordsTable
              :rows="store.records"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              :record-types="catalogStore.catalog?.types ?? []"
              @sort="table.toggleSort"
              @edit="startEdit"
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

    <DnsRecordsFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :record-types="catalogStore.catalog?.types ?? []"
      @apply="table.setFilters"
      @close="closeFilters"
    />

    <DnsRecordDialog
      v-if="editing !== null"
      :record="editing.record"
      :catalog="catalogStore.catalog"
      :submitting="store.saving"
      :error-message="store.saveErrorMessage"
      @submit="save"
      @close="closeDialog"
    />

    <UiConfirm
      :open="pending !== null"
      :title="t('dns.records.confirmDeleteTitle', { name: pending?.name ?? '', type: pendingTypeLabel })"
      :question="t('dns.records.confirmDelete')"
      :confirm-label="t('dns.records.confirmDeleteAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('common.saving')"
      @close="cancel"
      @confirm="confirm"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </div>
</template>
