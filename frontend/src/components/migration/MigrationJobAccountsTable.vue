<script setup lang="ts">
/**
 * One page of a job's accounts as a table: the account and its primary domain, the plan mapping, the state with
 * the step it is at, and a row menu with what the state allows (map, cut over, retry, cancel, the report, the
 * credentials). Props in, events out: the page owns the store and the dialogs.
 *
 * What a row offers is advice for the screen: the backend refuses an action the state does not allow.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import MigrationStateBadge from './MigrationStateBadge.vue'
import type { MigrationAccountAction, MigrationJobAccount } from '../../types/migrationJob'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link MigrationJobAccountsTable}. */
defineProps<{
  /** The page of accounts to draw. */
  rows: MigrationJobAccount[]
  /** Whether the page is being read. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The list's sort key. */
  sort: string
  /** Whether the job is a draft, the only state in which the mapping is edited. */
  editable: boolean
}>()

/** Events emitted by {@link MigrationJobAccountsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed. */
  (e: 'sort', field: string): void
  /** Map was chosen on a row. */
  (e: 'map', row: MigrationJobAccount): void
  /** A control was chosen on a row. */
  (e: 'control', row: MigrationJobAccount, action: MigrationAccountAction): void
  /** The report was chosen on a row. */
  (e: 'report', row: MigrationJobAccount): void
  /** The credentials were chosen on a row. */
  (e: 'secrets', row: MigrationJobAccount): void
}>()

const { t } = useI18n()

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('migrationJobs.accounts.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'domain', label: t('migrationJobs.accounts.columns.domain'), kind: 'mono' },
    { key: 'size', label: t('migrationJobs.accounts.columns.size'), kind: 'number', sortField: 'size' },
    { key: 'state', label: t('migrationJobs.accounts.columns.state'), kind: 'badge', sortField: 'state' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Whether a state is one an account can be cancelled from.
 * @param row The account.
 * @returns True unless the account has ended.
 */
const canCancel = (row: MigrationJobAccount): boolean => {
  return !['Completed', 'Cancelled', 'RolledBack', 'RolledBackIncomplete'].includes(row.state)
}

/**
 * Forwards a sortable heading's press.
 * @param field The column's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('migrationJobs.accounts.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-name="{ row }">
      <span class="font-semibold">{{ row.sourceAccountName }}</span>
      <span v-if="row.stage" class="ms-2 text-xs text-text-muted">{{ t(`migrationJobs.stages.${row.stage}`, row.stage) }}</span>
    </template>
    <template #cell-domain="{ row }">{{ row.primaryDomain ?? t('common.emptyValue') }}</template>
    <template #cell-size="{ row }">{{ row.sourceDiskBytes ?? t('common.emptyValue') }}</template>
    <template #cell-state="{ row }">
      <div class="flex flex-col gap-1">
        <MigrationStateBadge :state="row.state" :name="row.stateName" />
        <span v-if="row.failureName" class="text-xs text-danger-text">{{ row.failureName }}</span>
      </div>
    </template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          :label="t('common.actions')"
          :aria-label="t('migrationJobs.accounts.rowActions', { name: row.sourceAccountName })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem v-if="editable" @select="emit('map', row)">{{ t('migrationJobs.accounts.map') }}</UiDropdownItem>
          <UiDropdownItem v-if="row.state === 'Synced'" @select="emit('control', row, 'cutOver')">{{ t('migrationJobs.accounts.cutOver') }}</UiDropdownItem>
          <UiDropdownItem v-if="row.state === 'RolledBack' || row.state === 'Failed'" @select="emit('control', row, 'retry')">{{ t('migrationJobs.accounts.retry') }}</UiDropdownItem>
          <UiDropdownItem v-if="row.state === 'Completed'" @select="emit('report', row)">{{ t('migrationJobs.accounts.report') }}</UiDropdownItem>
          <UiDropdownItem v-if="row.hasIssuedSecrets" @select="emit('secrets', row)">{{ t('migrationJobs.accounts.secrets') }}</UiDropdownItem>
          <UiDropdownItem v-if="canCancel(row) && !editable" destructive @select="emit('control', row, 'cancel')">{{ t('migrationJobs.accounts.cancel') }}</UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
