<script setup lang="ts">
/**
 * The apps the host's package sources can install and that are not installed yet, with one install
 * button each. The set is closed and small (a web server or two, a handful of runtimes), so it is a
 * plain table without paging; the installed apps, which can be searched and filtered, are the
 * page's server-paged table. Each button is named for the app it acts on, so two rows' "Install"
 * never share an accessible name.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiTable from '../ui/UiTable.vue'
import type { InstallableHostApp } from '../../types/hostApp'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link AvailableAppsTable}. */
defineProps<{
  /** The apps that can be installed and are not yet. */
  apps: readonly InstallableHostApp[]
  /** Ids of the apps an install or removal is running for; their buttons are held. */
  busy: readonly string[]
}>()

/** Events emitted by {@link AvailableAppsTable}. */
defineEmits<{
  /** The administrator asked to install an app. */
  (e: 'install', id: string): void
}>()

const { t } = useI18n()

/** The table's columns. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('apps.list.availableName'), kind: 'identity' },
    { key: 'kind', label: t('apps.list.columns.kind'), kind: 'text' },
    { key: 'actions', label: t('apps.list.columns.actions'), kind: 'actions' },
  ]
})
</script>

<template>
  <UiEmptyState v-if="apps.length === 0" :title="t('apps.list.availableEmpty')" />
  <UiTable v-else :caption="t('apps.list.availableCaption')" :columns="columns" :rows="apps" row-key="appId">
    <template #cell-name="{ row }">{{ row.name }}</template>
    <template #cell-kind="{ row }">{{ row.kindName }}</template>
    <template #cell-actions="{ row }">
      <UiButton
        variant="secondary"
        :disabled="busy.includes(row.appId)"
        :aria-label="t('apps.list.installFor', { name: row.name })"
        @click="$emit('install', row.appId)"
      >
        {{ t('apps.list.install') }}
      </UiButton>
    </template>
  </UiTable>
</template>
