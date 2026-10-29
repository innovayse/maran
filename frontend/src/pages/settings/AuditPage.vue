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
 * set in the mono face (`UiTableCell mono`) and keep their own case. An identifier (the actor, the
 * subject, the action constant, the address) is one unit and never wraps (`whitespace-nowrap`): a
 * login read across six lines is a different string, and a truncated one is too, so the column
 * takes the identifier's width and the table scrolls inside its container. The localized action
 * name is prose and still wraps at word boundaries.
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
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import { useAuditStore } from '../../stores/audit'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AuditEvent } from '../../types/audit'

const { t } = useI18n()
const auditStore = useAuditStore()
const localeStore = useLocaleStore()

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
    <UiAlert v-if="!auditStore.loading && failedCount > 0" variant="warning">
      {{ t('app.audit.failedNotice', { count: failedCount }) }}
    </UiAlert>

    <UiCard>
      <UiSpinner v-if="auditStore.loading" :label="t('app.audit.loading')" />

      <UiAlert v-else-if="auditStore.errorMessage !== null" variant="error">
        {{ auditStore.errorMessage }}
      </UiAlert>

      <UiEmptyState
        v-else-if="auditStore.events.length === 0"
        :title="t('app.audit.emptyTitle')"
        :description="t('app.audit.emptyDescription')"
      >
        <template #icon><UiIcon name="fileText" size="lg" /></template>
      </UiEmptyState>

      <UiTable v-else :caption="t('app.audit.tableCaption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('app.audit.whenColumn') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('app.audit.actorColumn') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('app.audit.actionColumn') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('app.audit.subjectColumn') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('app.audit.addressColumn') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('app.audit.outcomeColumn') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>

        <UiTableRow v-for="event in auditStore.events" :key="event.id">
          <!-- The instant, not the day. `formatDate` printed only the day here, so every event
               recorded on one day carried the identical string: two entries seconds apart were
               indistinguishable and a day's ordering was unreadable — on the one screen in the
               product whose whole subject is WHEN something happened. `formatIsoTimestamp` is the
               formatter the backups tables already use for exactly this kind of value. -->
          <UiTableCell mono tone="secondary" class="whitespace-nowrap">
            {{ formatIsoTimestamp(event.occurredAt, localeStore.current) }}
          </UiTableCell>
          <UiTableCell class="font-medium whitespace-nowrap">{{ event.actorUsername }}</UiTableCell>
          <UiTableCell class="min-w-48">
            <!-- Both halves of the action, and neither instead of the other. The name is the
                 backend's own, already localized for the request's language (rules/vue.md: the SPA
                 owns no text for a server outcome) — inventing a translation here would let the SPA
                 and the journal disagree about what happened. The machine constant below it is
                 stable across languages, so it is what an administrator greps a log by and quotes in
                 a ticket — kept as visible, selectable text rather than hidden in a `title`. A panel
                 older than the display name sends no name, and then the constant is the whole cell;
                 an action this build has no entry for arrives with name equal to constant, and one
                 line is enough. -->
            <span v-if="actionLabel(event) !== null" class="block wrap-anywhere">{{ actionLabel(event) }}</span>
            <span
              class="block font-mono whitespace-nowrap"
              :class="actionLabel(event) !== null ? 'mt-0.5 text-xs text-text-muted' : ''"
            >
              {{ event.action }}
            </span>
          </UiTableCell>
          <UiTableCell mono class="whitespace-nowrap">{{ event.subject }}</UiTableCell>
          <UiTableCell mono class="whitespace-nowrap">{{ event.ipAddress }}</UiTableCell>
          <UiTableCell>
            <UiBadge :variant="event.succeeded ? 'success' : 'danger'">
              {{ event.succeeded ? t('app.audit.succeeded') : t('app.audit.failed') }}
            </UiBadge>
          </UiTableCell>
        </UiTableRow>
      </UiTable>
    </UiCard>
  </section>
</template>
