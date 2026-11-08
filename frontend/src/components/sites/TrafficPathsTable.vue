<script setup lang="ts">
/**
 * The most requested paths over the window, most frequent first.
 *
 * A path is text a visitor chose, so it reaches the DOM through interpolation only — never
 * `v-html` (rules/vue.md: the panel renders customer-supplied strings).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import type { UiTableColumn } from '../../types/tableColumn'
import type { TrafficPath } from '../../types/siteTraffic'

/** Props accepted by {@link TrafficPathsTable}. */
defineProps<{
  /** The paths to list. */
  rows: TrafficPath[]
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'path', label: t('siteTraffic.paths.columns.path'), kind: 'code', minWidth: '12rem' },
    { key: 'requests', label: t('siteTraffic.paths.columns.requests'), kind: 'number' },
  ]
})
</script>

<template>
  <UiTable :caption="t('siteTraffic.paths.caption')" :columns="columns" :rows="rows" row-key="path">
    <template #cell-path="{ row }">{{ row.path }}</template>
    <template #cell-requests="{ row }">{{ row.requests.toLocaleString(localeStore.current) }}</template>
  </UiTable>
</template>
