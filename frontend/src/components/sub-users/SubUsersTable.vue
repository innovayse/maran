<script setup lang="ts">
/**
 * One page of the account's sub-users as a table: the login, the address, whether they have accepted
 * the invitation, the features they hold, their last sign-in, and each row's actions menu. Dumb by the
 * usual contract — the page owns the paging, the sort state and the dialogs; this renders rows and
 * reports clicks.
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
import type { UiTableColumn } from '../../types/tableColumn'
import type { GrantableFeature, SubUser, SubUserAction } from '../../types/subUser'

/** Props accepted by {@link SubUsersTable}. */
const props = defineProps<{
  /** The rows of the page the panel answered. */
  rows: SubUser[]
  /** The features the panel offers, so a row can name the ones it holds. */
  features: GrantableFeature[]
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link SubUsersTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's menu asked for an action. */
  (e: 'act', row: SubUser, action: SubUserAction): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'username', label: t('subUsers.columns.username'), kind: 'identifier', sortField: 'username' },
    { key: 'email', label: t('subUsers.columns.email'), kind: 'text', sortField: 'email' },
    { key: 'state', label: t('subUsers.columns.state'), kind: 'badge' },
    { key: 'features', label: t('subUsers.columns.features'), kind: 'text', minWidth: '14rem' },
    { key: 'lastLoginAt', label: t('subUsers.columns.lastLogin'), kind: 'date' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Names the features a row holds, in the panel's labels.
 * @param row The sub-user.
 * @returns The labels joined for reading, or the "none" text when the person holds no gated feature.
 */
const featureNames = (row: SubUser): string => {
  const names = row.features.map((id) => {
    return props.features.find((feature) => {
      return feature.feature === id
    })?.featureDisplayName ?? id
  })
  return names.length === 0 ? t('subUsers.noFeatures') : names.join(', ')
}
</script>

<template>
  <UiTable
    :caption="t('subUsers.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-username="{ row }">{{ row.username }}</template>
    <template #cell-email="{ row }">{{ row.email }}</template>
    <template #cell-state="{ row }">
      <UiBadge :variant="row.state === 'active' ? 'success' : 'warning'">
        {{ t(`subUsers.states.${row.state}`) }}
      </UiBadge>
    </template>
    <template #cell-features="{ row }">{{ featureNames(row) }}</template>
    <template #cell-lastLoginAt="{ row }">
      {{ row.lastLoginAt === null ? t('subUsers.neverSignedIn') : formatDate(row.lastLoginAt, localeStore.current) }}
    </template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('subUsers.rowActions', { name: row.username })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger><UiIcon name="ellipsis" size="md" /></template>
        <UiDropdownItem @select="emit('act', row, 'features')">
          {{ t('subUsers.changeFeatures') }}
        </UiDropdownItem>
        <UiDropdownItem destructive @select="emit('act', row, 'remove')">
          {{ t('subUsers.remove.action') }}
        </UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
