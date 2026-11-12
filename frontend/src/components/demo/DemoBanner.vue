<script setup lang="ts">
/**
 * The strip every demo screen opens with, signed in or not: this is a public demo on a simulated
 * server, other visitors see what is typed, and the demo resets on a schedule. It names the next
 * reset so a visitor mid-walkthrough knows how long their changes will last.
 *
 * It exists only in the demo build (the layouts mount it behind `__MARAN_DEMO__`) and draws nothing
 * until the panel has answered, because a banner that guessed would be wrong. The reset time is shown only
 * once the panel has recorded a reset (before that it has none to report); the filter notice and the abuse
 * contact do not depend on the schedule, because the filter is live from the first start.
 *
 * It says that offensive text and outside links are refused (the backend filters what is stored) and
 * names where to report abusive content. The contact is the operator's configured address or URL, shown as
 * plain text exactly as the panel reported it.
 */
import { computed, onMounted, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import { useDemoStore } from '../../stores/demo'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'

const { t } = useI18n()
const demoStore = useDemoStore()
const localeStore = useLocaleStore()

/** Whether the panel has reported its status; the banner draws nothing before that. */
const reported: ComputedRef<boolean> = computed((): boolean => {
  return demoStore.status !== null
})

/** The next reset as the visitor's clock prints it, or `null` before the panel has answered or recorded a reset. */
const nextReset: ComputedRef<string | null> = computed((): string | null => {
  const next = demoStore.status?.nextResetAt ?? null
  return next === null ? null : formatIsoTimestamp(next, localeStore.current)
})

/** The operator's abuse contact, or `null` before the panel has answered or when it sent none. */
const abuseContact: ComputedRef<string | null> = computed((): string | null => {
  const contact = demoStore.status?.abuseContact ?? ''
  return contact === '' ? null : contact
})

onMounted(async (): Promise<void> => {
  await demoStore.load()
})

// The refusal reasons the status carries arrive in the interface language, so a switch asks again.
watch(
  () => {
    return localeStore.current
  },
  async (): Promise<void> => {
    await demoStore.load()
  },
)
</script>

<template>
  <section v-if="reported" :aria-label="t('demo.banner.region')" data-testid="demo-banner">
    <UiAlert variant="info">
      {{ t('demo.banner.text') }}
      {{ t('demo.banner.filtered') }}
      <span v-if="nextReset !== null" data-testid="demo-next-reset">{{ t('demo.banner.nextReset', { time: nextReset }) }}</span>
      <span v-if="abuseContact !== null" data-testid="demo-abuse">
        {{ t('demo.banner.reportAbuse') }}
        <strong data-testid="demo-abuse-contact">{{ abuseContact }}</strong>
      </span>
    </UiAlert>
  </section>
</template>
