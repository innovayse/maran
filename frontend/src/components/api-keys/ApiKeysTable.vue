<script setup lang="ts">
/**
 * One page of API keys as a table: the name, the public prefix, the scopes, the state, the last
 * use, the expiry, the creation date and each key's actions menu. Dumb by the usual contract: the
 * page owns paging, sort and the confirmation; this renders rows and reports clicks. Every name
 * printed for a scope or a state is the panel's, handed in as a function.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { ApiKey, ApiKeyStatus } from '../../types/apiKey'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link ApiKeysTable}. */
defineProps<{
  /** The rows of the page the panel answered. */
  rows: ApiKey[]
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
  /** Names a scope as the panel named it. */
  scopeName: (value: string) => string
  /** Names a state as the panel named it. */
  statusName: (value: string) => string
}>()

/** Events emitted by {@link ApiKeysTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked to revoke that key. */
  (e: 'revoke', row: ApiKey): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: header, cells and loading placeholders all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('apiKeys.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'prefix', label: t('apiKeys.columns.prefix'), kind: 'mono' },
    { key: 'scopes', label: t('apiKeys.columns.scopes'), kind: 'text', minWidth: '14rem' },
    { key: 'status', label: t('apiKeys.columns.status'), kind: 'badge' },
    { key: 'lastUsed', label: t('apiKeys.columns.lastUsed'), kind: 'text', sortField: 'lastUsedAt', minWidth: '12rem' },
    { key: 'expiresAt', label: t('apiKeys.columns.expiresAt'), kind: 'date' },
    { key: 'createdAt', label: t('apiKeys.columns.createdAt'), kind: 'date', sortField: 'createdAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Maps a key's state onto a badge tone. The tone is chrome; the word beside it is the panel's.
 * @param status The state as the panel derived it.
 * @returns The badge variant.
 */
const toneOf = (status: ApiKeyStatus): BadgeVariant => {
  switch (status) {
    case 'active':
      return 'success'
    case 'expired':
      return 'warning'
    default:
      return 'neutral'
  }
}

/**
 * Lists a key's scopes under the panel's names.
 * @param key The key row.
 * @param name Names one scope.
 * @returns The names joined for reading, or the empty-value mark when the key has none.
 */
const scopesText = (key: ApiKey, name: (value: string) => string): string => {
  return key.scopes.length === 0 ? t('common.emptyValue') : key.scopes.map(name).join(', ')
}

/**
 * Says when and from where a key was last used.
 * @param key The key row.
 * @returns The date and the address, or the "never used" text.
 */
const lastUseText = (key: ApiKey): string => {
  if (key.lastUsedAt === null) {
    return t('apiKeys.neverUsed')
  }
  const when = formatDate(key.lastUsedAt, localeStore.current)
  return key.lastUsedIp === null ? when : `${when} · ${key.lastUsedIp}`
}

/**
 * Says when a key stops working.
 * @param key The key row.
 * @returns The date, or the "never expires" text.
 */
const expiryText = (key: ApiKey): string => {
  return key.expiresAt === null ? t('apiKeys.noExpiry') : formatDate(key.expiresAt, localeStore.current)
}
</script>

<template>
  <UiTable
    :caption="t('apiKeys.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-name="{ row }">{{ row.name }}</template>
    <template #cell-prefix="{ row }">{{ row.prefix }}</template>
    <template #cell-scopes="{ row }">{{ scopesText(row, scopeName) }}</template>
    <template #cell-status="{ row }">
      <UiBadge :variant="toneOf(row.status)">{{ statusName(row.status) }}</UiBadge>
    </template>
    <template #cell-lastUsed="{ row }">{{ lastUseText(row) }}</template>
    <template #cell-expiresAt="{ row }">{{ expiryText(row) }}</template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <!-- A revoked key has nothing left to do, so its menu is not drawn. -->
      <UiDropdown
        v-if="row.status !== 'revoked'"
        :label="t('common.actions')"
        :aria-label="t('apiKeys.rowActions', { name: row.name })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem destructive @select="emit('revoke', row)">
          {{ t('apiKeys.revoke') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
