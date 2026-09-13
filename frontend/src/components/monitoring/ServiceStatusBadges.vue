<script setup lang="ts">
/**
 * The states of the services the agent watches, one badge per service.
 *
 * **Three states, never two.** The agent reports running, stopped and not-known, and the third is
 * not padding: a socket-activated SSH unit on the Debian family is inactive from boot until the
 * first connection, so collapsing "not known" into "stopped" would report an outage on every such
 * host at every reboot. The three map onto three badge tones, and the badge always carries its text
 * so the tone is never the only thing saying which state it is.
 *
 * **A service with no row is not rendered at all.** The panel sends only what the agent watches, so
 * absence means "this host does not observe that service" — inventing a row for every service the
 * panel knows of would turn that into "we watched it and it was fine".
 *
 * The service's name is the panel's `name` field, already localized by the backend for the
 * request's language, rendered verbatim (rules/vue.md: the SPA never holds display text for a
 * server-side thing). The machine `service` member stays what a row is keyed by — stable across
 * languages, which a translated name is not.
 */
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { ServiceState, ServiceStatus } from '../../types/monitoring'

/** Props accepted by {@link ServiceStatusBadges}. */
defineProps<{
  /** The rows the panel answered with, in its own order. */
  statuses: ServiceStatus[]
}>()

const { t } = useI18n()

/**
 * The badge tone for a state.
 * @param state The state the panel reported.
 * @returns The tone: success for up, danger for down, neutral for not known — never a warning,
 * which would read as a problem where the honest answer is "nobody knows".
 */
const toneOf = (state: ServiceState): BadgeVariant => {
  switch (state) {
    case 'running':
      return 'success'
    case 'stopped':
      return 'danger'
    default:
      return 'neutral'
  }
}

/**
 * The translated label for a state.
 *
 * A state is a machine token, not a server-produced message, so this SPA owns its wording — the
 * same distinction rules/vue.md draws when it says an error `code` stays useful for behaviour while
 * its TEXT comes from the panel.
 * @param state The state the panel reported.
 * @returns The label to render inside the badge.
 */
const labelOf = (state: ServiceState): string => {
  switch (state) {
    case 'running':
      return t('monitoring.services.running')
    case 'stopped':
      return t('monitoring.services.stopped')
    default:
      return t('monitoring.services.unknown')
  }
}
</script>

<template>
  <div class="flex flex-wrap gap-3" data-testid="monitoring-services">
    <div
      v-for="status in statuses"
      :key="status.service"
      class="flex items-center gap-2 rounded-lg border border-border-subtle bg-surface-2 px-3 py-2"
      :title="status.detail"
    >
      <!-- The localized name, not the machine member: `font-mono` left with the constant it was
           chosen for — a translated phrase is prose, not code. -->
      <span class="text-sm font-medium text-text-secondary">{{ status.name }}</span>
      <UiBadge :variant="toneOf(status.state)">{{ labelOf(status.state) }}</UiBadge>
    </div>
  </div>
</template>
