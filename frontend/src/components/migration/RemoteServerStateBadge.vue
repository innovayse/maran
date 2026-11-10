<script setup lang="ts">
/**
 * The state of a remote server as a badge: the text always names the state, the tone only
 * repeats it, so colour is never the sole carrier of meaning. The words are the panel's own
 * chrome, taken from the locale table keyed by the state the panel reports.
 */
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { RemoteServerState } from '../../types/migrationSource'

/** Props accepted by {@link RemoteServerStateBadge}. */
defineProps<{
  /** The state to name. */
  state: RemoteServerState
}>()

/** The tone of each state: a working server is green, one waiting for the administrator amber, a broken one red. */
const TONES: Readonly<Record<RemoteServerState, BadgeVariant>> = {
  ready: 'success',
  pinned: 'info',
  hostKeyUnconfirmed: 'warning',
  hostKeyChanged: 'danger',
  authRefused: 'danger',
  unreachable: 'danger',
}

const { t } = useI18n()
</script>

<template>
  <UiBadge :variant="TONES[state]">{{ t(`migration.states.${state}`) }}</UiBadge>
</template>
