<script setup lang="ts">
/**
 * The audit journal (spec §10): who did what, when, from where, and whether it worked. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under.
 *
 * Read-only by construction. Entries are written by the backend from inside the handlers that
 * perform an action, so there is nothing here to edit and no route that could amend one. The
 * endpoint is administrators-only; a customer who reaches this URL sees the panel's own refusal,
 * rendered verbatim like any other.
 *
 * **Layout (redesign 2026-10).** The heading block with its eyebrow, then what needs attention — a
 * warning notice naming how many recorded entries failed, shown only when there are any — then the
 * journal in a card. The notice is a count and nothing more: the page offers no filter, so it
 * promises no action. A refusal, the loading state and the empty state all render inside the same
 * card, so the screen keeps one shape whatever the endpoint answered.
 *
 * **A failure is told by its text, never by its colour alone.** The outcome column prints
 * "Failed" or "Succeeded" in words (the badge leads the word with a dot in the status colour, and
 * the word stays primary text); a reader who cannot tell red from green loses nothing.
 *
 * **Machine text is mono and never uppercased.** The instant, the address, the subject and the
 * action constant are values an administrator copies into a ticket or greps a log by, so they are
 * set in the mono face (the `mono` and `date` column kinds) and keep their own case. An identifier
 * (the actor, the subject, the address) is one unit and never wraps — every column kind but `text`
 * keeps one line: a login read across six lines is a different string, and a truncated one is too,
 * so the column takes the identifier's width and the table scrolls inside its container. The
 * localized action name is prose (`text`) and still wraps at word boundaries.
 *
 * **Loading keeps the journal's shape.** While the entries are on the way the table shows its real
 * header over placeholder rows drawn from its own `columns`, inside a {@link UiLoadingRegion} that
 * announces the wait once; a failed read is the region's alert, never a skeleton.
 *
 * **The table scrolls inside its own container at 390px.** Six columns do not fit a phone; the
 * `UiTable` wrapper is the scroll container, so the content panel never scrolls sideways.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useAuditStore } from '../../stores/audit'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AuditEvent } from '../../types/audit'

const { t } = useI18n()
const auditStore = useAuditStore()
const localeStore = useLocaleStore()

/** The journal's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'when', label: t('app.audit.whenColumn'), kind: 'date' },
    { key: 'actor', label: t('app.audit.actorColumn'), kind: 'identity' },
    { key: 'action', label: t('app.audit.actionColumn'), kind: 'text' },
    { key: 'subject', label: t('app.audit.subjectColumn'), kind: 'mono' },
    { key: 'address', label: t('app.audit.addressColumn'), kind: 'mono' },
    { key: 'outcome', label: t('app.audit.outcomeColumn'), kind: 'badge' },
  ]
})

/** How many of the held entries record a failed action — the notice above the journal. */
const failedCount: ComputedRef<number> = computed(() => {
  return auditStore.events.filter((event) => {
    return !event.succeeded
  }).length
})

/**
 * The readable half of an event's action cell, or `null` when there is nothing beyond the machine
 * constant to show: a panel older than the display name sends no name at all, and an action this
 * build has no entry for arrives named as itself — either way one line is enough.
 * @param event The journal row the cell is drawn for.
 * @returns The backend-localized name, or `null` when only the constant should render.
 */
const actionLabel = (event: AuditEvent): string | null => {
  const name: unknown = event.actionName
  return typeof name === 'string' && name.length > 0 && name !== event.action ? name : null
}

onMounted(async () => {
  await auditStore.load()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('app.audit.eyebrow')"
      :title="t('app.audit.heading')"
      :subtitle="t('app.audit.subtitle')"
    />

    <!-- What needs attention goes first: an action that was refused or failed. -->
    <UiAlert v-if="auditStore.loadState === 'loaded' && failedCount > 0" variant="warning">
      {{ t('app.audit.failedNotice', { count: failedCount }, failedCount) }}
    </UiAlert>

    <UiCard>
      <UiLoadingRegion :state="auditStore.loadState">
        <template #error>
          <UiAlert variant="error">{{ auditStore.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
        </template>

        <UiEmptyState
          v-if="auditStore.loadState === 'loaded' && auditStore.events.length === 0"
          :title="t('app.audit.emptyTitle')"
          :description="t('app.audit.emptyDescription')"
        >
          <template #icon><UiIcon name="fileText" size="lg" /></template>
        </UiEmptyState>

        <UiTable
          v-else
          :caption="t('app.audit.tableCaption')"
          :columns="columns"
          :rows="auditStore.events"
          row-key="id"
          :loading="auditStore.loadState === 'loading'"
        >
          <!-- The instant, not the day: two entries seconds apart must be told apart on the one
               screen whose whole subject is WHEN something happened. -->
          <template #cell-when="{ row }">{{ formatIsoTimestamp(row.occurredAt, localeStore.current) }}</template>
          <template #cell-actor="{ row }">{{ row.actorUsername }}</template>
          <template #cell-action="{ row }">
            <!-- Both halves of the action, and neither instead of the other. The name is the
                 backend's own, already localized (rules/vue.md: the SPA owns no text for a server
                 outcome); the machine constant below it is what an administrator greps a log by,
                 kept as visible, selectable text. A panel older than the display name sends no
                 name, and then the constant is the whole cell. -->
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
      </UiLoadingRegion>
    </UiCard>
  </section>
</template>
