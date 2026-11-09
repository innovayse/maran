<script setup lang="ts">
/**
 * The app bar's notification bell, for administrators: a count badge on a bell that opens the
 * feed of things the panel wants the operator to know (failed tasks, resource and backup alerts).
 *
 * **It owns the polling's lifetime.** The shell mounts it for an administrator only (the feed is
 * the operator's, and the panel refuses everyone else), so mounting starts the store's polling and
 * unmounting stops it and forgets the entries. A session that stops being an administrator's (a
 * view-as, a sign-out) unmounts the bell, and nothing is polled for the next identity.
 *
 * **The badge is advice, the list is read when the bell opens.** A figure the panel could not give
 * draws no badge, never a zero. The count is white on `--acf` like the tasks badge (5.14:1), and
 * the accessible name carries the number so the badge needs no text of its own for a screen reader.
 * Past the panel's cap it reads "99+" and the name says "more than 99".
 *
 * The popover (`UiPopover`) is the only floating surface; this component wires it to the store and
 * closes it when a link in the feed is followed, so the destination is not left behind a panel.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ShellNotificationFeed from './ShellNotificationFeed.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiPopover from '../ui/UiPopover.vue'
import { useNotificationsFeedStore } from '../../stores/notificationsFeed'

const { t } = useI18n()
const store = useNotificationsFeedStore()

/** Whether the popover is open. */
const open: Ref<boolean> = ref(false)

/** The popover, whose panel takes focus back when a dismissal removes the control that had it. */
const popover: Ref<InstanceType<typeof UiPopover> | null> = ref(null)

/** The figure the badge draws, or `null` when there is nothing to draw (no figure, or none unread). */
const badgeText: ComputedRef<string | null> = computed(() => {
  const figure = store.count
  if (figure === null || figure.count === 0) {
    return null
  }
  return figure.capped ? t('notifications.capped', { count: figure.count }) : String(figure.count)
})

/** The trigger's accessible name, with the number when there is one. */
const triggerLabel: ComputedRef<string> = computed(() => {
  const figure = store.count
  if (figure === null || figure.count === 0) {
    return t('notifications.label')
  }
  return figure.capped
    ? t('notifications.labelCapped', { count: figure.count })
    : t('notifications.labelUnread', { count: figure.count }, figure.count)
})

/**
 * Closes the popover, for a followed link.
 * @returns Nothing.
 */
const close = (): void => {
  open.value = false
}

/**
 * Moves focus to the popover panel when a dismissal left it on nothing: the dismissed row's button
 * is gone once the list is re-read, and focus would otherwise fall to the document body.
 * @returns Nothing.
 */
const keepFocusInPanel = (): void => {
  const active = document.activeElement
  if (active === null || active === document.body) {
    popover.value?.focusPanel()
  }
}

/**
 * Dismisses one entry, then keeps focus inside the panel.
 * @param id The entry's id.
 * @returns Resolves once the dismissal settled and focus was placed.
 */
const dismissOne = async (id: string): Promise<void> => {
  await store.dismiss(id)
  await nextTick()
  keepFocusInPanel()
}

/**
 * Dismisses every entry, then keeps focus inside the panel.
 * @returns Resolves once the dismissal settled and focus was placed.
 */
const dismissEvery = async (): Promise<void> => {
  await store.dismissAll()
  await nextTick()
  keepFocusInPanel()
}

/**
 * Re-reads the list, for the retry after a failure.
 * @returns Nothing.
 */
const retry = (): void => {
  void store.loadFeed()
}

// The list is read each time the bell opens, so it is never older than the moment it was asked for.
watch(open, (isOpen: boolean): void => {
  if (isOpen) {
    void store.open()
  }
})

onMounted(store.start)

onBeforeUnmount(store.stop)
</script>

<template>
  <UiPopover
    ref="popover"
    v-model:open="open"
    :label="triggerLabel"
    :title="t('notifications.title')"
    :close-label="t('notifications.close')"
  >
    <template #trigger>
      <UiIcon name="bell" size="lg" />
      <span
        v-if="badgeText !== null"
        class="absolute -top-1 -right-1 min-w-5 rounded-full bg-accent-fill px-1 text-center text-xs font-bold text-white tabular-nums"
        data-testid="notifications-badge"
        aria-hidden="true"
      >{{ badgeText }}</span>
    </template>

    <ShellNotificationFeed
      :feed="store.feed"
      :busy="store.dismissing"
      :dismiss-error="store.change.state === 'failed' ? store.change.message ?? t('notifications.unreachable') : null"
      @dismiss="dismissOne"
      @dismiss-all="dismissEvery"
      @retry="retry"
      @follow="close"
    />
  </UiPopover>
</template>
