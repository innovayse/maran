<script setup lang="ts">
/**
 * The body of the notification popover: the newest entries, a way to dismiss each one or all of
 * them, and the states a read can be in (loading, unreachable, nothing to show).
 *
 * A dumb view: the state and the busy flag come in as props, every action goes out as an event,
 * and the shell's bell connects it to the store (rules/vue.md). The list is capped by the panel's
 * page size, so when there are more entries than are drawn the footer says so rather than
 * implying the list is complete; dismissing the visible ones brings the next into view.
 *
 * "Dismiss all" is offered only when there is something to dismiss and is disabled while a
 * dismissal is on its way, so a double press cannot send two. A refused dismissal is shown above
 * the list in the panel's own words.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import ShellNotificationRow from './ShellNotificationRow.vue'
import type { Paged } from '../../types/list'
import type { Read } from '../../types/loading'
import type { PanelNotification } from '../../types/notification'

/** Props accepted by {@link ShellNotificationFeed}. */
const props = defineProps<{
  /** Where the read of the list stands. */
  feed: Read<Paged<PanelNotification>>
  /** True while a dismissal is on its way. */
  busy: boolean
  /** The panel's reason for a refused dismissal, already localized, or `null`. */
  dismissError: string | null
}>()

/** Events emitted by {@link ShellNotificationFeed}. */
const emit = defineEmits<{
  /** The person dismissed one entry. */
  (e: 'dismiss', id: string): void
  /** The person dismissed every entry. */
  (e: 'dismissAll'): void
  /** The person asked to read the list again after a failure. */
  (e: 'retry'): void
  /** The person followed an entry's link. */
  (e: 'follow'): void
}>()

const { t } = useI18n()

/** The entries drawn, or none while the list is not loaded. */
const items: ComputedRef<PanelNotification[]> = computed(() => {
  return props.feed.state === 'loaded' ? props.feed.value.items : []
})

/** How many entries exist beyond the ones drawn. */
const hidden: ComputedRef<number> = computed(() => {
  return props.feed.state === 'loaded' ? Math.max(props.feed.value.total - props.feed.value.items.length, 0) : 0
})
</script>

<template>
  <div class="flex flex-col" :aria-busy="feed.state === 'loading'">
    <UiAlert v-if="dismissError !== null" variant="error" class="m-3">{{ dismissError }}</UiAlert>

    <div v-if="feed.state === 'idle' || feed.state === 'loading'" class="flex flex-col gap-3 p-4">
      <UiSkeleton />
      <UiSkeleton />
      <UiSkeleton />
    </div>

    <div v-else-if="feed.state === 'failed'" class="flex flex-col items-start gap-3 p-4">
      <UiAlert variant="error" class="w-full">{{ feed.message ?? t('notifications.unreachable') }}</UiAlert>
      <UiButton variant="secondary" @click="emit('retry')">{{ t('notifications.retry') }}</UiButton>
    </div>

    <UiEmptyState
      v-else-if="items.length === 0"
      :title="t('notifications.emptyTitle')"
      :description="t('notifications.emptyDescription')"
    />

    <template v-else>
      <ul class="flex flex-col" :aria-label="t('notifications.listLabel')">
        <ShellNotificationRow
          v-for="notification in items"
          :key="notification.id"
          :notification="notification"
          :busy="busy"
          @dismiss="emit('dismiss', $event)"
          @follow="emit('follow')"
        />
      </ul>
      <div class="flex flex-wrap items-center justify-between gap-2 border-t border-border-subtle px-4 py-2">
        <p v-if="hidden > 0" class="text-xs text-text-muted">{{ t('notifications.more', { count: hidden }, hidden) }}</p>
        <UiButton variant="secondary" class="ml-auto" :disabled="busy" @click="emit('dismissAll')">
          {{ t('notifications.dismissAll') }}
        </UiButton>
      </div>
    </template>
  </div>
</template>
