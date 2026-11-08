<script setup lang="ts">
/**
 * The confirmation a rule change passes through when it could cost the operator their own way into
 * the server.
 *
 * **This dialog is advice; the refusal is the backend's.** For an allow, the panel itself decides
 * whether the rule would stop the address the request comes from reaching an SSH port, and answers
 * 409 with a message from the server when it would: a single-port TCP rule for an SSH port, scoped
 * to a range that does not contain the caller, while no rule for that port exists yet. There is no
 * override, so the dialog never offers one. The page shows the server's own text beside the form
 * and leaves the form filled, and this component does not duplicate that text or the decision.
 * What the confirmation buys on top of the refusal is the half a second in which an operator reads
 * the rule they are about to install back to themselves, and it is the only guard on a REMOVAL:
 * removing the last rule for an SSH port returns the agent's unconditional accept (fail-open by
 * design), so the panel has nothing to refuse there and the dialog asks about every removal.
 *
 * **Why it cannot say "this is your SSH port".** Which ports sshd listens on is a host fact the
 * panel holds — `Firewall__SshPorts`, read from the real `sshd_config` by the installer — and
 * deliberately never sends to the browser: no endpoint exposes it, and `FirewallRuleDto` carries no
 * flag for it. So this screen reasons about the shape of the change instead, and errs towards
 * asking:
 *
 * - a TCP allow scoped to a NARROWER range than "everything" is the only kind of addition that can
 *   displace the SSH fallback, whichever port that turns out to be, so it is confirmed first and
 *   then checked by the panel against the real SSH ports;
 * - any removal can be the removal of the rule governing SSH, so every removal is confirmed.
 *
 * The alternative — assuming port 22 — is the failure this whole chain exists to prevent: it would
 * stay silent for the host running sshd on 2222, which is precisely the host that gets locked out.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import { portRangeLabel } from '../../utils/portRangeLabel'
import type { FirewallRule, PendingFirewallChange } from '../../types/firewall'

/** Props accepted by {@link FirewallLockoutDialog}; the page mounts it only while a change waits. */
const props = defineProps<{
  /** The change awaiting an answer. */
  change: PendingFirewallChange
  /** Whether the change is already in flight, which disables the confirm control. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallLockoutDialog}. */
const emit = defineEmits<{
  /** The operator confirmed the change. */
  (e: 'confirm'): void
  /** The operator dismissed the dialog without confirming. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** Heading, which states which way the change goes before the operator reads anything else. */
const title: ComputedRef<string> = computed(() => {
  return props.change.intent === 'allow'
    ? t('firewall.lockout.allowTitle')
    : t('firewall.lockout.denyTitle')
})

/** The risk this particular change carries, in one paragraph. */
const warning: ComputedRef<string> = computed(() => {
  return props.change.intent === 'allow'
    ? t('firewall.lockout.allowWarning')
    : t('firewall.lockout.denyWarning')
})

/** Label of the control that goes ahead with the change. */
const confirmLabel: ComputedRef<string> = computed(() => {
  return props.change.intent === 'allow'
    ? t('firewall.lockout.confirmAllow')
    : t('firewall.lockout.confirmDeny')
})

/**
 * Names one rule as one line of text, the way the panel's own audit journal names it.
 * @param rule The rule to describe.
 * @returns The rule as one line.
 */
const describe = (rule: FirewallRule): string => {
  return t('firewall.rules.ruleSummary', {
    protocol: rule.protocol,
    ports: portRangeLabel(rule.port, rule.portTo),
    source: rule.sourceCidr,
  })
}
</script>

<template>
  <!-- The confirm and cancel controls sit in the modal's pinned footer, not in the scrolling body:
       on a phone the warning can be taller than the viewport, and a confirmation whose buttons
       scroll out of sight cannot be answered. Focus order is close, Cancel, then the confirm
       control, so the first control reached is never the one that goes ahead with the change. -->
  <UiModal
    :open="true"
    :title="title"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <div class="flex flex-col gap-3">
      <ul class="flex flex-col gap-1 rounded-control border border-border-subtle bg-surface-2 p-3">
        <li
          v-for="rule in change.rules"
          :key="`${rule.protocol}-${portRangeLabel(rule.port, rule.portTo)}-${rule.sourceCidr}`"
          class="font-mono text-sm text-text-primary wrap-anywhere"
        >
          {{ describe(rule) }}
        </li>
      </ul>
      <UiAlert variant="warning">
        <p>{{ warning }}</p>
        <p v-if="change.intent === 'deny' && change.leavesPortsUnruled" class="mt-2 font-semibold">
          {{ t('firewall.lockout.lastRuleForPort') }}
        </p>
      </UiAlert>
      <!-- Said on every confirmation, not only the risky-looking ones: the screen genuinely does
           not know which port SSH is on, and an operator who is told so once will check the port
           themselves — which is the only check that can actually be made here. -->
      <p class="text-sm text-text-muted">{{ t('firewall.lockout.sshPortUnknown') }}</p>
    </div>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">
        {{ t('common.cancel') }}
      </UiButton>
      <UiButton
        :variant="change.intent === 'deny' ? 'destructive' : 'primary'"
        :disabled="busy"
        @click="emit('confirm')"
      >
        {{ confirmLabel }}
      </UiButton>
    </template>
  </UiModal>
</template>
