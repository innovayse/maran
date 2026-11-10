<script setup lang="ts">
/**
 * One page of migration jobs as a table: the label (the only link in a row, to the job's page), the state,
 * how many accounts it holds and how many are done, and when it was created. Props in, events out: the page owns
 * the paging and the sort.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import MigrationStateBadge from './MigrationStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { MigrationJob } from '../../types/migrationJob'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link MigrationJobsTable}. */
defineProps<{
  /** The page of jobs to draw. */
  rows: MigrationJob[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link MigrationJobsTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('migrationJobs.list.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'state', label: t('migrationJobs.list.columns.state'), kind: 'badge', sortField: 'state' },
    { key: 'accounts', label: t('migrationJobs.list.columns.accounts'), kind: 'number' },
    { key: 'done', label: t('migrationJobs.list.columns.done'), kind: 'number' },
    { key: 'created', label: t('migrationJobs.list.columns.created'), kind: 'date', sortField: 'createdAt' },
  ]
})

/**
 * Forwards a sortable heading's press.
 * @param field The column's sort field.
 * @returns Nothing; the event is emitted synchronously.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('migrationJobs.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-name="{ row }">
      <div class="flex items-center gap-3">
        <UiIconTile name="server" />
        <RouterLink
          :to="{ name: 'migration-job-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.name }}</RouterLink
        >
      </div>
    </template>
    <template #cell-state="{ row }"><MigrationStateBadge :state="row.state" :name="row.stateName" /></template>
    <template #cell-accounts="{ row }">{{ row.accountCount }}</template>
    <template #cell-done="{ row }">{{ row.accountStates.Completed ?? 0 }}</template>
    <template #cell-created="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
  </UiTable>
</template>
