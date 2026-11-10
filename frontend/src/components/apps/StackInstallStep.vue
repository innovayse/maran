<script setup lang="ts">
/**
 * The wizard's last step: the install queue with live progress. One row per chosen app with its
 * state, and under each running or failed row the meter fed by the task that carries it. The rows
 * are the queue the wizard walks; this component only displays it.
 */
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import HostAppProgress from './HostAppProgress.vue'
import type { StackQueueItem, StackQueueState } from '../../composables/useStackWizard'
import type { PanelTask } from '../../types/panelTask'

/** Props accepted by {@link StackInstallStep}. */
defineProps<{
  /** The queue, in install order. */
  queue: readonly StackQueueItem[]
  /** Reads the newest snapshot of the task carrying an app's install. */
  progressOf: (id: string) => PanelTask | null
}>()

const { t } = useI18n()

/**
 * The badge tone of a queue state.
 * @param state The state of a queued app.
 * @returns The tone.
 */
const variantOf = (state: StackQueueState): BadgeVariant => {
  switch (state) {
    case 'done':
      return 'success'
    case 'failed':
      return 'danger'
    case 'running':
      return 'info'
    case 'waiting':
    default:
      return 'neutral'
  }
}
</script>

<template>
  <ul class="flex flex-col gap-3" :aria-label="t('apps.setup.queue.caption')">
    <li v-for="item in queue" :key="item.id" :data-testid="`stack-queue-${item.id}`">
      <UiCard>
        <div class="flex flex-col gap-2">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span class="font-semibold">{{ item.name }}</span>
            <UiBadge :variant="variantOf(item.state)">{{ t(`apps.setup.queue.${item.state}`) }}</UiBadge>
          </div>
          <HostAppProgress v-if="item.state !== 'waiting'" :name="item.name" :task="progressOf(item.id)" />
        </div>
      </UiCard>
    </li>
  </ul>
</template>
