<script setup lang="ts">
/**
 * One page of a folder as a table: a tick, the name (a link into a folder, a button that opens a
 * file), the kind, the size, when it changed, the permission bits and each row's actions menu.
 * Props in, events out: the page owns the paging, the sort, the selection and what an action does.
 *
 * **Keyboard.** Every openable name is a stop in the Tab order. ArrowDown and ArrowUp move focus to
 * the name above or below, Enter opens (a link or a button does that natively) and Space ticks the
 * row, the way a list of files behaves elsewhere. The handlers sit on the wrapper, so one listener
 * serves every row and the table and its card layout stay the kit's.
 *
 * Names are drawn as text, never as markup: a customer chooses them.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiTable from '../ui/UiTable.vue'
import FileEntryIcon from './FileEntryIcon.vue'
import FileEntryMenu from './FileEntryMenu.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatDate } from '../../utils/formatDate'
import type { FileEntry, FileEntryAction } from '../../types/file'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link FileBrowserTable}. */
const props = defineProps<{
  /** The page of entries to draw. */
  rows: FileEntry[]
  /** Whether the page is being read; the table keeps its shape with placeholder rows. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The list's sort key, `-` prefixed when descending. */
  sort: string
  /** The paths of the ticked rows. */
  selectedPaths: readonly string[]
  /** Where a link into a folder goes, given the folder's path. */
  folderTarget: (path: string) => RouteLocationRaw
}>()

/** Events emitted by {@link FileBrowserTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
  /** A row was ticked or unticked. */
  (e: 'select', path: string, selected: boolean): void
  /** The name of a file was pressed. */
  (e: 'open', entry: FileEntry): void
  /** An item of a row's menu was chosen. */
  (e: 'action', action: FileEntryAction, entry: FileEntry): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The attribute that marks a name as a stop of the arrow-key movement. */
const OPENER_SELECTOR = '[data-file-open]'

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('filesBrowser.columns.name'), kind: 'identity', sortField: 'name', minWidth: '14rem' },
    { key: 'kind', label: t('filesBrowser.columns.kind'), kind: 'text' },
    { key: 'size', label: t('filesBrowser.columns.size'), kind: 'number', sortField: 'size' },
    { key: 'modified', label: t('filesBrowser.columns.modified'), kind: 'date', sortField: 'modified' },
    { key: 'mode', label: t('filesBrowser.columns.mode'), kind: 'mono' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Says what an entry is, in words.
 * @param entry The entry.
 * @returns The kind's label; a link is a link whatever it points at.
 */
const kindLabel = (entry: FileEntry): string => {
  if (entry.isSymlink) {
    return t('filesBrowser.kinds.link')
  }
  return t(`filesBrowser.kinds.${entry.kind}`)
}

/**
 * Says how big an entry is; a folder's own node size means nothing to a person, so it is an absence.
 * @param entry The entry.
 * @returns The formatted size, or the em dash for a folder.
 */
const sizeLabel = (entry: FileEntry): string => {
  return entry.kind === 'directory' ? t('common.emptyValue') : formatBytes(entry.sizeBytes)
}

/**
 * Whether a row is ticked.
 * @param entry The entry.
 * @returns True when its path is among the ticked ones.
 */
const isSelected = (entry: FileEntry): boolean => {
  return props.selectedPaths.includes(entry.path)
}

/**
 * Finds the name the key event came from.
 * @param event The keyboard event.
 * @returns The name element, or `null` when focus is on anything else.
 */
const openerOf = (event: KeyboardEvent): HTMLElement | null => {
  const target = event.target
  return target instanceof HTMLElement && target.matches(OPENER_SELECTOR) ? target : null
}

/**
 * Moves focus between the names with the arrow keys and ticks a row with Space.
 * @param event The keydown event bubbling from a row.
 * @returns Nothing.
 */
const onKeydown = (event: KeyboardEvent): void => {
  const opener = openerOf(event)
  if (opener === null) {
    return
  }
  if (event.key === ' ') {
    // A button would also click on the key's release; the row is ticked instead.
    event.preventDefault()
    const path = opener.dataset.filePath ?? ''
    emit('select', path, !props.selectedPaths.includes(path))
    return
  }
  if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
    return
  }
  event.preventDefault()
  const root = opener.closest('[data-testid="file-browser"]')
  const openers = Array.from(root?.querySelectorAll<HTMLElement>(OPENER_SELECTOR) ?? [])
  const next = openers[openers.indexOf(opener) + (event.key === 'ArrowDown' ? 1 : -1)]
  next?.focus()
}

/**
 * Stops the release of Space from pressing a name that Space only ticked.
 * @param event The keyup event.
 * @returns Nothing.
 */
const onKeyup = (event: KeyboardEvent): void => {
  if (event.key === ' ' && openerOf(event) !== null) {
    event.preventDefault()
  }
}
</script>

<template>
  <div data-testid="file-browser" @keydown="onKeydown" @keyup="onKeyup">
    <UiTable
      :caption="t('filesBrowser.tableCaption')"
      :columns="columns"
      :rows="rows"
      row-key="path"
      :loading="loading"
      :skeleton-rows="skeletonRows"
      :sort="sort"
      @sort="emit('sort', $event)"
    >
      <template #cell-name="{ row }">
        <div class="flex min-w-0 items-center gap-2">
          <UiCheckbox
            :model-value="isSelected(row)"
            :label="t('filesBrowser.selectRow', { name: row.name })"
            hide-label
            @update:model-value="emit('select', row.path, $event)"
          />
          <FileEntryIcon :entry="row" />
          <RouterLink
            v-if="row.kind === 'directory'"
            :to="folderTarget(row.path)"
            data-file-open
            :data-file-path="row.path"
            class="inline-flex min-h-6 min-w-0 items-center rounded-control font-semibold text-accent-text wrap-anywhere hover:underline focus-visible:shadow-focus focus-visible:outline-none"
            >{{ row.name }}</RouterLink
          >
          <UiButton
            v-else-if="row.kind === 'file'"
            variant="ghost"
            data-file-open
            :data-file-path="row.path"
            @click="emit('open', row)"
            >{{ row.name }}</UiButton
          >
          <span v-else class="wrap-anywhere">{{ row.name }}</span>
        </div>
      </template>
      <template #cell-kind="{ row }">{{ kindLabel(row) }}</template>
      <template #cell-size="{ row }">{{ sizeLabel(row) }}</template>
      <template #cell-modified="{ row }">{{ formatDate(row.modifiedAt, localeStore.current) }}</template>
      <template #cell-mode="{ row }">{{ row.mode }}</template>
      <template #cell-actions="{ row }">
        <FileEntryMenu :entry="row" @action="emit('action', $event, row)" />
      </template>
    </UiTable>
  </div>
</template>
