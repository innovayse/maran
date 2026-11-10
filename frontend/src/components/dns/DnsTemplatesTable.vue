<script setup lang="ts">
/**
 * One page of zone templates as a table: the name (the only link in a row, to the template's edit
 * screen) with a badge for a built-in and for the default template, the description, the record
 * count, when it last changed, and a row menu. Props in, events out: the page owns the paging, the
 * sort and the confirmation.
 *
 * Which actions a row offers follows what the panel stated about it: a default template is not
 * offered "make default" again, and a built-in or default one is not offered delete, so the
 * default's row has no menu at all. That is advice; the panel refuses a delete of either
 * regardless.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiBadge from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { DnsTemplate } from '../../types/dnsTemplate'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link DnsTemplatesTable}. */
defineProps<{
  /** The page of templates to draw. */
  rows: DnsTemplate[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
}>()

/** Events emitted by {@link DnsTemplatesTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** Make default was chosen on a row; carries the template. */
  (e: 'makeDefault', row: DnsTemplate): void
  /** Delete was chosen on a row; carries the template. */
  (e: 'remove', row: DnsTemplate): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('dns.templates.columns.name'), kind: 'identity', sortField: 'name', minWidth: '12rem' },
    { key: 'description', label: t('dns.templates.columns.description'), kind: 'text', minWidth: '14rem' },
    { key: 'recordCount', label: t('dns.templates.columns.records'), kind: 'number' },
    { key: 'updatedAt', label: t('dns.templates.columns.updatedAt'), kind: 'date', sortField: 'updatedAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
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
    :caption="t('dns.templates.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-name="{ row }">
      <div class="flex flex-wrap items-center gap-3">
        <UiIconTile name="fileText" />
        <RouterLink
          :to="{ name: 'dns-template-edit', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.name }}</RouterLink
        >
        <UiBadge v-if="row.isDefault" variant="success">{{ t('dns.templates.badges.default') }}</UiBadge>
        <UiBadge v-if="row.isBuiltIn" variant="neutral">{{ t('dns.templates.badges.builtIn') }}</UiBadge>
      </div>
    </template>
    <template #cell-description="{ row }">{{ row.description }}</template>
    <template #cell-recordCount="{ row }">{{ row.recordCount }}</template>
    <template #cell-updatedAt="{ row }">{{ formatDate(row.updatedAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end">
        <UiDropdown
          v-if="!row.isDefault"
          :label="t('common.actions')"
          :aria-label="t('dns.templates.rowActions', { name: row.name })"
          align="end"
          variant="bare"
          :chevron="false"
        >
          <template #trigger>
            <UiIcon name="ellipsis" size="md" />
          </template>
          <UiDropdownItem v-if="!row.isDefault" @select="emit('makeDefault', row)">{{
            t('dns.templates.makeDefault')
          }}</UiDropdownItem>
          <UiDropdownItem v-if="!row.isBuiltIn && !row.isDefault" destructive @select="emit('remove', row)">{{
            t('common.delete')
          }}</UiDropdownItem>
        </UiDropdown>
      </div>
    </template>
  </UiTable>
</template>
