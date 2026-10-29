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
 *
 * Rendered as a list of tiles (name left, state badge right) that fills whatever width it is given:
 * the monitoring card and the dashboard's status card both use it, so its props and its
 * `data-testid` are a contract with both and do not change with its looks.
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
  <!-- `auto-fill` rather than a fixed column count: this component is rendered at two widths (the
       full monitoring card and the narrower dashboard card), and the tiles should fill whichever
       they are given. `min-w-0` + `wrap-anywhere` keep a long translated name inside its tile at
       390px. -->
  <ul class="grid grid-cols-[repeat(auto-fill,minmax(14rem,1fr))] gap-3" data-testid="monitoring-services">
    <li
      v-for="status in statuses"
      :key="status.service"
      class="flex min-w-0 items-center justify-between gap-3 rounded-control border border-border-subtle bg-surface-2 px-4 py-3"
      :title="status.detail"
    >
      <!-- The localized name, not the machine member: `font-mono` left with the constant it was
           chosen for — a translated phrase is prose, not code. -->
      <span class="min-w-0 text-sm font-semibold text-text-primary wrap-anywhere">{{ status.name }}</span>
      <UiBadge :variant="toneOf(status.state)" class="shrink-0">{{ labelOf(status.state) }}</UiBadge>
    </li>
  </ul>
</template>
