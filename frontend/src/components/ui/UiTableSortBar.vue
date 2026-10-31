<script setup lang="ts">
/**
 * The sort control of a {@link UiTable} drawn as cards (its phone layout, issue #217), where there
 * is no header row whose headings could be pressed.
 *
 * It is derived from the same column schema as the headings, so it offers exactly the columns the
 * desktop table lets a reader sort by, under the same names, and it speaks the table's own sort
 * contract: it emits `sort` with a column's `sortField`, exactly as pressing that heading would.
 * Choosing a column in the select is pressing its heading once (the list sorts by it ascending);
 * the direction button is pressing the current column's heading again (the order flips). Nothing
 * here sorts or knows how the caller turns the field into a sort key.
 *
 * The direction button is named by what it does ("Sort descending" while the list is ascending),
 * and it is disabled while the list is in the server's default order, which has no column to flip.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'
import UiSelect, { type SelectOption } from './UiSelect.vue'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link UiTableSortBar}. */
const props = defineProps<{
  /** The table's column schema; only the sortable columns are offered. */
  columns: readonly UiTableColumn[]
  /** The list's sort key, `-` prefixed when descending, empty for the server's default order. */
  sort: string
}>()

/** Events emitted by {@link UiTableSortBar}. */
const emit = defineEmits<{
  /** The reader asked to sort by a column, carrying that column's `sortField`. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()

/** The sortable columns, under their headings' names. */
const options: ComputedRef<SelectOption[]> = computed(() => {
  return props.columns.flatMap((column) => {
    return column.sortField === undefined ? [] : [{ value: column.sortField, label: column.label }]
  })
})

/** Whether the list is sorted in descending order. */
const descending: ComputedRef<boolean> = computed(() => {
  return props.sort.startsWith('-')
})

/** The field the list is sorted by, without its direction; empty for the default order. */
const field: ComputedRef<string> = computed(() => {
  return descending.value ? props.sort.slice(1) : props.sort
})

/**
 * Sorts by the chosen column.
 * @param next The chosen column's `sortField`.
 * @returns Nothing.
 */
const chooseField = (next: string): void => {
  if (next !== '' && next !== field.value) {
    emit('sort', next)
  }
}

/**
 * Flips the order of the current column.
 * @returns Nothing.
 */
const reverse = (): void => {
  if (field.value !== '') {
    emit('sort', field.value)
  }
}
</script>

<template>
  <div class="flex items-end gap-2" data-testid="table-sort-bar">
    <div class="min-w-0 flex-1">
      <UiSelect
        :model-value="field"
        :label="t('app.table.sortBy')"
        :options="options"
        :placeholder="t('app.table.defaultOrder')"
        @update:model-value="chooseField"
      />
    </div>
    <UiButton
      variant="secondary"
      size="icon"
      :disabled="field === ''"
      :aria-label="t(descending ? 'app.table.sortAscending' : 'app.table.sortDescending')"
      @click="reverse"
    >
      <UiIcon :name="descending ? 'chevronDown' : 'chevronUp'" size="md" />
    </UiButton>
  </div>
</template>
