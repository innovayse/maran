<script setup lang="ts">
/**
 * One page of the host's mail queue as a table: the message's id, its sender, the queue it waits
 * in, how many recipients it has, its size, when it arrived, why it waits, and each row's actions
 * menu. Dumb by the usual contract — the page owns the paging, the sort state and the dialogs; this
 * renders rows and reports clicks.
 *
 * **The sender is untrusted text.** It came from whoever sent the message, so it is interpolated as
 * text (Vue escapes it) and never rendered as markup, a link or a mail address.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { UiTableColumn } from '../../types/tableColumn'
import type { MailQueueEntry } from '../../types/mailQueue'

/** Props accepted by {@link MailQueueTable}. */
defineProps<{
  /** The rows of the page the panel answered. */
  rows: MailQueueEntry[]
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link MailQueueTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked to remove its message. */
  (e: 'remove', row: MailQueueEntry): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'queueId', label: t('mail.queue.columns.message'), kind: 'code' },
    { key: 'sender', label: t('mail.queue.columns.sender'), kind: 'text', sortField: 'sender', minWidth: '14rem' },
    { key: 'queue', label: t('mail.queue.columns.queue'), kind: 'badge' },
    { key: 'recipients', label: t('mail.queue.columns.recipients'), kind: 'number', sortField: 'recipients' },
    { key: 'size', label: t('mail.queue.columns.size'), kind: 'number', sortField: 'size' },
    { key: 'arrivedAt', label: t('mail.queue.columns.arrivedAt'), kind: 'date', sortField: 'arrivedAt' },
    { key: 'reason', label: t('mail.queue.columns.reason'), kind: 'text' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})
</script>

<template>
  <UiTable
    :caption="t('mail.queue.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="queueId"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-queueId="{ row }">{{ row.queueId }}</template>
    <template #cell-sender="{ row }">{{ row.sender }}</template>
    <template #cell-queue="{ row }"><UiBadge>{{ row.queueDisplayName }}</UiBadge></template>
    <template #cell-recipients="{ row }">{{ row.recipientCount }}</template>
    <template #cell-size="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
    <template #cell-arrivedAt="{ row }">{{ formatIsoTimestamp(row.arrivedAt, localeStore.current) }}</template>
    <template #cell-reason="{ row }">{{ row.deferralDisplayName }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('mail.queue.rowActions', { id: row.queueId })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem destructive @select="emit('remove', row)">
          {{ t('mail.queue.rowRemove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
