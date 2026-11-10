<script setup lang="ts">
/**
 * The name server's card on the nameservers page: whether this server answers DNS queries and the
 * switch that turns it on or off, and whether the panel firewall lets its port through. Turning the
 * server on or off NEVER touches the firewall: the daemon listens on every address, and whether the
 * world reaches it is the firewall's decision. So the card derives the port's state from the rules
 * the page hands in and offers the fix beside the warning (open the port, or close what a stopped
 * server left behind), as the FTPS card does. The fix is offered, never applied.
 *
 * "Turned on", "running" and "listening" are three facts and are shown as three: the first is the
 * setting, the other two are read from the host, and a server that is on but not answering is the
 * state an administrator most needs to see. When the agent could not be reached the host's facts
 * are unknown, and the card says so instead of printing them as "no".
 *
 * Dumb by the usual contract: the page owns the store and the confirmation.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DnsPolicyState, DnsServerStatus } from '../../types/dnsSettings'
import type { FirewallProtocol, FirewallRule } from '../../types/firewall'

/** Whether the panel firewall lets the name server's port through. `unknown` is a rule list not read. */
type PortState = 'unknown' | 'open' | 'partial' | 'closed'

/** Props accepted by {@link DnsServerPanel}. */
const props = defineProps<{
  /** The name server's state. */
  server: DnsServerStatus
  /** The port rules the firewall is running, or `null` while they are not read (or the read failed). */
  rules: readonly FirewallRule[] | null
  /** Whether a change is in flight, which disables the button. */
  acting: boolean
}>()

/** Events emitted by {@link DnsServerPanel}. */
const emit = defineEmits<{
  /** The person asked to turn the name server on or off. */
  (e: 'switch', on: boolean): void
  /** The person asked to open the port; carries exactly the rules still missing. */
  (e: 'open-port', rules: FirewallRule[]): void
  /** The person asked to close the port; carries exactly the single-port rules that let it through. */
  (e: 'close-port', rules: FirewallRule[]): void
}>()

const { t } = useI18n()

/** The two protocols a name server answers on. */
const PROTOCOLS: readonly FirewallProtocol[] = ['tcp', 'udp']

/** The protocols no running rule lets through. */
const missingProtocols: ComputedRef<FirewallProtocol[]> = computed(() => {
  const rules = props.rules ?? []
  return PROTOCOLS.filter((protocol) => {
    return !rules.some((rule) => {
      return lets(rule, protocol)
    })
  })
})

/** What the firewall does with the port. */
const portState: ComputedRef<PortState> = computed(() => {
  if (props.rules === null) {
    return 'unknown'
  }
  if (missingProtocols.value.length === 0) {
    return 'open'
  }
  return missingProtocols.value.length === PROTOCOLS.length ? 'closed' : 'partial'
})

/** The rules opening the port would add: one single-port rule for each protocol still missing. */
const rulesToOpen: ComputedRef<FirewallRule[]> = computed(() => {
  return missingProtocols.value.map((protocol) => {
    return { port: props.server.port, portTo: null, protocol, sourceCidr: '' }
  })
})

/**
 * The rules closing the port would remove: those naming exactly the port, spelled as listed. A
 * broader rule (a range) is left to the firewall screen, which can narrow it safely.
 */
const rulesToClose: ComputedRef<FirewallRule[]> = computed(() => {
  return (props.rules ?? []).filter((rule) => {
    return rule.portTo === null && rule.port === props.server.port
  })
})

/** The server is on and something is still missing in the firewall. */
const isOnAndBlocked: ComputedRef<boolean> = computed(() => {
  return props.server.enabled && (portState.value === 'closed' || portState.value === 'partial')
})

/** The server is off and a rule still lets its port through. */
const isOffAndOpen: ComputedRef<boolean> = computed(() => {
  return !props.server.enabled && (portState.value === 'open' || portState.value === 'partial')
})

/**
 * Whether a rule lets the name server's port through for a protocol.
 * @param rule A running rule.
 * @param protocol The protocol asked about.
 * @returns True when the rule's port or range contains the server's port for that protocol.
 */
const lets = (rule: FirewallRule, protocol: FirewallProtocol): boolean => {
  return rule.protocol === protocol && rule.port <= props.server.port && (rule.portTo ?? rule.port) >= props.server.port
}

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
    <UiAlert v-if="isOnAndBlocked" variant="warning">
      <span data-testid="dns-port-blocked">{{ t('dns.server.firewall.blocked', { port: server.port }) }}</span>
      <template #actions>
        <UiButton variant="secondary" :disabled="acting" @click="emit('open-port', rulesToOpen)">
          {{ t('dns.server.firewall.open', { port: server.port }) }}
        </UiButton>
      </template>
    </UiAlert>
    <UiAlert v-if="isOffAndOpen" variant="warning">
      <span data-testid="dns-port-lingering">{{ t('dns.server.firewall.lingering', { port: server.port }) }}</span>
      <template #actions>
        <UiButton
          v-if="rulesToClose.length > 0"
          variant="secondary"
          :disabled="acting"
          @click="emit('close-port', rulesToClose)"
        >
          {{ t('dns.server.firewall.close', { port: server.port }) }}
        </UiButton>
      </template>
    </UiAlert>
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
      <UiDescriptionItem :term="t('dns.server.firewall.label', { port: server.port })">
        {{ t(`dns.server.firewall.states.${portState}`) }}
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
      {{ server.enabled ? t('dns.server.onExplanation', { port: server.port }) : t('dns.server.offExplanation') }}
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
