<script setup lang="ts">
/**
 * Background tasks screen: what the panel has been doing, and one task watched live.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. State comes exclusively from the tasks store, which is the same store the
 * shell header's badge reads; the page never touches the API layer (rules/vue.md).
 *
 * **Layout (redesign 2026-10).** The heading block with its eyebrow, then what needs attention —
 * a failed task, as a warning notice with the way to open it — then the live pane when a task is
 * open, then the listing in a card. The notice names the count and nothing else: the failure code
 * belongs to the pane, where it is shown once, as the thing an operator quotes.
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
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiMeter, { type UiMeterStatus } from '../../components/ui/UiMeter.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
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
import type { PanelTask, PanelTaskStatus } from '../../types/panelTask'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'

const { t } = useI18n()
const store = useTasksStore()
const localeStore = useLocaleStore()
const kindLabel = useTaskKindLabel()

/** The failed tasks, in the listing's order — newest first — so the first is the latest failure. */
const failedTasks: ComputedRef<PanelTask[]> = computed(() => {
  return store.tasks.filter((task) => {
    return task.status === 'failed'
  })
})

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
 * Opens the latest failed task, the one the attention notice is about.
 * @returns Nothing.
 */
const openLatestFailure = (): void => {
  const latest = failedTasks.value[0]
  if (latest !== undefined) {
    open(latest.id)
  }
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

/**
 * The meter status of a row's small progress bar: the accent while it runs, green when done, red
 * when it failed — the same three the pane uses, so a row and its pane agree.
 * @param status The task's status.
 * @returns The status the kit's meter colours itself by.
 */
const meterStatus = (status: PanelTaskStatus): UiMeterStatus => {
  switch (status) {
    case 'completed':
      return 'success'
    case 'failed':
      return 'danger'
    default:
      return 'running'
  }
}

onMounted(refresh)
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('tasks.list.eyebrow')"
      :title="t('tasks.list.heading')"
      :subtitle="t('tasks.list.subtitle')"
    />

    <!-- What needs attention goes first: a task that failed. -->
    <UiAlert v-if="failedTasks.length > 0" variant="warning">
      {{ t('tasks.list.failedNotice', { count: failedTasks.length }) }}
      <template #actions>
        <UiButton variant="secondary" @click="openLatestFailure">
          {{ t('tasks.list.openFailure') }}
        </UiButton>
      </template>
    </UiAlert>

    <!-- Keyed by task, so opening another one starts a fresh pane — a fresh scroll position
         included — instead of reusing the previous task's. -->
    <TaskLivePane v-if="store.openTask !== null" :key="store.openTask.id" :task="store.openTask" @close="close" />

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('tasks.list.recentHeading')" />

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
            <UiTableHeaderCell align="end">{{ t('tasks.columns.percent') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('tasks.columns.startedAt') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('common.actions') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="task in store.tasks" :key="task.id">
          <UiTableCell class="min-w-40 font-medium">{{ kindLabel(task.kind) }}</UiTableCell>
          <!-- The subject is whatever the module named the task after, and it is machine text as
               often as not — a domain, a system user name, an identifier. Monospaced so a column of
               them lines up and a zero is not an O; never uppercased. It never wraps (a hyphen or a dot is a
               break opportunity, and `break-all` shredded it into a column of fragments): an identifier
               read across six lines is a different string, so the table scrolls instead. -->
          <UiTableCell mono tone="secondary" class="whitespace-nowrap">{{ task.subject }}</UiTableCell>
          <UiTableCell><TaskStatusBadge :status="task.status" /></UiTableCell>
          <!-- The wire value is an integer 0-100 clamped by the module, never absent, so the cell
               always carries a percentage and says so: a bare "100" names no unit. The bar beside
               it is decoration and carries no text, so the cell's text is the figure alone (the
               meter is told not to print its own, which would read "40%40%"). -->
          <UiTableCell align="end" mono>
            <div class="flex items-center justify-end gap-3">
              <!-- Decoration: the figure beside it is the text, and a row's bar must not be announced
                   as a second progressbar next to the pane's (nor share its name). The fixed width
                   keeps the column's bars aligned whatever the figure's length. -->
              <div class="w-16 shrink-0" aria-hidden="true">
                <UiMeter
                  tone="progress"
                  :status="meterStatus(task.status)"
                  :value="task.percent"
                  :max="100"
                  :label="t('tasks.columns.percent')"
                  :value-text="t('tasks.list.percentValue', { percent: task.percent })"
                  :show-value="false"
                />
              </div>
              <span class="min-w-10">{{ t('tasks.list.percentValue', { percent: task.percent }) }}</span>
            </div>
          </UiTableCell>
          <UiTableCell mono tone="secondary" class="whitespace-nowrap">{{ started(task.startedAt) }}</UiTableCell>
          <UiTableCell align="end">
            <!-- One command today and a menu anyway, so the last column has one shape in every
                 table of the panel and an operator learns the control once. The trigger's name
                 carries the row's subject, because "Actions" repeated down a column of identical
                 triggers names nothing to a screen reader. -->
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('tasks.list.rowActions', { subject: task.subject })"
              align="end"
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
    </UiCard>
  </section>
</template>
