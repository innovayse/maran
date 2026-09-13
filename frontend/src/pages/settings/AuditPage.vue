<script setup lang="ts">
/**
 * The audit journal (spec §10): who did what, when, and from where. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under.
 *
 * Read-only by construction. Entries are written by the backend from inside the
 * handlers that perform an action, so there is nothing here to edit and no route
 * that could amend one. The endpoint is administrators-only; a customer who
 * reaches this URL sees the panel's own refusal, rendered verbatim like any other.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
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
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="t('app.audit.heading')" :subtitle="t('app.audit.subtitle')" />

    <UiSpinner v-if="auditStore.loading" :label="t('app.audit.loading')" />

    <UiAlert v-else-if="auditStore.errorMessage !== null" variant="error">
      {{ auditStore.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else-if="auditStore.events.length === 0"
      :title="t('app.audit.emptyTitle')"
      :description="t('app.audit.emptyDescription')"
    />

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
        <UiTableCell>{{ formatIsoTimestamp(event.occurredAt, localeStore.current) }}</UiTableCell>
        <UiTableCell>{{ event.actorUsername }}</UiTableCell>
        <UiTableCell>
          <!-- Both halves of the action, and neither instead of the other. The name is the
               backend's own, already localized for the request's language (rules/vue.md: the SPA
               owns no text for a server outcome) — inventing a translation here would let the SPA
               and the journal disagree about what happened. The machine constant below it is
               stable across languages, so it is what an administrator greps a log by and quotes in
               a ticket — kept as visible, selectable text rather than hidden in a `title`. A panel
               older than the display name sends no name, and then the constant is the whole cell;
               an action this build has no entry for arrives with name equal to constant, and one
               line is enough. -->
          <span v-if="actionLabel(event) !== null" class="block">{{ actionLabel(event) }}</span>
          <span
            class="block font-mono"
            :class="actionLabel(event) !== null ? 'mt-0.5 text-xs text-text-muted' : ''"
          >
            {{ event.action }}
          </span>
        </UiTableCell>
        <UiTableCell>
          <span class="block max-w-[320px] truncate">{{ event.subject }}</span>
        </UiTableCell>
        <UiTableCell>
          <span class="font-mono">{{ event.ipAddress }}</span>
        </UiTableCell>
        <UiTableCell>
          <UiBadge :variant="event.succeeded ? 'success' : 'danger'">
            {{ event.succeeded ? t('app.audit.succeeded') : t('app.audit.failed') }}
          </UiBadge>
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
