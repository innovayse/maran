<script setup lang="ts">
/**
 * One page of the operating-system packages that have an update available. The page around it owns
 * the paging (`useServerTable`); this component draws the rows it is given, the loading placeholder
 * and the sortable headings.
 *
 * Sortable by package and by source, the two fields the panel can order stably. The version
 * columns have no sort: version strings do not order as text, and the panel offers no such key.
 *
 * **An installed version of `null` is "not reported", drawn as a dash.** The RHEL family's update
 * check lists only the new version, so the installed one is genuinely unknown there; printing an
 * empty cell, or the available version again, would claim a finding nobody made. The source may be
 * an empty string for the same reason and is drawn the same way.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import type { PendingUpdate } from '../../types/monitoring'

/** What a cell prints for a value the package manager did not report. */
const NOT_REPORTED = '—'

/** Props accepted by {@link PendingUpdatesTable}. */
defineProps<{
  /** The rows of the page the panel answered with, in its own order. */
  rows: PendingUpdate[]
  /** Whether the page is still on its way: the real header over placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading — the page size the rows will fill. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the panel's default. */
  sort: string
}>()

/** Events emitted by {@link PendingUpdatesTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the sort key the panel whitelists. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()

/** The table's columns: its header and its cells read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'package', label: t('monitoring.updates.columnPackage'), kind: 'identifier', sortField: 'packageName' },
    { key: 'installed', label: t('monitoring.updates.columnInstalled'), kind: 'mono' },
    { key: 'available', label: t('monitoring.updates.columnAvailable'), kind: 'mono' },
    { key: 'source', label: t('monitoring.updates.columnSource'), kind: 'text', sortField: 'source' },
  ]
})

/**
 * A reported value, or the dash for one the package manager did not report.
 * @param value The value, `null` or an empty string when it was not reported.
 * @returns The value, or the dash.
 */
const reported = (value: string | null): string => {
  return value === null || value === '' ? NOT_REPORTED : value
}
</script>

<template>
  <UiTable
    :caption="t('monitoring.updates.caption')"
    :columns="columns"
    :rows="rows"
    row-key="packageName"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="emit('sort', $event)"
  >
    <template #cell-package="{ row }">{{ row.packageName }}</template>
    <template #cell-installed="{ row }">{{ reported(row.installedVersion) }}</template>
    <template #cell-available="{ row }">{{ row.availableVersion }}</template>
    <template #cell-source="{ row }">{{ reported(row.source) }}</template>
  </UiTable>
</template>
