<script setup lang="ts">
/**
 * A module's published versions as a table: version, publication date, size, the panel versions it
 * runs on, whether it can be installed and, per version, the action. The release notes are plain
 * text printed with their line breaks kept; they are never rendered as markup.
 *
 * Whether a version may be installed is the panel's `installable` and `notInstallableMessage`; the
 * list prints the reason as text and offers the per-version button only when the panel allows it
 * (and not while an operation runs or the catalogue is unverified). A withdrawn version carries its
 * reason in the panel's words.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatDate } from '../../utils/formatDate'
import type { UiTableColumn } from '../../types/tableColumn'
import type { MarketplaceVersion } from '../../types/marketplace'

/** Props accepted by {@link ModuleVersionList}. */
defineProps<{
  /** Every published version, newest first. */
  versions: readonly MarketplaceVersion[]
  /** True while an operation on the module runs or the catalogue is unverified; every button is then off. */
  disabled: boolean
}>()

/** Events emitted by {@link ModuleVersionList}. */
const emit = defineEmits<{
  /** The administrator chose to install this version. */
  (e: 'install', version: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's schema. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'version', label: t('marketplace.versions.version'), kind: 'identifier' },
    { key: 'publishedAt', label: t('marketplace.versions.publishedAt'), kind: 'date' },
    { key: 'size', label: t('marketplace.versions.size'), kind: 'number', align: 'end' },
    { key: 'panel', label: t('marketplace.manifest.minPanel'), kind: 'text', minWidth: '11rem' },
    { key: 'status', label: t('marketplace.versions.status'), kind: 'text', minWidth: '12rem' },
    { key: 'notes', label: t('marketplace.versions.notes'), kind: 'text', minWidth: '14rem' },
    { key: 'actions', label: t('common.actions'), kind: 'actions', align: 'end' },
  ]
})
</script>

<template>
  <UiTable :caption="t('marketplace.versions.tableCaption')" :columns="columns" :rows="versions" row-key="version">
    <template #cell-version="{ row }">{{ row.version }}</template>
    <template #cell-publishedAt="{ row }">{{ formatDate(row.publishedAt, localeStore.current) }}</template>
    <template #cell-size="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
    <template #cell-panel="{ row }">
      <p>{{ t('marketplace.versions.minPanel') }} {{ row.minPanelVersion }}</p>
      <p>{{ t('marketplace.versions.maxPanel') }} {{ row.maxPanelVersion ?? t('marketplace.versions.noMax') }}</p>
    </template>
    <template #cell-status="{ row }">
      <UiBadge v-if="row.revoked" variant="warning">{{ t('marketplace.versions.revoked') }}</UiBadge>
      <UiBadge v-else-if="row.installable" variant="success">{{ t('marketplace.versions.installable') }}</UiBadge>
      <p v-if="row.revokedMessage !== null" class="mt-1">{{ row.revokedMessage }}</p>
      <p v-if="row.notInstallableMessage !== null" class="mt-1">{{ row.notInstallableMessage }}</p>
    </template>
    <template #cell-notes="{ row }">
      <p class="whitespace-pre-line">{{ row.releaseNotes === '' ? t('marketplace.versions.noNotes') : row.releaseNotes }}</p>
    </template>
    <template #cell-actions="{ row }">
      <UiButton
        v-if="row.installable"
        variant="secondary"
        :disabled="disabled"
        :aria-label="t('marketplace.versions.installThisFor', { version: row.version })"
        @click="emit('install', row.version)"
      >
        {{ t('marketplace.versions.installThis') }}
      </UiButton>
    </template>
  </UiTable>
</template>
