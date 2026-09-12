<script setup lang="ts">
/**
 * Background tasks screen: what the panel has been doing, and one task watched live.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. State comes exclusively from the tasks store, which is the same store the
 * shell header's badge reads; the page never touches the API layer (rules/vue.md).
 *
 * **A refusal is rendered, not hidden.** The listing answers 404 to a caller the surface does not
 * exist for — the module's own choice, so a customer is not told there is an administrator-only
 * feed they were refused — and the panel's already-localized message for it is shown here verbatim
 * like any other. No route guard duplicates that rule: a second copy of an authorization decision
 * is a second place for it to be wrong, and the client's copy is the one that cannot be trusted.
 *
 * Opening a task starts its stream and leaves it running when the pane is closed, because the badge
 * is still counting. The shell releases every stream when it tears down.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import TaskLivePane from '../../components/tasks/TaskLivePane.vue'
import TaskStatusBadge from '../../components/tasks/TaskStatusBadge.vue'
import { useTaskKindLabel } from '../../composables/useTaskKindLabel'
import { useLocaleStore } from '../../stores/locale'
import { useTasksStore } from '../../stores/tasks'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'

const { t } = useI18n()
const store = useTasksStore()
const localeStore = useLocaleStore()
const kindLabel = useTaskKindLabel()

/**
 * Reads the listing.
 *
 * The store drops a second request while one is already in flight, so mounting this page at the
 * same moment as the header's badge is one request rather than two.
 * @returns Resolves once the request has settled.
 */
const refresh = async (): Promise<void> => {
  await store.load()
}

/**
 * Opens one task's live pane and starts watching it.
 * @param id The task to open.
 * @returns Nothing.
 */
const open = (id: string): void => {
  store.select(id)
}

/**
 * Closes the live pane, leaving the stream open.
 * @returns Nothing.
 */
const close = (): void => {
  store.deselect()
}

/**
 * Renders when a task started, in the operator's language.
 *
 * The instant, not the day: `formatDate` printed `3 Sep 2026` for every task started on that day,
 * so a list of a morning's provisioning runs said nothing about which ran when. A task is a thing
 * that happened at a moment, and the panel already owns the formatter for one.
 * @param startedAt The instant, as the module sent it.
 * @returns The formatted instant, date and time of day.
 */
const started = (startedAt: string): string => {
  return formatIsoTimestamp(startedAt, localeStore.current)
}

onMounted(refresh)
</script>

<template>
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="t('tasks.list.heading')" :subtitle="t('tasks.list.subtitle')" />

    <TaskLivePane v-if="store.openTask !== null" :task="store.openTask" @close="close" />

    <UiSpinner v-if="store.loading" :label="t('tasks.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else-if="store.isEmpty"
      :title="t('tasks.list.emptyTitle')"
      :description="t('tasks.list.emptyDescription')"
    >
      <template #icon><UiIcon name="listChecks" size="lg" /></template>
    </UiEmptyState>

    <UiTable v-else-if="store.tasks.length > 0" :caption="t('tasks.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('tasks.columns.kind') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('tasks.columns.subject') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('tasks.columns.status') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('tasks.columns.percent') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('tasks.columns.startedAt') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('common.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="task in store.tasks" :key="task.id">
        <UiTableCell class="font-medium">{{ kindLabel(task.kind) }}</UiTableCell>
        <!-- The subject is whatever the module named the task after, and it is machine text as
             often as not — a domain, a system user name, an identifier. Monospaced for the same
             reason `UiDescriptionItem` has a `mono` prop: a column of them lines up, and a zero is
             not an O. -->
        <UiTableCell class="font-mono break-all text-text-secondary">{{ task.subject }}</UiTableCell>
        <UiTableCell><TaskStatusBadge :status="task.status" /></UiTableCell>
        <!-- The wire value is an integer 0-100 clamped by the module, never absent, so the cell
             always carries a percentage and says so: a bare "100" names no unit. -->
        <UiTableCell class="font-mono">{{ t('tasks.list.percentValue', { percent: task.percent }) }}</UiTableCell>
        <UiTableCell class="font-mono text-text-muted">{{ started(task.startedAt) }}</UiTableCell>
        <UiTableCell>
          <!-- One command today and a menu anyway, so the last column has one shape in every
               table of the panel and an operator learns the control once. The trigger's name
               carries the row's subject, because "Actions" repeated down a column of identical
               triggers names nothing to a screen reader. -->
          <UiDropdown
            :label="t('common.actions')"
            :aria-label="t('tasks.list.rowActions', { subject: task.subject })"
            align="start"
            variant="bare"
            :chevron="false"
          >
            <template #trigger>
              <UiIcon name="ellipsis" size="md" />
            </template>
            <UiDropdownItem @select="open(task.id)">{{ t('tasks.list.watch') }}</UiDropdownItem>
          </UiDropdown>
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
