<script setup lang="ts">
/**
 * One page of pull grants as a table: the exported account (the only link in a row, to the export's
 * screen), the state, the address range the pull must come from, how often the package's head was read,
 * when the grant was issued and when it ends, and a row menu that revokes a live grant. The token is
 * never a column: it was shown once. Props in, events out: the page owns the paging, the sort and the
 * revoking.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import ExportGrantStateBadge from './ExportGrantStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { ExportGrantSummary } from '../../types/accountMove'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link ExportGrantsTable}. */
defineProps<{
  /** The page of grants to draw. */
  rows: ExportGrantSummary[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link ExportGrantsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Revoke was chosen on a row; carries the grant. */
  (e: 'revoke', row: ExportGrantSummary): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'account', label: t('accountMove.export.grants.columns.account'), kind: 'identity', sortField: 'accountName', minWidth: '12rem' },
    { key: 'state', label: t('accountMove.export.grants.columns.state'), kind: 'badge' },
    { key: 'allowedFrom', label: t('accountMove.export.grants.columns.allowedFrom'), kind: 'mono' },
    { key: 'reads', label: t('accountMove.export.grants.columns.reads'), kind: 'number' },
    { key: 'createdAt', label: t('accountMove.export.grants.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'expiresAt', label: t('accountMove.export.grants.columns.expiresAt'), kind: 'date', sortField: 'expiresAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
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
    :caption="t('accountMove.export.grants.tableCaption')"
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
          :to="{ name: 'account-export-detail', params: { id: row.exportId } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.accountName }}</RouterLink
        >
      </div>
    </template>
    <template #cell-state="{ row }"><ExportGrantStateBadge :state="row.state" :name="row.stateName" /></template>
    <template #cell-allowedFrom="{ row }">{{ row.allowedFrom ?? t('accountMove.export.grants.fromAnyShort') }}</template>
    <template #cell-reads="{ row }">{{ row.manifestReads }}</template>
    <template #cell-createdAt="{ row }">{{ formatIsoTimestamp(row.createdAt, localeStore.current) }}</template>
    <template #cell-expiresAt="{ row }">{{ formatIsoTimestamp(row.expiresAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          v-if="row.state === 'Live'"
          :label="t('common.actions')"
          :aria-label="t('accountMove.export.grants.rowActions', { name: row.accountName })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem destructive @select="emit('revoke', row)">{{ t('accountMove.export.grants.revoke') }}</UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
