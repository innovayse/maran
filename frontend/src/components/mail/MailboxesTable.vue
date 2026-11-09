<script setup lang="ts">
/**
 * One page of mailboxes as a table: the address, the owner (for a reader who does not own every
 * row), its state, how much of its quota it uses, its hourly send limit, and each row's actions
 * menu. Dumb by the usual contract — the page owns the paging, the sort state and the dialogs; this
 * renders rows and reports clicks.
 *
 * The usage figure is the mail host's own report. When the host has not reported one the cell says
 * so with the quota alone, rather than drawing an empty bar that reads as "nothing used".
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiTable from '../ui/UiTable.vue'
import MailboxStatusBadge from './MailboxStatusBadge.vue'
import { formatMegabytes } from '../../utils/formatBytes'
import type { UiTableColumn } from '../../types/tableColumn'
import type { Mailbox, MailboxAction } from '../../types/mailbox'

/** Props accepted by {@link MailboxesTable}. */
const props = defineProps<{
  /** The rows of the page the panel answered. */
  rows: Mailbox[]
  /** Whether to draw the owner column; a customer owns every row. */
  showOwner: boolean
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link MailboxesTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked for a dialog. */
  (e: 'act', row: Mailbox, action: MailboxAction): void
}>()

const { t } = useI18n()

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The owner column is drawn only when the page says the reader is not the owner of every row.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner
    ? [{ key: 'owner', label: t('mail.mailboxes.columns.account'), kind: 'mono' }]
    : []
  return [
    { key: 'address', label: t('mail.mailboxes.columns.address'), kind: 'identifier', sortField: 'address' },
    ...owner,
    { key: 'status', label: t('mail.mailboxes.columns.status'), kind: 'badge' },
    { key: 'usage', label: t('mail.mailboxes.columns.usage'), kind: 'text', minWidth: '12rem' },
    { key: 'sendLimit', label: t('mail.mailboxes.columns.sendLimit'), kind: 'number' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account a mailbox belongs to, as the panel named it on the row.
 * @param mailbox The mailbox row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (mailbox: Mailbox): string => {
  return mailbox.ownerName === '' ? t('common.emptyValue') : mailbox.ownerName
}

/**
 * States how much of the quota a mailbox uses, in words, for the bar's accessible value.
 * @param mailbox The mailbox row.
 * @returns "120 MiB of 1 GiB", or the quota alone while no usage was reported.
 */
const usageText = (mailbox: Mailbox): string => {
  return mailbox.usedMib === null
    ? t('mail.mailboxes.quotaOnly', { quota: formatMegabytes(mailbox.quotaMib) })
    : t('mail.mailboxes.usage', { used: formatMegabytes(mailbox.usedMib), quota: formatMegabytes(mailbox.quotaMib) })
}
</script>

<template>
  <UiTable
    :caption="t('mail.mailboxes.tableCaption')"
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
    <template #cell-status="{ row }">
      <MailboxStatusBadge :status="row.status" :label="row.statusName" />
    </template>
    <template #cell-usage="{ row }">
      <div class="flex min-w-0 flex-col gap-1">
        <UiMeter
          v-if="row.usedMib !== null"
          :value="row.usedMib"
          :max="row.quotaMib"
          :label="t('mail.mailboxes.usageLabel', { address: row.address })"
          :value-text="usageText(row)"
          :show-value="false"
        />
        <span class="text-sm text-text-secondary">{{ usageText(row) }}</span>
      </div>
    </template>
    <template #cell-sendLimit="{ row }">{{ row.sendLimitPerHour }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('mail.mailboxes.rowActions', { name: row.address })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem @select="emit('act', row, 'edit')">
          {{ t('mail.mailboxes.rowEdit') }}
        </UiDropdownItem>
        <UiDropdownItem @select="emit('act', row, 'password')">
          {{ t('mail.mailboxes.rowChangePassword') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('act', row, 'remove')">
          {{ t('mail.mailboxes.rowRemove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
