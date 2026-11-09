<script setup lang="ts">
/**
 * One entry of the notification feed: a severity mark, the headline sentence, an optional second
 * line, when it happened, a link to the screen it is about, and a button that dismisses it.
 *
 * **Text is the panel's.** The headline and the detail arrive worded in the reader's language and
 * are rendered as plain text, never as markup: a detail can carry a service manager's words or a
 * task's subject, which are attacker-influenced. Only the chrome (the link and the dismiss
 * button's names) is translated here.
 *
 * **The link is a closed set.** The panel sends a route NAME from a short list, never a URL, and
 * this component maps it to a page. A name the SPA has no route for draws no link: the entry is
 * still readable, and the SPA invents no destination.
 *
 * Severity is carried by the icon and its colour AND by the sentence: the icon is decorative, so
 * the colour is never the only signal.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import UiButton from '../ui/UiButton.vue'
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import type { NotificationSeverity, PanelNotification } from '../../types/notification'

/** Props accepted by {@link ShellNotificationRow}. */
const props = defineProps<{
  /** The entry to draw. */
  notification: PanelNotification
  /** True while a dismissal is on its way; the dismiss button is then disabled. */
  busy: boolean
}>()

/** Events emitted by {@link ShellNotificationRow}. */
const emit = defineEmits<{
  /** The person dismissed this entry. */
  (e: 'dismiss', id: string): void
  /** The person followed the entry's link; the feed closes so the destination is not hidden behind it. */
  (e: 'follow'): void
}>()

/** The glyph and colour of each severity. */
const SEVERITY_MARKS: Record<NotificationSeverity, { icon: UiIconName; tone: string }> = {
  info: { icon: 'info', tone: 'text-accent-text' },
  warning: { icon: 'triangleAlert', tone: 'text-warning' },
  critical: { icon: 'circleAlert', tone: 'text-danger' },
}

const { t } = useI18n()
const router = useRouter()
const localeStore = useLocaleStore()

/** The severity's glyph and colour. */
const mark: ComputedRef<{ icon: UiIconName; tone: string }> = computed(() => {
  return SEVERITY_MARKS[props.notification.severity]
})

/** Where the entry leads, or `null` when it names no page the SPA has a route for. */
const destination: ComputedRef<RouteLocationRaw | null> = computed(() => {
  const name = props.notification.deepLink
  return name !== null && router.hasRoute(name) ? { name } : null
})

/** The instant, in the reader's language. */
const when: ComputedRef<string> = computed(() => {
  return formatIsoTimestamp(props.notification.occurredAt, localeStore.current)
})
</script>

<template>
  <li class="flex items-start gap-3 border-b border-border-subtle px-4 py-3 last:border-b-0" data-testid="notification-row">
    <UiIcon :name="mark.icon" size="lg" :class="mark.tone" />
    <div class="flex min-w-0 flex-1 flex-col gap-1">
      <p class="text-sm font-semibold text-text-primary">{{ notification.title }}</p>
      <p v-if="notification.detail !== null" class="text-sm text-text-secondary">{{ notification.detail }}</p>
      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
        <time class="text-xs text-text-muted tabular-nums" :datetime="notification.occurredAt">{{ when }}</time>
        <UiButton v-if="destination !== null" :to="destination" variant="quiet" size="bar" @click="emit('follow')">
          {{ t('notifications.open') }}
          <UiIcon name="arrowRight" size="sm" />
        </UiButton>
      </div>
    </div>
    <UiButton
      variant="quiet"
      size="icon-sm"
      :disabled="busy"
      :aria-label="t('notifications.dismiss', { title: notification.title })"
      @click="emit('dismiss', notification.id)"
    >
      <UiIcon name="x" size="sm" />
    </UiButton>
  </li>
</template>
