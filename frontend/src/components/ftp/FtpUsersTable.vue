<script setup lang="ts">
/**
 * One page of FTPS logins as a table: the prefixed login, the owner (for a reader who does not own
 * every row), the creation date, and each row's actions menu. Dumb by the usual contract — the page
 * owns the paging, the sort state and the confirmation; this renders rows and reports clicks.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { UiTableColumn } from '../../types/tableColumn'
import type { FtpUser, FtpUserAction } from '../../types/ftpUser'


/** Props accepted by {@link FtpUsersTable}. */
const props = defineProps<{
  /** The rows of the page the panel answered. */
  rows: FtpUser[]
  /** Whether to draw the owner column; a customer owns every row. */
  showOwner: boolean
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link FtpUsersTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked for an action. */
  (e: 'act', row: FtpUser, action: FtpUserAction): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The owner column is drawn only when the page says the reader is not the owner of every row.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner
    ? [{ key: 'owner', label: t('ftp.list.columns.account'), kind: 'mono' }]
    : []
  return [
    { key: 'fullName', label: t('ftp.list.columns.fullName'), kind: 'identifier', sortField: 'name' },
    ...owner,
    { key: 'createdAt', label: t('ftp.list.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account a login belongs to, as the panel named it on the row.
 * @param login The login row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (login: FtpUser): string => {
  return login.ownerName === '' ? t('common.emptyValue') : login.ownerName
}
</script>

<template>
  <UiTable
    :caption="t('ftp.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <!-- The prefixed login, because it is the one a client accepts. -->
    <template #cell-fullName="{ row }">{{ row.fullName }}</template>
    <template #cell-owner="{ row }">{{ accountName(row) }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('ftp.list.rowActions', { name: row.fullName })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem @select="emit('act', row, 'resetPassword')">
          {{ t('ftp.list.resetPassword') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('act', row, 'remove')">
          {{ t('ftp.list.remove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
