<script setup lang="ts">
/**
 * One page of the audit journal as a table. Props in, events out: the page owns the paging, the
 * filters and every request.
 *
 * **A failure is told by its text, never by its colour alone.** The outcome column prints "Failed"
 * or "Succeeded" in words; a reader who cannot tell red from green loses nothing.
 *
 * **Machine text is mono and never uppercased.** The instant, the address, the subject and the
 * action constant are values an administrator copies into a ticket or greps a log by, so they are
 * set in the mono face (the `mono` and `date` column kinds) and keep their own case. An identifier
 * never wraps; the localized action name is prose (`text`) and wraps at word boundaries. The table
 * scrolls inside its own container at 390px.
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
    { key: 'action', label: t('app.audit.actionColumn'), kind: 'text', sortField: 'action' },
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
    <template #cell-actor="{ row }">{{ row.actorUsername }}</template>
    <template #cell-action="{ row }">
      <!-- Both halves of the action, and neither instead of the other: the backend's localized name,
           and the machine constant an administrator greps a log by, as selectable text. -->
      <div class="min-w-48">
        <span v-if="actionLabel(row) !== null" class="block wrap-anywhere">{{ actionLabel(row) }}</span>
        <span
          class="block font-mono whitespace-nowrap"
          :class="actionLabel(row) !== null ? 'mt-0.5 text-xs text-text-muted' : ''"
        >
          {{ row.action }}
        </span>
      </div>
    </template>
    <template #cell-subject="{ row }">{{ row.subject }}</template>
    <template #cell-address="{ row }">{{ row.ipAddress }}</template>
    <template #cell-outcome="{ row }">
      <UiBadge :variant="row.succeeded ? 'success' : 'danger'">
        {{ row.succeeded ? t('app.audit.succeeded') : t('app.audit.failed') }}
      </UiBadge>
    </template>
  </UiTable>
</template>
