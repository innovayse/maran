<script setup lang="ts">
/**
 * One page of the audit journal as a table. Props in, events out: the page owns the paging, the
 * filters and every request.
 *
 * **A failure is told by its text, never by its colour alone.** The outcome column prints the panel's
 * word for it (`outcomeName`); a reader who cannot tell red from green loses nothing.
 *
 * **Machine text is mono and never uppercased.** The instant, the address and the action constant
 * are values an administrator copies into a ticket or greps a log by, so they are set in the mono
 * face (the `mono` and `date` column kinds) and keep their own case. An identifier never wraps in a
 * table (and breaks in a card, where nothing can scroll); the localized action name and a subject
 * the backend spells out as a sentence (`subjectName`, such as a chain verdict) are prose and wrap. The
 * table scrolls inside its own container at 390px.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AuditEvent } from '../../types/audit'
import type { UiTableColumn } from '../../types/tableColumn'

/** Props accepted by {@link AuditTable}. */
defineProps<{
  /** The rows of the page on screen, in the server's order. */
  rows: AuditEvent[]
  /** Whether a page is being fetched, so the rows are drawn as placeholders. */
  loading: boolean
  /** How many placeholder rows to draw while loading: the page size. */
  skeletonRows: number
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: string
}>()

/** Events emitted by {@link AuditTable}. */
const emit = defineEmits<{
  /** A sortable heading was pressed; carries the server's sort field. */
  (e: 'sort', field: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The journal's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'when', label: t('app.audit.whenColumn'), kind: 'date', sortField: 'occurredAt' },
    { key: 'actor', label: t('app.audit.actorColumn'), kind: 'identity', sortField: 'actor' },
    { key: 'action', label: t('app.audit.actionColumn'), kind: 'text', sortField: 'action', minWidth: '12rem' },
    { key: 'subject', label: t('app.audit.subjectColumn'), kind: 'mono' },
    { key: 'address', label: t('app.audit.addressColumn'), kind: 'mono' },
    { key: 'outcome', label: t('app.audit.outcomeColumn'), kind: 'badge' },
  ]
})

/**
 * The readable half of an event's action cell, or `null` when there is nothing beyond the machine
 * constant to show: an action this build has no entry for arrives named as itself, and then one
 * line is enough.
 * @param event The journal row the cell is drawn for.
 * @returns The backend-localized name, or `null` when only the constant should render.
 */
const actionLabel = (event: AuditEvent): string | null => {
  const name: unknown = event.actionName
  return typeof name === 'string' && name.length > 0 && name !== event.action ? name : null
}

/**
 * Whether the subject is the stored identifier itself (the backend answers a sentence only for an
 * entry whose stored subject is a machine line).
 * @param event The journal row the cell is drawn for.
 * @returns True when `subjectName` is the stored subject unchanged.
 */
const subjectIsIdentifier = (event: AuditEvent): boolean => {
  return event.subjectName === event.subject
}

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
    :caption="t('app.audit.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="id"
    :loading="loading"
    :skeleton-rows="skeletonRows"
    :sort="sort"
    @sort="sortBy"
  >
    <!-- The instant, not the day: two entries seconds apart must be told apart on the one screen
         whose whole subject is WHEN something happened. -->
    <template #cell-when="{ row }">{{ formatIsoTimestamp(row.occurredAt, localeStore.current) }}</template>
    <template #cell-actor="{ row }">
      <!-- Who the entry is about, and when an administrator was viewing as them, who was at the
           keyboard: a second line under the name and not a column of its own, because a column
           that is empty on nearly every row costs the table its width at 390px. -->
      <div>
        <span class="block">{{ row.actorUsername }}</span>
        <span v-if="row.impersonatorUsername !== null" class="mt-0.5 block text-xs text-text-muted wrap-anywhere">
          {{ t('app.audit.viewedBy', { name: row.impersonatorUsername }) }}
        </span>
      </div>
    </template>
    <template #cell-action="{ row }">
      <!-- Both halves of the action, and neither instead of the other: the backend's localized name,
           and the machine constant an administrator greps a log by, as selectable text. -->
      <div>
        <span v-if="actionLabel(row) !== null" class="block wrap-anywhere">{{ actionLabel(row) }}</span>
        <span
          class="block font-mono"
          :class="actionLabel(row) !== null ? 'mt-0.5 text-xs text-text-muted' : ''"
        >
          {{ row.action }}
        </span>
      </div>
    </template>
    <template #cell-subject="{ row }">
      <!-- The column is a mono one, so the cell keeps an identifier on one line in a table (it scrolls
           inside its container) and lets it break in a card at 390px, where there is no column to
           widen. The backend spells a sentence only for a machine line; that one is prose, so it
           takes the line back and wraps. -->
      <template v-if="subjectIsIdentifier(row)">{{ row.subjectName }}</template>
      <span v-else class="block max-w-md font-sans whitespace-normal wrap-anywhere">{{ row.subjectName }}</span>
    </template>
    <template #cell-address="{ row }">{{ row.ipAddress }}</template>
    <template #cell-outcome="{ row }">
      <UiBadge :variant="row.succeeded ? 'success' : 'danger'">
        {{ row.outcomeName }}
      </UiBadge>
    </template>
  </UiTable>
</template>
