<script setup lang="ts">
/**
 * The confirmation a rule change passes through when it could cost the operator their own way into
 * the server.
 *
 * **This is a UI-level check and nothing more.** The panel accepts either operation whether or not
 * this dialog was shown, and it is right to: the agent renders an unconditional accept for the
 * host's SSH ports whenever no explicit TCP rule for them exists, so removing the last such rule is
 * fail-open by design. What a confirmation buys is the half a second in which an operator reads the
 * rule they are about to install back to themselves.
 *
 * **Why it cannot say "this is your SSH port".** Which ports sshd listens on is a host fact the
 * panel holds — `Firewall__SshPorts`, read from the real `sshd_config` by the installer — and
 * deliberately never sends to the browser: no endpoint exposes it, and `FirewallRuleDto` carries no
 * flag for it. So this screen reasons about the shape of the change instead, and errs towards
 * asking:
 *
 * - a TCP allow scoped to a NARROWER range than "everything" is the only kind of addition that can
 *   displace the SSH fallback, whichever port that turns out to be;
 * - any removal can be the removal of the rule governing SSH, so every removal is confirmed.
 *
 * The alternative — assuming port 22 — is the failure this whole chain exists to prevent: it would
 * stay silent for the host running sshd on 2222, which is precisely the host that gets locked out.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import type { FirewallRule, FirewallRuleChange } from '../../types/firewall'

/** Props accepted by {@link FirewallLockoutDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** Whether the pending change installs rules or removes them. */
  intent: FirewallRuleChange
  /** The rules the change covers, spelled as they will be sent. */
  rules: readonly FirewallRule[]
  /**
   * Whether the change would leave the host with no rule at all for one of the ports it names.
   *
   * True is the case the plan calls "removing the last one": if that port is the SSH port, the
   * panel's unconditional accept comes back and SSH is reachable from every source again.
   */
  leavesPortsUnruled: boolean
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
  return props.intent === 'allow'
    ? t('firewall.lockout.allowTitle')
    : t('firewall.lockout.denyTitle')
})

/** The risk this particular change carries, in one paragraph. */
const warning: ComputedRef<string> = computed(() => {
  return props.intent === 'allow'
    ? t('firewall.lockout.allowWarning')
    : t('firewall.lockout.denyWarning')
})

/** Label of the control that goes ahead with the change. */
const confirmLabel: ComputedRef<string> = computed(() => {
  return props.intent === 'allow'
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
    port: rule.port,
    source: rule.sourceCidr,
  })
}
</script>

<template>
  <UiModal
    :open="open"
    :title="title"
    :close-label="t('firewall.lockout.close')"
    @close="emit('close')"
  >
    <div class="flex flex-col gap-3">
      <ul class="flex flex-col gap-1 rounded-lg border border-border-subtle bg-surface-2 p-3">
        <li v-for="rule in rules" :key="`${rule.protocol}-${rule.port}-${rule.sourceCidr}`" class="font-mono text-sm text-text-primary">
          {{ describe(rule) }}
        </li>
      </ul>
      <p class="text-base text-text-secondary">{{ warning }}</p>
      <p v-if="leavesPortsUnruled" class="text-base text-text-secondary">
        {{ t('firewall.lockout.lastRuleForPort') }}
      </p>
      <!-- Said on every confirmation, not only the risky-looking ones: the screen genuinely does
           not know which port SSH is on, and an operator who is told so once will check the port
           themselves — which is the only check that can actually be made here. -->
      <p class="text-sm text-text-muted">{{ t('firewall.lockout.sshPortUnknown') }}</p>
      <div class="mt-1 flex flex-wrap justify-end gap-2">
        <UiButton variant="secondary" @click="emit('close')">
          {{ t('firewall.lockout.cancel') }}
        </UiButton>
        <UiButton
          :variant="intent === 'deny' ? 'destructive' : 'primary'"
          :disabled="busy"
          @click="emit('confirm')"
        >
          {{ confirmLabel }}
        </UiButton>
      </div>
    </div>
  </UiModal>
</template>
