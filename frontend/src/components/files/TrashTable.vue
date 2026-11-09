<script setup lang="ts">
/**
 * One page of the trash as a table: a tick, the name with the folder it came from, what it is, its
 * size, when it was trashed and when the retention job will remove it for good. Props in, events
 * out: the page owns the paging, the sort and the selection. Names are drawn as text, never markup.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiTable from '../ui/UiTable.vue'
import FileEntryIcon from './FileEntryIcon.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatDate } from '../../utils/formatDate'
import type { TrashEntry } from '../../types/file'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link TrashTable}. */
defineProps<{
  /** The page of trashed entries to draw. */
  rows: TrashEntry[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
  /** The trash ids of the ticked rows. */
  selectedIds: readonly string[]
}>()

/** Events emitted by {@link TrashTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** A row was ticked or unticked. */
  (e: 'select', id: string, selected: boolean): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('filesTrash.columns.name'), kind: 'identity', sortField: 'name', minWidth: '14rem' },
    { key: 'originalPath', label: t('filesTrash.columns.originalPath'), kind: 'mono', minWidth: '12rem' },
    { key: 'size', label: t('filesTrash.columns.size'), kind: 'number', sortField: 'size' },
    { key: 'deletedAt', label: t('filesTrash.columns.deletedAt'), kind: 'date', sortField: 'deletedAt' },
    { key: 'expiresAt', label: t('filesTrash.columns.expiresAt'), kind: 'date', sortField: 'expiresAt' },
  ]
})

/**
 * Says how big an entry is; a folder's size is only known when the agent reports it.
 * @param entry The trashed entry.
 * @returns The formatted size.
 */
const sizeLabel = (entry: TrashEntry): string => {
  return formatBytes(entry.sizeBytes)
}
</script>

<template>
  <div data-testid="trash-table">
    <UiTable
      :caption="t('filesTrash.tableCaption')"
      :columns="columns"
      :rows="rows"
      row-key="id"
      :loading="loading"
      :skeleton-rows="skeletonRows"
      :sort="sort"
      @sort="emit('sort', $event)"
    >
      <template #cell-name="{ row }">
        <div class="flex min-w-0 items-center gap-2">
          <UiCheckbox
            :model-value="selectedIds.includes(row.id)"
            :label="t('filesTrash.selectRow', { name: row.name })"
            hide-label
            @update:model-value="emit('select', row.id, $event)"
          />
          <FileEntryIcon :entry="{ kind: row.kind, isSymlink: false }" />
          <span class="wrap-anywhere font-semibold">{{ row.name }}</span>
        </div>
      </template>
      <template #cell-originalPath="{ row }">{{ row.originalPath }}</template>
      <template #cell-size="{ row }">{{ sizeLabel(row) }}</template>
      <template #cell-deletedAt="{ row }">{{ formatDate(row.deletedAt, localeStore.current) }}</template>
      <template #cell-expiresAt="{ row }">{{ formatDate(row.expiresAt, localeStore.current) }}</template>
    </UiTable>
  </div>
</template>
