<script setup lang="ts">
/**
 * The notices above the Updates list: how many of the waiting updates are security updates, and
 * whether the server needs a reboot.
 *
 * **Each notice appears only on a measured answer.** A `null` security count (the agent could not
 * classify) and a `null` or false reboot requirement draw nothing: an unmeasured figure is never
 * shown as "no security updates" or "no reboot needed". A count of zero is a real answer and is
 * said, except that when nothing at all is pending and no filter narrows the list the page shows
 * only its empty state, so the security notice stays away (`hasUpdates`).
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'

/** Props accepted by {@link PendingUpdatesSummary}. */
defineProps<{
  /** How many updates are security updates; `null` when not measured. */
  securityCount: number | null
  /** Whether a reboot is required; `null` when not measured. */
  rebootRequired: boolean | null
  /** Whether the list holds any update or a search or filter narrows it; false means the empty state is showing. */
  hasUpdates: boolean
}>()

const { t } = useI18n()
</script>

<template>
  <div v-if="(hasUpdates && securityCount !== null) || rebootRequired === true" class="flex flex-col gap-3">
    <UiAlert v-if="hasUpdates && securityCount !== null" :variant="securityCount > 0 ? 'warning' : 'success'">
      {{
        securityCount > 0
          ? t('monitoring.updates.securitySummary', { n: securityCount }, securityCount)
          : t('monitoring.updates.securityNone')
      }}
    </UiAlert>
    <UiAlert v-if="rebootRequired === true" variant="warning">
      {{ t('monitoring.updates.rebootRequired') }}
    </UiAlert>
  </div>
</template>
