<script setup lang="ts">
/**
 * The strip every demo screen opens with, signed in or not: this is a public demo on a simulated
 * server, other visitors see what is typed, and the demo resets on a schedule. It names the next
 * reset so a visitor mid-walkthrough knows how long their changes will last.
 *
 * It exists only in the demo build (the layouts mount it behind `__MARAN_DEMO__`) and draws nothing
 * until the panel has reported its schedule, because a banner that guessed the time would be wrong.
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

/** The next reset as the visitor's clock prints it, or `null` before the panel has answered. */
const nextReset: ComputedRef<string | null> = computed((): string | null => {
  const status = demoStore.status
  return status === null ? null : formatIsoTimestamp(status.nextResetAt, localeStore.current)
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
  <section v-if="nextReset !== null" :aria-label="t('demo.banner.region')" data-testid="demo-banner">
    <UiAlert variant="info">
      {{ t('demo.banner.text') }}
      <span data-testid="demo-next-reset">{{ t('demo.banner.nextReset', { time: nextReset }) }}</span>
    </UiAlert>
  </section>
</template>
