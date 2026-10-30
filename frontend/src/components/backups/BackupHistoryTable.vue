<script setup lang="ts">
/**
 * One page of the backup history as a table: the account, the status with both halves of a failure
 * (the backend's sentence and the machine-stable code), the reason, the size, the databases, when
 * the run started and ended, and a row menu. Props in, events out: the page owns the paging, the
 * dialogs and every request.
 *
 * **A row's commands live behind one menu, not a button each**, even where a row offers only
 * Delete: a running or failed row is offered no restore, and a last column that changed shape from
 * row to row would make an operator re-find the control on every line. The trigger's accessible
 * name carries the row's account.
 *
 * **Restore is offered on a completed backup and on no other.** A running or failed run produced no
 * artifact and the panel answers `BackupNotRestorable`, so a control there could only ever refuse.
 *
 * The table scrolls inside its own container at 390px (the specs find it as `table.parentElement`).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import BackupStatusBadge from './BackupStatusBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { backupAccountName } from '../../utils/backupAccountName'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { Backup } from '../../types/backup'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link BackupHistoryTable}. */
defineProps<{
  /** The rows of the page on screen, in the server's order. */
  rows: Backup[]
  /** Whether a page is being fetched, so the rows are drawn as placeholders. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link BackupHistoryTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Restore was chosen on a completed row. */
  (e: 'restore', backup: Backup): void
  /** Delete was chosen on a row; carries its id. */
  (e: 'delete', id: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'account', label: t('backups.list.columns.account'), kind: 'identifier' },
    { key: 'status', label: t('backups.list.columns.status'), kind: 'badge' },
    { key: 'kind', label: t('backups.list.columns.kind'), kind: 'text' },
    { key: 'size', label: t('backups.list.columns.size'), kind: 'number', sortField: 'size' },
    { key: 'databases', label: t('backups.list.columns.databases'), kind: 'number' },
    { key: 'startedAt', label: t('backups.list.columns.startedAt'), kind: 'date', sortField: 'startedAt' },
    { key: 'finishedAt', label: t('backups.list.columns.finishedAt'), kind: 'date' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * The account a row names.
 * @param backup The backup the row is drawn for.
 * @returns The account's name, or a placeholder.
 */
const accountName = (backup: Backup): string => {
  return backupAccountName(backup, t('common.emptyValue'))
}

/**
 * The size a row shows. A running or failed backup has no artifact, and its zero `sizeBytes`
 * formatted as "0 B" would read as an archive that exists and is empty.
 * @param backup The backup the row is drawn for.
 * @returns The formatted size, or a placeholder when there is no artifact.
 */
const size = (backup: Backup): string => {
  return backup.status === 'completed' ? formatBytes(backup.sizeBytes) : t('common.emptyValue')
}

/**
 * The sentence a failed row shows above its code: the backend's, already localized. `null` for a
 * row that did not fail, or a panel older than the field, which sends the code alone.
 * @param backup The backup the row is drawn for.
 * @returns The localized failure name, or `null` when the row has none.
 */
const failureName = (backup: Backup): string | null => {
  // Read through a `typeof`: the value comes off the wire, and a panel older than the field sends
  // the key not at all. A screen that threw on the row it exists to explain would be worse.
  const name: unknown = backup.failureDisplayName
  return typeof name === 'string' && name.length > 0 ? name : null
}

/**
 * When the run ended, or a placeholder while it is still going.
 * @param backup The backup the row is drawn for.
 * @returns The formatted instant, or a placeholder when the run has not ended.
 */
const finished = (backup: Backup): string => {
  return backup.finishedAt === null
    ? t('common.emptyValue')
    : formatIsoTimestamp(backup.finishedAt, localeStore.current)
}

/**
 * Passes a heading's sort up.
 * @param field The server's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('backups.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <!-- The cell holds the account's name and nothing else: a spec reads it as the cell's whole text. -->
    <template #cell-account="{ row }">{{ accountName(row) }}</template>
    <template #cell-status="{ row }">
      <BackupStatusBadge :status="row.status" />
      <!-- Both halves of the failure, and neither instead of the other: the sentence is what a
           reader reads, the code is what a support ticket quotes — visible, selectable text. -->
      <span
        v-if="failureName(row) !== null"
        class="mt-1.5 block max-w-64 text-sm whitespace-normal text-text-secondary wrap-anywhere"
      >
        {{ failureName(row) }}
      </span>
      <span v-if="row.failureCode.length > 0" class="mt-0.5 block font-mono text-xs text-text-muted">
        {{ t('backups.list.failureCode', { code: row.failureCode }) }}
      </span>
    </template>
    <template #cell-kind="{ row }">{{ t(`backups.kind.${row.kind}`) }}</template>
    <template #cell-size="{ row }">{{ size(row) }}</template>
    <template #cell-databases="{ row }">{{ row.databaseCount }}</template>
    <template #cell-startedAt="{ row }">{{ formatIsoTimestamp(row.startedAt, localeStore.current) }}</template>
    <template #cell-finishedAt="{ row }">{{ finished(row) }}</template>
    <template #cell-actions="{ row }">
      <!-- `align="end"` because this is the last column: a start-aligned menu would open off the edge. -->
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('backups.list.rowActions', { account: accountName(row) })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger>
          <UiIcon name="ellipsis" size="md" />
        </template>
        <UiDropdownItem v-if="row.status === 'completed'" @select="emit('restore', row)">
          {{ t('backups.restore.action') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('delete', row.id)">
          {{ t('common.delete') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
