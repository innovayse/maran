<script setup lang="ts">
/**
 * The exact status codes a site answered with over the window, most frequent first. The panel
 * names no class here, only the code and its count; the day-by-day class split lives in the
 * summary's `perDay`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import type { UiTableColumn } from '../../types/tableColumn'
import type { TrafficStatusCode } from '../../types/siteTraffic'

/** Props accepted by {@link TrafficStatusTable}. */
defineProps<{
  /** The status codes to list. */
  rows: TrafficStatusCode[]
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'code', label: t('siteTraffic.status.columns.code'), kind: 'identifier' },
    { key: 'requests', label: t('siteTraffic.status.columns.requests'), kind: 'number' },
  ]
})
</script>

<template>
  <UiTable :caption="t('siteTraffic.status.caption')" :columns="columns" :rows="rows" row-key="code">
    <template #cell-code="{ row }">{{ row.code }}</template>
    <template #cell-requests="{ row }">{{ row.requests.toLocaleString(localeStore.current) }}</template>
  </UiTable>
</template>
