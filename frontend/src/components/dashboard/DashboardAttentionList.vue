<script setup lang="ts">
/**
 * What on this server is asking to be looked at: certificates near expiry, backups that failed,
 * addresses the firewall is refusing, panel tasks that failed.
 *
 * **Only the rows that are not zero are shown, and when none of them is, the notice says so in a
 * sentence.** That is the opposite of {@link DashboardCountGrid}, where every tile is always drawn,
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
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { DashboardAttention, DashboardSection } from '../../types/dashboard'

/** Which attention source a row is about; the list key and the i18n leaf it is worded by. */
type AttentionKey = 'certificates' | 'backups' | 'tasks' | 'bans'

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

const { t } = useI18n()

/** Every source the panel reported, read or not, in the order they are judged below. */
const sources: ComputedRef<AttentionSource[]> = computed(() => {
  return [
    { key: 'certificates', section: props.attention.certificatesExpiringSoon, to: '/sites' },
    { key: 'backups', section: props.attention.failedBackups, to: '/backups' },
    { key: 'tasks', section: props.attention.failedTasks, to: '/tasks' },
    { key: 'bans', section: props.attention.bannedAddresses, to: '/firewall/bans' },
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

/** The sources the panel could not read, each of which is named rather than counted as zero. */
const unchecked: ComputedRef<AttentionSource[]> = computed(() => {
  return sources.value.filter((source) => {
    return source.section.state === 'unavailable'
  })
})

/** Whether everything was read and nothing needs attention — the only case the all-clear is true. */
const allClear: ComputedRef<boolean> = computed(() => {
  return rows.value.length === 0 && unchecked.value.length === 0
})
</script>

<template>
  <UiAlert v-if="!allClear" variant="warning">
    <p class="font-semibold">{{ t('app.status.attention.title') }}</p>

    <ul v-if="rows.length > 0" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="row in rows" :key="row.key">
        <RouterLink
          :to="row.to"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span class="font-mono font-semibold text-text-primary tabular-nums">{{ row.value }}</span>
          <span>{{ t(`app.status.attention.${row.key}`, row.value) }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>

    <!-- Named, never counted: a source the panel could not read is not a zero. -->
    <ul v-if="unchecked.length > 0" class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="source in unchecked" :key="source.key">
        <RouterLink
          :to="source.to"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span>{{ t(`app.status.attention.unchecked.${source.key}`) }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>
  </UiAlert>

  <UiAlert v-else variant="success">{{ t('app.status.attention.allClear') }}</UiAlert>
</template>
