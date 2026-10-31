<script setup lang="ts">
/**
 * One page of the background tasks as a table: the operation, what it acts on, its status, how far
 * it got, when it started, and a row menu whose one command opens the task's live pane. Props in,
 * events out: the page owns the paging, the pane and every request.
 *
 * **A row's commands live behind one menu**, even with a single command, so the last column has one
 * shape in every table of the panel. The trigger's accessible name carries the row's subject.
 *
 * The table scrolls inside its own container at 390px.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import TaskProgress from './TaskProgress.vue'
import TaskStatusBadge from './TaskStatusBadge.vue'
import { useTaskKindLabel } from '../../composables/useTaskKindLabel'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { PanelTask } from '../../types/panelTask'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link TaskTable}. */
defineProps<{
  /** The rows of the page on screen, in the server's order. */
  rows: PanelTask[]
  /** Whether a page is being fetched, so the rows are drawn as placeholders. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link TaskTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** The live view was chosen on a row; carries the task. */
  (e: 'open', task: PanelTask): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
const kindLabel = useTaskKindLabel()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'kind', label: t('tasks.columns.kind'), kind: 'identity' },
    { key: 'subject', label: t('tasks.columns.subject'), kind: 'mono', sortField: 'subject' },
    { key: 'status', label: t('tasks.columns.status'), kind: 'badge' },
    { key: 'percent', label: t('tasks.columns.percent'), kind: 'number' },
    { key: 'startedAt', label: t('tasks.columns.startedAt'), kind: 'date', sortField: 'startedAt' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Passes a heading's sort up.
 * @param field The server's sort field.
 * @returns Nothing.
 */
const sortBy = (field: string): void => {
  emit('sort', field)
}
</script>

<template>
  <UiTable
    :caption="t('tasks.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <template #cell-kind="{ row }">{{ kindLabel(row.kind) }}</template>
    <!-- Machine text as often as not — a domain, a system user name — so monospaced and never
         wrapped: an identifier read across six lines is a different string. -->
    <template #cell-subject="{ row }">{{ row.subject }}</template>
    <template #cell-status="{ row }"><TaskStatusBadge :status="row.status" /></template>
    <!-- The figure is the cell's text and the bar decoration; a failed task keeps the figure only. -->
    <template #cell-percent="{ row }"><TaskProgress :status="row.status" :percent="row.percent" size="row" /></template>
    <!-- The instant, not the day: a morning's runs must be told apart. -->
    <template #cell-startedAt="{ row }">{{ formatIsoTimestamp(row.startedAt, localeStore.current) }}</template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('tasks.list.rowActions', { subject: row.subject })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger>
          <UiIcon name="ellipsis" size="md" />
        </template>
        <UiDropdownItem @select="emit('open', row)">{{ t('tasks.list.watch') }}</UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
