<script setup lang="ts">
/**
 * One page of sites as a table: the domain (the only link in a row, to the site's detail page,
 * where every action a site has lives), the owner for an administrator, the backend, the PHP
 * version, the serving state and when it was created. Props in, events out: the page owns the
 * paging and the sort, this component only draws the rows it is given.
 *
 * The owner is named by the panel (`ownerName` on each row), never joined from an account list the
 * SPA happens to hold. A non-PHP site's empty PHP version is shown as an absence, not a blank cell.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiIconTile from '../ui/UiIconTile.vue'
import UiTable from '../ui/UiTable.vue'
import SiteStatusBadge from './SiteStatusBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { Site } from '../../types/site'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link SitesTable}. */
const props = defineProps<{
  /** The page of sites to draw. */
  rows: Site[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
  /** Whether to draw the owner column; a customer's sites are all their own. */
  showOwner: boolean
}>()

/** Events emitted by {@link SitesTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  const owner: UiTableColumn[] = props.showOwner ? [{ key: 'owner', label: t('sites.list.columns.owner'), kind: 'mono' }] : []
  return [
    { key: 'domain', label: t('sites.list.columns.domain'), kind: 'identity', sortField: 'domain', minWidth: '12rem' },
    ...owner,
    { key: 'backendType', label: t('sites.list.columns.backendType'), kind: 'text' },
    { key: 'phpVersion', label: t('sites.list.columns.phpVersion'), kind: 'mono' },
    { key: 'status', label: t('sites.list.columns.status'), kind: 'badge', sortField: 'status' },
    { key: 'createdAt', label: t('sites.list.columns.createdAt'), kind: 'date', align: 'end', sortField: 'createdAt' },
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
    :caption="t('sites.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-domain="{ row }">
      <div class="flex items-center gap-3">
        <UiIconTile name="earth" />
        <!-- The way in: everything that can be done to a site lives on its detail page. -->
        <RouterLink
          :to="{ name: 'site-detail', params: { id: row.id } }"
          class="inline-flex min-h-6 items-center rounded-control font-mono font-semibold text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ row.domain }}</RouterLink
        >
      </div>
    </template>
    <template #cell-owner="{ row }">{{ row.ownerName }}</template>
    <template #cell-backendType="{ row }">{{ t(`sites.backendType.${row.backendType}`) }}</template>
    <template #cell-phpVersion="{ row }">
      {{ row.phpVersion.length > 0 ? row.phpVersion : t('common.emptyValue') }}
    </template>
    <template #cell-status="{ row }"><SiteStatusBadge :status="row.status" /></template>
    <template #cell-createdAt="{ row }">{{ formatDate(row.createdAt, localeStore.current) }}</template>
  </UiTable>
</template>
