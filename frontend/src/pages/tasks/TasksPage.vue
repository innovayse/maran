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
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import TaskLivePane from '../../components/tasks/TaskLivePane.vue'
import TaskStatusBadge from '../../components/tasks/TaskStatusBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { useTasksStore } from '../../stores/tasks'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const store = useTasksStore()
const localeStore = useLocaleStore()

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
 * @param startedAt The instant, as the module sent it.
 * @returns The formatted date.
 */
const started = (startedAt: string): string => {
  return formatDate(startedAt, localeStore.current)
}

onMounted(refresh)
</script>

<template>
  <section class="w-full">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('tasks.list.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('tasks.list.subtitle') }}</p>
    </div>

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
          <UiTableHeaderCell>{{ t('tasks.columns.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="task in store.tasks" :key="task.id">
        <UiTableCell class="font-medium">{{ task.kind }}</UiTableCell>
        <UiTableCell class="break-all text-text-secondary">{{ task.subject }}</UiTableCell>
        <UiTableCell><TaskStatusBadge :status="task.status" /></UiTableCell>
        <UiTableCell class="font-mono">{{ task.percent }}</UiTableCell>
        <UiTableCell class="font-mono text-text-muted">{{ started(task.startedAt) }}</UiTableCell>
        <UiTableCell>
          <UiButton
            variant="secondary"
            :aria-label="t('tasks.list.watchTask', { subject: task.subject })"
            @click="open(task.id)"
          >
            {{ t('tasks.list.watch') }}
          </UiButton>
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
