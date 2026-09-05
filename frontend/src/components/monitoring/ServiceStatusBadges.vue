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
 * The service's name is the agent's own machine name (`webServer`, `phpFpm`), rendered verbatim:
 * the module ships no localized display text for it, and inventing one in the SPA would be this
 * bundle holding a name for a server-side thing (rules/vue.md).
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
      <span class="font-mono text-sm text-text-secondary">{{ status.service }}</span>
      <UiBadge :variant="toneOf(status.state)">{{ labelOf(status.state) }}</UiBadge>
    </div>
  </div>
</template>
