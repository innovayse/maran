<script setup lang="ts">
/**
 * Says why no Node.js app is on offer when the host could not offer one, in the place the Node.js apps
 * would have been, instead of leaving them out silently (issue #532). The sentence is the panel's own,
 * already localized; this only picks the tone: a host whose sources carry no Node.js is an answer
 * (`info`), a listing that could not be read may be cured by trying again (`warning`).
 */
import { computed, type ComputedRef } from 'vue'
import UiAlert, { type UiAlertVariant } from '../ui/UiAlert.vue'
import type { HostNodejsUnavailable } from '../../types/hostApp'

/** Props accepted by {@link NodejsUnavailableAlert}. */
const props = defineProps<{
  /** Why Node.js is missing. */
  reason: HostNodejsUnavailable
  /** The panel's sentence for the reason, in the user's language. */
  message: string
}>()

/** The alert's tone for the reason. */
const variant: ComputedRef<UiAlertVariant> = computed(() => {
  return props.reason === 'listingFailed' ? 'warning' : 'info'
})
</script>

<template>
  <UiAlert :variant="variant">{{ message }}</UiAlert>
</template>
