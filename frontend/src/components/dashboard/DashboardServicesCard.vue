<script setup lang="ts">
/**
 * The services the agent watches, on the landing page, through the same badges the monitoring screen
 * uses.
 *
 * Three answers, drawn three ways. Listed services are badges. A list the agent could not give is a
 * sentence saying so, in the card's own place (issue #248): an absent card would look exactly like a
 * server that watches nothing. An empty watch list is a true answer with nothing to draw, so the card
 * is not drawn at all — an empty panel under a heading reads as a failed load.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import ServiceStatusBadges from '../monitoring/ServiceStatusBadges.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DashboardSection } from '../../types/dashboard'
import type { ServiceStatus } from '../../types/monitoring'

const props = defineProps<{
  /** The watched services as the panel answered them, or the fact that they could not be read. */
  services: DashboardSection<ServiceStatus[]>
  /** Whether the heading links on to the monitoring screen, which only a licensed module has. */
  linksToMonitoring: boolean
}>()

const { t } = useI18n()

/** Whether there is anything to draw: services to show, or the fact that they could not be read. */
const drawn: ComputedRef<boolean> = computed(() => {
  return props.services.state === 'unavailable' || props.services.data.length > 0
})
</script>

<template>
  <UiCard v-if="drawn">
    <UiSectionHeading class="mb-4" :title="t('monitoring.services.title')">
      <template v-if="linksToMonitoring" #action>
        <RouterLink
          to="/monitoring"
          class="rounded-control text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >{{ t('monitoring.heading') }}</RouterLink>
      </template>
    </UiSectionHeading>
    <ServiceStatusBadges v-if="services.state === 'loaded'" :statuses="services.data" />
    <p v-else class="m-0 text-sm text-text-secondary">{{ t('app.status.services.unavailable') }}</p>
  </UiCard>
</template>
