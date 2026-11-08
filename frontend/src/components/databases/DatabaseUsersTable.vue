<script setup lang="ts">
/**
 * One page of database users as a table: the prefixed login, the owning account, what kind of user
 * it is, how many databases it holds privileges on, when it was created, and a row menu. Props in,
 * events out: the page owns the paging, the confirmation and every request.
 *
 * **A database's dedicated user has no Drop in its menu.** It lives and dies with its database; the
 * backend refuses a lone drop with a 409 regardless, so omitting the item is presentation.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { DatabaseUser, DatabaseUserAction } from '../../types/databaseUser'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DatabaseUsersTable}. */
defineProps<{
  /** The rows of the page on screen, in the server's order. */
  rows: DatabaseUser[]
  /** Whether a page is being fetched, so the rows are drawn as placeholders. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link DatabaseUsersTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** A confirmable action was chosen on a row. */
  (e: 'act', row: DatabaseUser, action: DatabaseUserAction): void
  /** The privileges editor was chosen on a row. */
  (e: 'editPrivileges', row: DatabaseUser): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: header, cells and loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'fullName', label: t('databases.users.list.columns.fullName'), kind: 'identifier', sortField: 'name' },
    { key: 'account', label: t('databases.users.list.columns.account'), kind: 'text' },
    { key: 'kind', label: t('databases.users.list.columns.kind'), kind: 'badge' },
    { key: 'databases', label: t('databases.users.list.columns.databases'), kind: 'number' },
    { key: 'createdAt', label: t('databases.users.list.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account a user belongs to, as the panel named it on the row.
 * @param user The user row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (user: DatabaseUser): string => {
  return user.ownerName === '' ? t('common.emptyValue') : user.ownerName
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
    :caption="t('databases.users.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-fullName="{ row }">{{ row.fullName }}</template>
    <template #cell-account="{ row }">{{ accountName(row) }}</template>
    <template #cell-kind="{ row }">
      <UiBadge :variant="row.isDedicated ? 'info' : 'neutral'">
        {{ row.isDedicated ? t('databases.users.list.kindDedicated') : t('databases.users.list.kindStandalone') }}
      </UiBadge>
    </template>
    <template #cell-databases="{ row }">{{ row.grants.length }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('databases.users.list.rowActions', { name: row.fullName })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem @select="emit('editPrivileges', row)">
            {{ t('databases.users.list.editPrivileges') }}
          </UiDropdownItem>
          <UiDropdownItem @select="emit('act', row, 'resetPassword')">
            {{ t('databases.users.list.resetPassword') }}
          </UiDropdownItem>
          <UiDropdownItem v-if="!row.isDedicated" destructive @select="emit('act', row, 'drop')">
            {{ t('databases.users.list.drop') }}
          </UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
