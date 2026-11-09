<script setup lang="ts">
/**
 * A list of DNS records as a table: the owner name, the type, the TTL, the data, where the record
 * came from, and a row menu. Props in, events out: the tab owns the paging, the sort and every
 * request, and a zone template's editor reuses it over rows it holds itself.
 *
 * **A record the system manages is read-only.** The panel says which rows a person may change
 * (`editable`); the others show a note in place of the menu. This is presentation: the server
 * refuses the change regardless (rules/security.md).
 *
 * The columns sort by the server's keys only when `sortable` is on; a template's editor holds all
 * its rows and has no server to sort for it.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { dnsRecordValue } from '../../utils/dnsRecordValue'
import type { DnsRecordTypeInfo } from '../../types/dns'
import type { DnsRecordRow } from '../../types/dnsRecord'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DnsRecordsTable}. */
const props = withDefaults(
  defineProps<{
    /** The records to draw. */
    rows: DnsRecordRow[]
    /** Whether the records are being read; the table keeps its shape with placeholder rows. */
    loading?: boolean
    /** How many placeholder rows to draw while loading: the page size. */
    skeletonRows?: number
    /** The list's sort key, `-` prefixed when descending. */
    sort?: string
    /** Whether the headings sort; off for a list held in the browser. */
    sortable?: boolean
    /** Whether to draw the origin column; a template's records all have the same origin. */
    showOrigin?: boolean
    /** The panel's record type catalogue, which names each type and says which inputs it takes. */
    recordTypes?: readonly DnsRecordTypeInfo[]
  }>(),
  {
    loading: false,
    skeletonRows: 5,
    sort: '',
    sortable: true,
    showOrigin: true,
    recordTypes: () => {
      return []
    },
  },
)

/** Events emitted by {@link DnsRecordsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Edit was chosen on a row. */
  (e: 'edit', row: DnsRecordRow): void
  /** Delete was chosen on a row. */
  (e: 'remove', row: DnsRecordRow): void
}>()

const { t } = useI18n()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const origin: UiTableColumn[] = props.showOrigin ? [{ key: 'origin', label: t('dns.records.columns.origin'), kind: 'text' }] : []
  return [
    { key: 'name', label: t('dns.records.columns.name'), kind: 'identifier', sortField: sortField('name'), minWidth: '10rem' },
    { key: 'type', label: t('dns.records.columns.type'), kind: 'mono', sortField: sortField('type') },
    { key: 'ttl', label: t('dns.records.columns.ttl'), kind: 'number', sortField: sortField('ttl') },
    { key: 'value', label: t('dns.records.columns.value'), kind: 'code', minWidth: '14rem' },
    ...origin,
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * A sort field, or nothing when the headings do not sort.
 * @param field The server's sort key for the column.
 * @returns The key when sorting is on, otherwise `undefined`.
 */
const sortField = (field: string): string | undefined => {
  return props.sortable ? field : undefined
}

/**
 * Finds the catalogue entry of a record's type.
 * @param row The record.
 * @returns The entry, or `undefined` while the catalogue has not arrived or does not list the type.
 */
const infoOf = (row: DnsRecordRow): DnsRecordTypeInfo | undefined => {
  return props.recordTypes.find((candidate) => {
    return candidate.type === row.type
  })
}

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
    :caption="t('dns.records.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-name="{ row }">{{ row.name }}</template>
    <template #cell-type="{ row }">{{ infoOf(row)?.label ?? row.type }}</template>
    <template #cell-ttl="{ row }">{{ row.ttl }}</template>
    <template #cell-value="{ row }">{{ dnsRecordValue(row, infoOf(row)) }}</template>
    <template #cell-origin="{ row }">{{ row.originName }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          v-if="row.editable"
          :label="t('common.actions')"
          :aria-label="t('dns.records.rowActions', { name: row.name, type: infoOf(row)?.label ?? row.type })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem @select="emit('edit', row)">{{ t('dns.records.edit') }}</UiDropdownItem>
          <UiDropdownItem destructive @select="emit('remove', row)">{{ t('common.delete') }}</UiDropdownItem>
        </UiDropdown>
        <span v-else class="text-sm text-text-muted">{{ t('dns.records.managed') }}</span>
      </div>
    </template>
  </UiTable>
</template>
