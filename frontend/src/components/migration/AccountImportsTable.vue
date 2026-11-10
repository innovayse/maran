<script setup lang="ts">
/**
 * One page of account imports as a table: the account (the only link in a row, to the import's
 * screen), the server the package came from, the state, the package's size and when it was
 * accepted. A running import carries a second link to its background task. Props in, events out: the
 * page owns the paging and the sort.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiIconTile from '../ui/UiIconTile.vue'
import UiLink from '../ui/UiLink.vue'
import UiTable from '../ui/UiTable.vue'
import AccountImportStateBadge from './AccountImportStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { accountImportTaskRoute } from '../../utils/accountImportTaskRoute'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AccountImport } from '../../types/accountMove'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link AccountImportsTable}. */
defineProps<{
  /** The page of imports to draw. */
  rows: AccountImport[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link AccountImportsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'account', label: t('accountMove.import.list.columns.account'), kind: 'identity', sortField: 'accountName', minWidth: '12rem' },
    { key: 'source', label: t('accountMove.import.list.columns.source'), kind: 'mono', sortField: 'sourceHostName' },
    { key: 'state', label: t('accountMove.import.list.columns.state'), kind: 'badge', sortField: 'state' },
    { key: 'size', label: t('accountMove.import.list.columns.size'), kind: 'number', sortField: 'sizeBytes' },
    { key: 'createdAt', label: t('accountMove.import.list.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
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
    :caption="t('accountMove.import.list.tableCaption')"
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
          :to="{ name: 'account-import-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.accountName }}</RouterLink
        >
      </div>
    </template>
    <template #cell-source="{ row }">{{ row.sourceHostName }}</template>
    <template #cell-state="{ row }">
      <div class="flex flex-col items-start gap-1">
        <AccountImportStateBadge :state="row.state" :name="row.stateName" />
        <UiLink
          v-if="row.state === 'Running'"
          :to="accountImportTaskRoute(row.accountName)"
          :aria-label="t('accountMove.import.list.taskLabel', { name: row.accountName })"
        >
          {{ t('accountMove.import.list.task') }}
        </UiLink>
      </div>
    </template>
    <template #cell-size="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
    <template #cell-createdAt="{ row }">{{ formatIsoTimestamp(row.createdAt, localeStore.current) }}</template>
  </UiTable>
</template>
