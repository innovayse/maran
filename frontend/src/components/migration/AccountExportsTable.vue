<script setup lang="ts">
/**
 * One page of account exports as a table: the account (the only link in a row, to the export's
 * screen), the package's size, how many databases it holds, when it was made and when it stops being
 * served. Props in, events out: the page owns the paging and the sort.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AccountExport } from '../../types/accountMove'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link AccountExportsTable}. */
defineProps<{
  /** The page of exports to draw. */
  rows: AccountExport[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link AccountExportsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'account', label: t('accountMove.export.list.columns.account'), kind: 'identity', sortField: 'accountName', minWidth: '12rem' },
    { key: 'size', label: t('accountMove.export.list.columns.size'), kind: 'number', sortField: 'sizeBytes' },
    { key: 'databases', label: t('accountMove.export.list.columns.databases'), kind: 'number' },
    { key: 'createdAt', label: t('accountMove.export.list.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'expiresAt', label: t('accountMove.export.list.columns.expiresAt'), kind: 'date', sortField: 'expiresAt' },
  ]
})

/**
 * Forwards a sortable heading's press.
 * @param field The column's sort field.
 * @returns Nothing; the event is emitted synchronously.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('accountMove.export.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-account="{ row }">
      <div class="flex items-center gap-3">
        <UiIconTile name="server" />
        <RouterLink
          :to="{ name: 'account-export-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.accountName }}</RouterLink
        >
      </div>
    </template>
    <template #cell-size="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
    <template #cell-databases="{ row }">{{ row.databaseCount }}</template>
    <template #cell-createdAt="{ row }">{{ formatIsoTimestamp(row.createdAt, localeStore.current) }}</template>
    <template #cell-expiresAt="{ row }">{{ formatIsoTimestamp(row.expiresAt, localeStore.current) }}</template>
  </UiTable>
</template>
