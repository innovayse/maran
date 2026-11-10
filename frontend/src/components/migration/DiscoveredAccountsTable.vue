<script setup lang="ts">
/**
 * One page of the accounts discovery found on a remote server: the account (a control that opens
 * its details), its home, the disk it uses, and how many sites, databases, cron entries and
 * mailboxes it holds. Props in, events out: the tab owns the paging, the sort and the details.
 *
 * A figure the panel could not read is said so (`diskKnown` is false) rather than shown as zero.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import { formatBytes } from '../../utils/formatBytes'
import type { DiscoveredAccount } from '../../types/migrationSource'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DiscoveredAccountsTable}. */
defineProps<{
  /** The page of discovered accounts to draw. */
  rows: DiscoveredAccount[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link DiscoveredAccountsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** An account was chosen to be opened; carries it. */
  (e: 'open', row: DiscoveredAccount): void
}>()

const { t } = useI18n()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('migration.accounts.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'home', label: t('migration.accounts.columns.home'), kind: 'mono' },
    { key: 'disk', label: t('migration.accounts.columns.disk'), kind: 'number', sortField: 'disk' },
    { key: 'sites', label: t('migration.accounts.columns.sites'), kind: 'number', sortField: 'sites' },
    { key: 'databases', label: t('migration.accounts.columns.databases'), kind: 'number', sortField: 'databases' },
    { key: 'cron', label: t('migration.accounts.columns.cron'), kind: 'number' },
    { key: 'mailboxes', label: t('migration.accounts.columns.mailboxes'), kind: 'number' },
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
    :caption="t('migration.accounts.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-name="{ row }">
      <div class="flex items-center gap-3">
        <UiIconTile name="user" />
        <UiButton
          variant="ghost"
          :aria-label="t('migration.accounts.rowOpen', { name: row.name })"
          @click="emit('open', row)"
        >
          {{ row.name }}
        </UiButton>
      </div>
    </template>
    <template #cell-home="{ row }">{{ row.home }}</template>
    <template #cell-disk="{ row }">
      {{ row.diskKnown ? formatBytes(row.diskBytes) : t('migration.accounts.diskUnknown') }}
    </template>
    <template #cell-sites="{ row }">{{ row.siteCount }}</template>
    <template #cell-databases="{ row }">{{ row.databaseCount }}</template>
    <template #cell-cron="{ row }">{{ row.cronEntryCount }}</template>
    <template #cell-mailboxes="{ row }">{{ row.mailboxCount }}</template>
  </UiTable>
</template>
