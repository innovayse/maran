<script setup lang="ts">
/**
 * The name server's card on the nameservers page: whether this server answers DNS queries and the
 * switch that turns it on or off. Turning it on opens port 53 to the internet, so the sentence
 * saying so sits above the button and the action asks first; turning it off closes the port and
 * keeps every zone.
 *
 * "Turned on", "running" and "listening" are three facts and are shown as three: the first is the
 * setting, the other two are read from the host, and a server that is on but not answering is the
 * state an administrator most needs to see. When the agent could not be reached the host's facts
 * are unknown, and the card says so instead of printing them as "no".
 *
 * Dumb by the usual contract: the page owns the store and the confirmation.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DnsPolicyState, DnsServerStatus } from '../../types/dnsSettings'

/** Props accepted by {@link DnsServerPanel}. */
defineProps<{
  /** The name server's state. */
  server: DnsServerStatus
  /** Whether a change is in flight, which disables the button. */
  acting: boolean
}>()

/** Events emitted by {@link DnsServerPanel}. */
const emit = defineEmits<{
  /** The person asked to turn the name server on or off. */
  (e: 'switch', on: boolean): void
}>()

const { t } = useI18n()

/**
 * The words for what the agent saw of a safety property. `notObserved` is its own sentence and is
 * never worded as "in force".
 * @param state What the agent saw.
 * @returns The reader's-language label.
 */
const policyLabel = (state: DnsPolicyState): string => {
  return t(`dns.server.policy.${state}`)
}
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('dns.server.heading')">
      <template #action>
        <UiBadge :variant="server.running && server.listening ? 'success' : 'neutral'">
          {{ server.running && server.listening ? t('dns.server.answering') : t('dns.server.notAnswering') }}
        </UiBadge>
      </template>
    </UiSectionHeading>
    <UiAlert v-if="!server.agentReachable" variant="warning">{{ t('dns.server.agentUnreachable') }}</UiAlert>
    <UiDescriptionList>
      <UiDescriptionItem :term="t('dns.server.enabledLabel')">
        {{ server.enabled ? t('dns.server.on') : t('dns.server.off') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.server.runningLabel')">
        {{ server.running ? t('dns.server.yes') : t('dns.server.no') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.server.listeningLabel')">
        {{ server.listening ? t('dns.server.yes') : t('dns.server.no') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.server.recursionLabel')">
        {{ policyLabel(server.recursionRefused) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.server.rateLimitLabel')">
        {{ policyLabel(server.responseRateLimiting) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.server.zonesLabel')" mono>{{ server.zoneCount }}</UiDescriptionItem>
      <UiDescriptionItem v-if="server.version.length > 0" :term="t('dns.server.versionLabel')" mono>
        {{ server.version }}
      </UiDescriptionItem>
    </UiDescriptionList>
    <p class="text-sm text-text-secondary">
      {{ server.enabled ? t('dns.server.onExplanation') : t('dns.server.offExplanation') }}
    </p>
    <div>
      <UiButton
        :variant="server.enabled ? 'destructive' : 'primary'"
        :disabled="acting"
        @click="emit('switch', !server.enabled)"
      >
        {{ server.enabled ? t('dns.server.turnOff') : t('dns.server.turnOn') }}
      </UiButton>
    </div>
  </UiCard>
</template>
