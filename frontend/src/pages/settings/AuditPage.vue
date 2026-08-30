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
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import { useAuditStore } from '../../stores/audit'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const auditStore = useAuditStore()
const localeStore = useLocaleStore()

onMounted(async () => {
  await auditStore.load()
})
</script>

<template>
  <section class="w-full">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('app.audit.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('app.audit.subtitle') }}</p>
    </div>

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
        <UiTableCell>{{ formatDate(event.occurredAt, localeStore.current) }}</UiTableCell>
        <UiTableCell>{{ event.actorUsername }}</UiTableCell>
        <UiTableCell>
          <!-- The action name comes from the backend as its own vocabulary and is shown as
               written: inventing a translation here would let the SPA and the journal disagree
               about what happened. -->
          <span class="font-mono">{{ event.action }}</span>
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
