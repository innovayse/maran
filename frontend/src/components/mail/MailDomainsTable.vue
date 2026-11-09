<script setup lang="ts">
/**
 * One page of mail domains as a table: the domain, the owner (for a reader who does not own every
 * row), its state, how many mailboxes it holds, the creation date, and each row's actions menu.
 * Dumb by the usual contract — the page owns the paging, the sort state and the confirmation; this
 * renders rows and reports clicks.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import MailDomainStatusBadge from './MailDomainStatusBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { UiTableColumn } from '../../types/tableColumn'
import type { MailDomain, MailDomainAction } from '../../types/mailDomain'

/** Props accepted by {@link MailDomainsTable}. */
const props = defineProps<{
  /** The rows of the page the panel answered. */
  rows: MailDomain[]
  /** Whether to draw the owner column; a customer owns every row. */
  showOwner: boolean
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link MailDomainsTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked for an action that needs confirmation. */
  (e: 'act', row: MailDomain, action: MailDomainAction): void
  /** A row's menu asked to open that domain's DNS records. */
  (e: 'records', row: MailDomain): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * The table's columns: its header, its cells and its loading placeholder all read this one list.
 * The owner column is drawn only when the page says the reader is not the owner of every row.
 */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner
    ? [{ key: 'owner', label: t('mail.domains.columns.account'), kind: 'mono' }]
    : []
  return [
    { key: 'name', label: t('mail.domains.columns.name'), kind: 'identifier', sortField: 'name' },
    ...owner,
    { key: 'status', label: t('mail.domains.columns.status'), kind: 'badge' },
    { key: 'mailboxes', label: t('mail.domains.columns.mailboxes'), kind: 'number' },
    { key: 'createdAt', label: t('mail.domains.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the account a domain belongs to, as the panel named it on the row.
 * @param domain The domain row.
 * @returns The owner's short name, or the empty-value mark when the panel did not name it.
 */
const accountName = (domain: MailDomain): string => {
  return domain.ownerName === '' ? t('common.emptyValue') : domain.ownerName
}
</script>

<template>
  <UiTable
    :caption="t('mail.domains.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-name="{ row }">{{ row.name }}</template>
    <template #cell-owner="{ row }">{{ accountName(row) }}</template>
    <template #cell-status="{ row }">
      <MailDomainStatusBadge :status="row.status" :label="row.statusName" />
    </template>
    <template #cell-mailboxes="{ row }">{{ row.mailboxCount }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('mail.domains.rowActions', { name: row.name })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem @select="emit('records', row)">
          {{ t('mail.domains.dnsRecords') }}
        </UiDropdownItem>
        <UiDropdownItem @select="emit('act', row, 'rotateDkim')">
          {{ t('mail.domains.rotateDkim') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('act', row, 'remove')">
          {{ t('mail.domains.remove') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
