<script setup lang="ts">
/**
 * What on this server is asking to be looked at: certificates near expiry, backups that failed,
 * addresses the firewall is refusing, panel tasks that failed, sites running on a PHP version that
 * no longer receives security fixes, a licence close to its expiry.
 *
 * **Only the rows that are not zero are shown, and when none of them is, the notice says so in a
 * sentence.** That is the opposite of {@link DashboardTotalsGrid}, where every tile is always drawn,
 * and the difference is deliberate: a count of accounts is information at any value, while a list of
 * four permanent zeros trains a reader to stop looking at the one card whose whole purpose is to be
 * noticed when it changes.
 *
 * Each row links to the screen that holds the detail. A number on a landing page is only useful if
 * the next click is obvious.
 *
 * **This is the page's notice, and it is drawn first.** The one thing that needs attention goes
 * above everything else as an amber `UiAlert` (a warning is amber, never blue); when nothing does,
 * the same slot says so in a green one, so the reader learns the answer from the same place either
 * way. The rows live inside the notice and are drawn as links, not as a second card, so the sentence
 * and its next click are one object.
 *
 * **A source the panel could not read is named, and the all-clear is drawn only when every source was
 * read** (issue #248). Each count arrives as a section: `unavailable` is not a zero, and folding it
 * into one would announce "nothing needs attention" about something nobody checked. So the notice is
 * amber whenever a source is unread, with a sentence per source ("Backups could not be checked")
 * linking to the screen where the operator can look for themselves.
 *
 * **The host clock is the one source that is not a count** (issue #150). A clock that is not
 * synchronised is a row of its own, worded without a number, linking to the monitoring screen where
 * the backend's own explanation is shown; a clock the panel could not read is unchecked like any
 * other source.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { DashboardAttention, DashboardSection } from '../../types/dashboard'
import { attentionIsClear } from '../../utils/attentionIsClear'

/** Which attention source a row is about; the list key and the i18n leaf it is worded by. */
type AttentionKey = 'certificates' | 'backups' | 'tasks' | 'bans' | 'endOfLifePhp' | 'licenceExpiring'

/** A source the panel could not read: a counted one, the clock, or the alert delivery state. */
interface UncheckedSource {
  /** Which source; worded under `app.status.attention.unchecked`. */
  key: AttentionKey | 'clock' | 'alertDelivery'
  /** The route the row links to. */
  to: string
}

/** One source as the panel answered it: what it is, its count section, and where to look. */
interface AttentionSource {
  /** Which source; worded under `app.status.attention` and `app.status.attention.unchecked`. */
  key: AttentionKey
  /** The count, or the fact that the source could not be read. */
  section: DashboardSection<number>
  /** The route the row links to. */
  to: string
}

/** One row worth showing: a source that was read and is not zero. */
interface AttentionRow {
  /** Which source. */
  key: AttentionKey
  /** How many there are. */
  value: number
  /** The route the row links to. */
  to: string
}

const props = defineProps<{
  /** The attention counts, as the panel answered them. */
  attention: DashboardAttention
}>()

/** Where the clock's own explanation is shown. */
const CLOCK_ROUTE = '/monitoring'

/** Where an alert channel's settings, and so the way to fix its delivery, are. */
const ALERT_SETTINGS_ROUTE = '/settings/alerts'

/** One alert channel whose last delivery failed. */
interface FailingChannel {
  /** Which channel; worded under `app.status.attention`. */
  key: 'alertMail' | 'alertWebhook'
  /** The panel's reason, already localized. */
  message: string | null
}

const { t } = useI18n()

/** Every source the panel reported, read or not, in the order they are judged below. */
const sources: ComputedRef<AttentionSource[]> = computed(() => {
  return [
    { key: 'certificates', section: props.attention.certificatesExpiringSoon, to: '/sites' },
    { key: 'backups', section: props.attention.failedBackups, to: '/backups' },
    { key: 'tasks', section: props.attention.failedTasks, to: '/tasks' },
    { key: 'bans', section: props.attention.bannedAddresses, to: '/firewall/bans' },
    { key: 'endOfLifePhp', section: props.attention.sitesOnEndOfLifePhp, to: '/settings/php' },
    { key: 'licenceExpiring', section: props.attention.licenceExpiring, to: '/settings/licence' },
  ]
})

/** The rows worth showing: the sources that were read and are not zero. */
const rows: ComputedRef<AttentionRow[]> = computed(() => {
  return sources.value.flatMap((source): AttentionRow[] => {
    return source.section.state === 'loaded' && source.section.data > 0
      ? [{ key: source.key, value: source.section.data, to: source.to }]
      : []
  })
})

/** Whether the clock was read and found anything but synchronised. */
const clockNeedsAttention: ComputedRef<boolean> = computed(() => {
  return props.attention.clock.state === 'loaded' && props.attention.clock.data.state !== 'synchronized'
})

/** The alert channels whose last delivery failed; none while the state is unread or both are fine. */
const failingChannels: ComputedRef<FailingChannel[]> = computed(() => {
  const delivery = props.attention.alertDelivery
  if (delivery.state !== 'loaded') {
    return []
  }
  const channels: FailingChannel[] = []
  if (delivery.data.mail.state === 'failing') {
    channels.push({ key: 'alertMail', message: delivery.data.mail.failureMessage })
  }
  if (delivery.data.webhook.state === 'failing') {
    channels.push({ key: 'alertWebhook', message: delivery.data.webhook.failureMessage })
  }
  return channels
})

/** The sources the panel could not read, each of which is named rather than counted as zero. */
const unchecked: ComputedRef<UncheckedSource[]> = computed(() => {
  const counted = sources.value
    .filter((source) => {
      return source.section.state === 'unavailable'
    })
    .map((source): UncheckedSource => {
      return { key: source.key, to: source.to }
    })
  const withClock: UncheckedSource[] =
    props.attention.clock.state === 'unavailable' ? [...counted, { key: 'clock', to: CLOCK_ROUTE }] : counted
  return props.attention.alertDelivery.state === 'unavailable'
    ? [...withClock, { key: 'alertDelivery', to: ALERT_SETTINGS_ROUTE }]
    : withClock
})

/** Whether everything was read and nothing needs attention — the only case the all-clear is true. */
const allClear: ComputedRef<boolean> = computed(() => {
  return attentionIsClear(props.attention)
})
</script>

<template>
  <UiAlert v-if="!allClear" variant="warning">
    <p class="font-semibold">{{ t('app.status.attention.title') }}</p>

    <ul v-if="rows.length > 0" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="row in rows" :key="row.key">
        <RouterLink
          :to="row.to"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
        >
          <span class="font-mono font-semibold text-text-primary tabular-nums">{{ row.value }}</span>
          <span>{{ t(`app.status.attention.${row.key}`, row.value) }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>

    <!-- The clock is not a count, so its row carries no number (issue #150). -->
    <ul v-if="clockNeedsAttention" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li>
        <RouterLink
          :to="CLOCK_ROUTE"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span>{{ t('app.status.attention.clock') }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>

    <!-- A failing alert channel is not a count either: it names the channel and the panel's reason (issue #142). -->
    <ul v-if="failingChannels.length > 0" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="channel in failingChannels" :key="channel.key">
        <RouterLink
          :to="ALERT_SETTINGS_ROUTE"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span>{{ t(`app.status.attention.${channel.key}`) }}</span>
          <span v-if="channel.message !== null">{{ channel.message }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>

    <!-- Named, never counted: a source the panel could not read is not a zero. -->
    <ul v-if="unchecked.length > 0" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="source in unchecked" :key="source.key">
        <RouterLink
          :to="source.to"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
        >
          <span>{{ t(`app.status.attention.unchecked.${source.key}`) }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>
  </UiAlert>

  <UiAlert v-else variant="success">{{ t('app.status.attention.allClear') }}</UiAlert>
</template>
