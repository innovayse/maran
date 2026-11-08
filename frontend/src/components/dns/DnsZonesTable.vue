<script setup lang="ts">
/**
 * One page of zones as a table: the apex name (the only link in a row, to the zone's detail page
 * where the records live), the owner for an administrator, the record count, the SOA serial,
 * when it was created, and a row menu. Props in, events out: the page owns the paging, the sort and
 * the confirmation.
 *
 * The owner is named by the panel on each row (`accountName`), never joined from anything the SPA
 * happens to hold.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { DnsZone } from '../../types/dns'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DnsZonesTable}. */
const props = defineProps<{
  /** The page of zones to draw. */
  rows: DnsZone[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
  /** Whether to draw the owner column; a customer's zones are all their own. */
  showOwner: boolean
}>()

/** Events emitted by {@link DnsZonesTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Delete was chosen on a row; carries the zone. */
  (e: 'remove', row: DnsZone): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner ? [{ key: 'owner', label: t('dns.zones.columns.owner'), kind: 'mono' }] : []
  return [
    { key: 'name', label: t('dns.zones.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    ...owner,
    { key: 'recordCount', label: t('dns.zones.columns.records'), kind: 'number' },
    { key: 'serial', label: t('dns.zones.columns.serial'), kind: 'number' },
    { key: 'createdAt', label: t('dns.zones.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
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
    :caption="t('dns.zones.tableCaption')"
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
        <UiIconTile name="globe" />
        <RouterLink
          :to="{ name: 'dns-zone-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.name }}</RouterLink
        >
      </div>
    </template>
    <template #cell-owner="{ row }">{{ row.accountName }}</template>
    <template #cell-recordCount="{ row }">{{ row.recordCount }}</template>
    <template #cell-serial="{ row }">{{ row.serial }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('dns.zones.rowActions', { name: row.name })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem destructive @select="emit('remove', row)">{{ t('common.delete') }}</UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
