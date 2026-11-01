<script setup lang="ts">
/**
 * One server page of an entry's runs started by hand: when each started, how it ended, its exit code
 * and how long it took, with a button that opens what it printed. Sorted by the server; the two
 * sortable columns hand their keys up.
 *
 * Dumb by design (rules/vue.md): rows in, `sort` and `view` out.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiTable from '../ui/UiTable.vue'
import CronRunOutcomeBadge from './CronRunOutcomeBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { formatSeconds } from '../../utils/formatSeconds'
import type { UiTableColumn } from '../../types/tableColumn'
import type { CronRunSummary } from '../../types/cronRun'

/** Props accepted by {@link CronRunsTable}. */
defineProps<{
  /** The rows of the page the panel answered. */
  rows: CronRunSummary[]
  /** Whether a page is on its way, which draws placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link CronRunsTable}. */
const emit = defineEmits<{
  /** A sortable header was clicked, carrying its sort key. */
  (e: 'sort', field: string): void
  /** A row's output was asked for. */
  (e: 'view', run: CronRunSummary): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns; the sort keys are the server's (`startedAt`, `duration`). */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'startedAt', label: t('cron.runs.columns.startedAt'), kind: 'date', sortField: 'startedAt' },
    { key: 'outcome', label: t('cron.runs.columns.outcome'), kind: 'badge' },
    { key: 'exitCode', label: t('cron.output.exitCode'), kind: 'number' },
    { key: 'duration', label: t('cron.runs.columns.duration'), kind: 'number', sortField: 'duration' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})
</script>

<template>
  <UiTable
    :caption="t('cron.runs.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-startedAt="{ row }">{{ formatIsoTimestamp(row.startedAt, localeStore.current) }}</template>
    <template #cell-outcome="{ row }"><CronRunOutcomeBadge :outcome="row.outcome" /></template>
    <template #cell-exitCode="{ row }">{{ row.exitCode ?? t('common.emptyValue') }}</template>
    <template #cell-duration="{ row }">{{ formatSeconds(row.durationMilliseconds, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <UiButton variant="secondary" @click="emit('view', row)">{{ t('cron.runs.view') }}</UiButton>
    </template>
  </UiTable>
</template>
