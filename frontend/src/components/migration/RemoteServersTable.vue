<script setup lang="ts">
/**
 * One page of remote servers as a table: the name (the only link in a row, to the server's page),
 * the address the panel connects to, the state, when it was last tested and how many accounts the
 * last discovery found, and a row menu. Props in, events out: the page owns the paging, the sort
 * and the removal.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import RemoteServerStateBadge from './RemoteServerStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { RemoteServer } from '../../types/migrationSource'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link RemoteServersTable}. */
defineProps<{
  /** The page of servers to draw. */
  rows: RemoteServer[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link RemoteServersTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Remove was chosen on a row; carries the server. */
  (e: 'remove', row: RemoteServer): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('migration.list.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'address', label: t('migration.list.columns.address'), kind: 'mono' },
    { key: 'state', label: t('migration.list.columns.state'), kind: 'badge' },
    { key: 'lastTested', label: t('migration.list.columns.lastTested'), kind: 'date', sortField: 'lastTestedAt' },
    { key: 'accounts', label: t('migration.list.columns.accounts'), kind: 'number' },
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
    :caption="t('migration.list.tableCaption')"
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
        <UiIconTile name="server" />
        <RouterLink
          :to="{ name: 'remote-server-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.name }}</RouterLink
        >
      </div>
    </template>
    <template #cell-address="{ row }">{{ row.host }}:{{ row.port }} ({{ row.sshUser }})</template>
    <template #cell-state="{ row }"><RemoteServerStateBadge :state="row.state" /></template>
    <template #cell-lastTested="{ row }">
      {{ row.lastTestedAt ? formatDate(row.lastTestedAt, localeStore.current) : t('migration.list.never') }}
    </template>
    <template #cell-accounts="{ row }">{{ row.accountsFound ?? t('common.emptyValue') }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('migration.list.rowActions', { name: row.name })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem destructive @select="emit('remove', row)">{{ t('migration.list.removeAction') }}</UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
