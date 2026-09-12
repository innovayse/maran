<script setup lang="ts">
/**
 * The administrator's FTPS server panel: what the daemon is actually doing, the switch that turns it
 * on and off, and the two things only this screen can see — the disagreements between the daemon and
 * the firewall.
 *
 * **Nothing here is what the panel asked for.** Every daemon fact arrives measured on the host by the
 * agent, out of the configuration the daemon was started against. A panel assembled from the
 * settings row would agree with itself by construction: a green screen over a crashed unit, and — the
 * case `forcedTls` exists for — a green screen over a daemon taking passwords in the clear.
 *
 * **The firewall composition is the reason this component exists rather than two status lines.**
 * Neither module can answer the question alone: the `Ftp` module reads no firewall, and the
 * `Firewall` module knows nothing about which ports FTPS wants. So the two warnings are derived here,
 * from the two stores the page hands in:
 * - the daemon is answering on the host and no rule lets those ports through — listening and
 *   unreachable, which looks exactly like a broken client from outside;
 * - the daemon is off and its rules are still there — `disable` never touches the firewall, because
 *   stopping a service is not editing a firewall, so this is the only place that leftover surface
 *   becomes visible.
 *
 * Dumb by the usual contract: props in, emits out. The page owns both stores.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { FirewallRule } from '../../types/firewall'
import type { FtpsStatus } from '../../types/ftpsStatus'

/** Props accepted by {@link FtpsServerPanel}. */
const props = defineProps<{
  /** The daemon's state as the agent measured it. */
  status: FtpsStatus
  /** The port rules the firewall is actually running, as the Firewall module reported them. */
  rules: readonly FirewallRule[]
  /** Whether a request that changes the daemon or the firewall is already in flight. */
  acting: boolean
}>()

/** Events emitted by {@link FtpsServerPanel}. */
const emit = defineEmits<{
  /** Fired to turn the daemon on, carrying the hostname and the passive address. */
  (e: 'enable', request: { hostname: string; passiveAddress: string }): void
  /** Fired to turn the daemon off. */
  (e: 'disable'): void
  /** Fired to install the rules FTPS needs, carrying exactly those rules. */
  (e: 'open-ports', rules: FirewallRule[]): void
  /** Fired to remove the rules FTPS left behind, carrying exactly those rules. */
  (e: 'close-ports', rules: FirewallRule[]): void
}>()

const { t } = useI18n()

/** The hostname typed into the enable form. Customers will connect to exactly this name. */
const hostname: Ref<string> = ref(props.status.hostname ?? '')

/** The passive-mode advertised address, for a host behind NAT. Empty when it is not. */
const passiveAddress: Ref<string> = ref('')

/**
 * The rules FTPS needs, built from the numbers the panel supplied — the control port and the passive
 * range as the LIVE configuration carries them. No port is written in this SPA.
 */
const requiredRules: ComputedRef<FirewallRule[]> = computed(() => {
  const built: FirewallRule[] = [
    { port: props.status.controlPort, portTo: null, protocol: 'tcp', sourceCidr: '' },
  ]

  // Zero is what the status reports when there is no live configuration to read a range out of, and
  // a rule for port 0 is not a rule — so the range is only offered once the daemon has one.
  if (props.status.passivePortMin > 0 && props.status.passivePortMax > props.status.passivePortMin) {
    built.push({
      port: props.status.passivePortMin,
      portTo: props.status.passivePortMax,
      protocol: 'tcp',
      sourceCidr: '',
    })
  }

  return built
})

/** Whether every port FTPS needs is let through by some rule the firewall is running. */
const isReachable: ComputedRef<boolean> = computed(() => {
  return requiredRules.value.every((required) => {
    return props.rules.some((rule) => {
      const upper = rule.portTo ?? rule.port
      const requiredUpper = required.portTo ?? required.port
      return rule.protocol === 'tcp' && rule.port <= required.port && upper >= requiredUpper
    })
  })
})

/** Whether any rule still lets an FTPS port through, which matters only once the daemon is off. */
const hasLingeringRules: ComputedRef<boolean> = computed(() => {
  return requiredRules.value.some((required) => {
    return props.rules.some((rule) => {
      const upper = rule.portTo ?? rule.port
      return rule.protocol === 'tcp' && rule.port <= required.port && upper >= required.port
    })
  })
})

/**
 * The daemon answers on the host and the firewall lets nothing through. A local probe cannot see
 * this and neither can the firewall screen — only the two together can.
 */
const isListeningAndUnreachable: ComputedRef<boolean> = computed(() => {
  return props.status.controlPortAnswered && !isReachable.value
})

/** The daemon is off and its ports are still open, which is surface nobody is serving. */
const isDisabledAndOpen: ComputedRef<boolean> = computed(() => {
  return !props.status.enabled && hasLingeringRules.value
})

/** The passive range as one readable value, or the placeholder when the daemon has no live one. */
const passiveRangeText: ComputedRef<string> = computed(() => {
  return props.status.passivePortMin > 0
    ? `${props.status.passivePortMin}–${props.status.passivePortMax}`
    : t('common.emptyValue')
})

/**
 * Asks the page to bring the daemon up for the typed hostname.
 * @returns Nothing; emits synchronously.
 */
const enable = (): void => {
  emit('enable', { hostname: hostname.value, passiveAddress: passiveAddress.value })
}

/**
 * Asks the page to stop the daemon. The firewall is deliberately untouched.
 * @returns Nothing; emits synchronously.
 */
const disable = (): void => {
  emit('disable')
}

/**
 * Asks the page to install the rules FTPS needs.
 * @returns Nothing; emits synchronously.
 */
const openPorts = (): void => {
  emit('open-ports', requiredRules.value)
}

/**
 * Asks the page to remove the rules FTPS left behind.
 * @returns Nothing; emits synchronously.
 */
const closePorts = (): void => {
  emit('close-ports', requiredRules.value)
}
</script>

<template>
  <section class="rounded-xl border border-border-subtle bg-surface-1 p-4.5">
    <UiSectionHeading :title="t('ftp.server.heading')" :subtitle="t('ftp.server.description')" />

    <UiDescriptionList class="mt-3">
      <UiDescriptionItem :term="t('ftp.server.fields.state')">
        {{ status.enabled ? t('ftp.server.states.enabled') : t('ftp.server.states.disabled') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.running')">
        {{ status.running ? t('ftp.server.states.running') : t('ftp.server.states.stopped') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.hostname')" mono>
        {{ status.hostname ?? t('common.emptyValue') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.controlPort')" mono>
        {{ status.controlPort }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.passiveRange')" mono>
        {{ passiveRangeText }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.forcedTls')">
        {{ status.forcedTls ? t('ftp.server.states.tlsForced') : t('ftp.server.states.tlsUnknown') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('ftp.server.fields.certificate')" mono>
        {{ status.certificatePath || t('common.emptyValue') }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <!-- Settled row 6's last mile: the host decided, the agent reported, the panel persisted, and
         this is the line the operator actually reads. Shown exactly when the status says so. -->
    <p v-if="status.ipv4Only" data-testid="ftps-ipv4-only" class="mt-3 text-sm text-text-muted">
      {{ t('ftp.server.ipv4Only') }}
    </p>

    <p
      v-if="status.certificateIsSelfSigned"
      data-testid="ftps-self-signed"
      class="mt-2 text-sm text-text-muted"
    >
      {{ t('ftp.server.selfSigned') }}
    </p>

    <UiAlert v-if="isListeningAndUnreachable" variant="error" class="mt-3">
      <span data-testid="ftps-unreachable">{{ t('ftp.server.warnings.unreachable') }}</span>
      <UiButton class="mt-2" variant="secondary" :disabled="acting" @click="openPorts">
        {{ t('ftp.server.warnings.openPorts') }}
      </UiButton>
    </UiAlert>

    <UiAlert v-if="isDisabledAndOpen" variant="error" class="mt-3">
      <span data-testid="ftps-lingering">{{ t('ftp.server.warnings.lingering') }}</span>
      <UiButton class="mt-2" variant="secondary" :disabled="acting" @click="closePorts">
        {{ t('ftp.server.warnings.closePorts') }}
      </UiButton>
    </UiAlert>

    <UiForm class="mt-4" @submit="enable">
      <div class="grid gap-3.5 sm:grid-cols-2">
        <UiInput
          v-model="hostname"
          :label="t('ftp.server.form.hostname')"
          :placeholder="t('ftp.server.form.hostnamePlaceholder')"
          required
        />
        <UiInput
          v-model="passiveAddress"
          :label="t('ftp.server.form.passiveAddress')"
          :placeholder="t('ftp.server.form.passiveAddressPlaceholder')"
        />
      </div>
      <div class="mt-3 flex flex-wrap items-center gap-2">
        <UiButton type="submit" :disabled="acting">{{ t('ftp.server.form.enable') }}</UiButton>
        <UiButton variant="secondary" :disabled="acting" @click="disable">
          {{ t('ftp.server.form.disable') }}
        </UiButton>
      </div>
      <p class="mt-2 text-sm text-text-muted">{{ t('ftp.server.form.note') }}</p>
    </UiForm>
  </section>
</template>
