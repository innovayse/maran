<script setup lang="ts">
/**
 * One page of aliases, forwarders and catch-alls as a table: the address, the owner (for a reader
 * who does not own every row), what it is, where the mail goes, and each row's actions menu. Dumb by
 * the usual contract — the page owns the paging, the sort state and the dialogs; this renders rows
 * and reports clicks.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import type { MailAlias, MailAliasAction } from '../../types/mailAlias'

/** Props accepted by {@link MailAliasesTable}. */
const props = defineProps<{
  /** The rows of the page the panel answered. */
  rows: MailAlias[]
  /** Whether to draw the owner column; a customer owns every row. */
  showOwner: boolean
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link MailAliasesTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked for a dialog. */
  (e: 'act', row: MailAlias, action: MailAliasAction): void
}>()

const { t } = useI18n()

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The owner column is drawn only when the page says the reader is not the owner of every row.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner
    ? [{ key: 'owner', label: t('mail.aliases.columns.account'), kind: 'mono' }]
    : []
  return [
    { key: 'address', label: t('mail.aliases.columns.address'), kind: 'identifier', sortField: 'address' },
    ...owner,
    { key: 'kind', label: t('mail.aliases.columns.kind'), kind: 'badge' },
    { key: 'targets', label: t('mail.aliases.columns.targets'), kind: 'code', minWidth: '14rem' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account an alias belongs to, as the panel named it on the row.
 * @param alias The alias row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (alias: MailAlias): string => {
  return alias.ownerName === '' ? t('common.emptyValue') : alias.ownerName
}
</script>

<template>
  <UiTable
    :caption="t('mail.aliases.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-address="{ row }">{{ row.address }}</template>
    <template #cell-owner="{ row }">{{ accountName(row) }}</template>
    <template #cell-kind="{ row }"><UiBadge>{{ row.kindName }}</UiBadge></template>
    <template #cell-targets="{ row }">{{ row.targets.join(', ') }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('mail.aliases.rowActions', { name: row.address })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem @select="emit('act', row, 'edit')">
          {{ t('mail.aliases.rowEdit') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('act', row, 'remove')">
          {{ t('mail.aliases.rowRemove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
