<script setup lang="ts">
/**
 * One page of databases as a table: the prefixed name, the database user, the owning account, when
 * it was created, and a row menu. Props in, events out: the page owns the paging, the confirmation
 * and every request.
 *
 * **The name column is the prefixed one**, because it is the name that works in a mysql client; the
 * value is the server's, never assembled here.
 *
 * The table scrolls inside its own container at 390px (the specs find it as `table.parentElement`).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { Database, DatabaseAction } from '../../types/database'
import type { UiTableColumn } from '../../types/tableColumn'


/** Props accepted by {@link DatabasesTable}. */
defineProps<{
  /** The rows of the page on screen, in the server's order. */
  rows: Database[]
  /** Whether a page is being fetched, so the rows are drawn as placeholders. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link DatabasesTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** An action was chosen on a row; carries the row and which action. */
  (e: 'act', row: Database, action: DatabaseAction): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The name column sorts by the server's `name` key, which orders by the full name this column shows.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'fullName', label: t('databases.list.columns.fullName'), kind: 'identifier', sortField: 'name' },
    { key: 'dbUserName', label: t('databases.list.columns.dbUserName'), kind: 'mono' },
    { key: 'account', label: t('databases.list.columns.account'), kind: 'text' },
    { key: 'createdAt', label: t('databases.list.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account a database belongs to, as the panel named it on the row.
 * @param database The database row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (database: Database): string => {
  return database.ownerName === '' ? t('common.emptyValue') : database.ownerName
}

/**
 * Forwards a sortable heading's press.
 * @param field The server's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('databases.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-fullName="{ row }">{{ row.fullName }}</template>
    <template #cell-dbUserName="{ row }">{{ row.dbUserName }}</template>
    <template #cell-account="{ row }">{{ accountName(row) }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <!-- One trigger instead of a button per command: the row's actions grow with the module, and
           a row that grows a button every time stops fitting on a narrow screen. `align="end"`
           because this is the last column — a menu aligned to the start would open off the right
           edge. -->
      <div class="flex items-center justify-end">
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('databases.list.rowActions', { name: row.fullName })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem @select="emit('act', row, 'resetPassword')">
            {{ t('databases.list.resetPassword') }}
          </UiDropdownItem>
          <UiDropdownItem destructive @select="emit('act', row, 'drop')">
            {{ t('databases.list.drop') }}
          </UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
