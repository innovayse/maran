<script setup lang="ts">
/**
 * The port presets that sit beside the raw rule form: the ports an operator opens so often that
 * typing them into three fields is friction, and nothing more than that.
 *
 * The panel produces every preset, its name and the exact rules it installs; this component holds
 * no port and no label. A preset is a shortcut to the SAME request the form sends — never a second
 * way of doing it. It composes rules out of the preset and the reported list and emits them; the
 * page sends them through the store and the panel re-validates every one, exactly as it does a
 * hand-typed rule.
 *
 * **Every preset reads its own state out of the rules the panel reported**, rather than keeping a
 * flag of its own. A toggle holding its own idea of whether MySQL is open would disagree with the
 * firewall the moment somebody changed the rule from the table beside it, or from another session —
 * and the toggle is the one the operator would believe.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { portRangeLabel } from '../../utils/portRangeLabel'
import type { FirewallPreset, FirewallRule } from '../../types/firewall'

/** Props accepted by {@link FirewallPresetButtons}. */
const props = defineProps<{
  /** The presets the panel offers, in the order it lists them. */
  presets: readonly FirewallPreset[]
  /** The rules the panel reported, which are what every preset reads its state from. */
  rules: readonly FirewallRule[]
  /** Whether a rule change is already in flight, which disables every control. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallPresetButtons}. */
const emit = defineEmits<{
  /** Fired with the rules to install, in the order they should be sent. */
  (e: 'allow', rules: FirewallRule[]): void
  /** Fired with the rules to remove, spelled exactly as the listing reported them. */
  (e: 'deny', rules: FirewallRule[]): void
}>()

const { t } = useI18n()

/**
 * Whether a running rule names the same port, range and protocol as a preset's rule.
 *
 * The source range is deliberately not compared: a rule scoped to a narrower range still holds the
 * port open, and `portTo` is compared rather than only the port because a range that happens to
 * start at 80 is a different rule, and treating it as the preset's would offer a switch that
 * removes something else.
 * @param running A rule the panel reported.
 * @param candidate A rule of a preset.
 * @returns True when they name the same ports over the same protocol.
 */
const sameTarget = (running: FirewallRule, candidate: FirewallRule): boolean => {
  return (
    running.protocol === candidate.protocol && running.port === candidate.port && running.portTo === candidate.portTo
  )
}

/**
 * The preset's rules the panel is not already running a rule for.
 *
 * Only these are sent: the panel answers a duplicate rule with a conflict, so a preset that
 * re-sent everything every time would fail on the second press for no reason an operator could act on.
 * @param preset The preset asked about.
 * @returns The rules still to install.
 */
const missingRules = (preset: FirewallPreset): FirewallRule[] => {
  return preset.rules.filter((candidate) => {
    return !props.rules.some((running) => {
      return sameTarget(running, candidate)
    })
  })
}

/**
 * Every running rule on the preset's ports, whatever source each is scoped to.
 *
 * The scoped ones are included on purpose: turning a switch off has to remove what is actually
 * there, and a deny naming a source range the rule was not installed with matches nothing while
 * still reporting success — the operator would then read a closed switch over an open port.
 * @param preset The preset asked about.
 * @returns The running rules to remove.
 */
const runningRules = (preset: FirewallPreset): FirewallRule[] => {
  return props.rules.filter((running) => {
    return preset.rules.some((candidate) => {
      return sameTarget(running, candidate)
    })
  })
}

/**
 * The label a control carries: the panel's name for the preset and the ports it names.
 * @param preset The preset to label.
 * @returns For example `Open the web ports (80, 443)`.
 */
const labelOf = (preset: FirewallPreset): string => {
  const ports = preset.rules.map((rule) => {
    return portRangeLabel(rule.port, rule.portTo)
  })
  return `${preset.name} (${ports.join(', ')})`
}

/**
 * Whether every rule of the preset is already running.
 * @param preset The preset asked about.
 * @returns True when nothing is left to install.
 */
const isOpen = (preset: FirewallPreset): boolean => {
  return missingRules(preset).length === 0
}

/**
 * Installs the preset's missing rules.
 * @param preset The preset the operator pressed.
 * @returns Nothing; emits synchronously.
 */
const apply = (preset: FirewallPreset): void => {
  emit('allow', missingRules(preset))
}

/**
 * Installs or removes a closable preset.
 * @param preset The preset whose switch was flipped.
 * @param open The state the operator asked for.
 * @returns Nothing; emits synchronously.
 */
const toggle = (preset: FirewallPreset, open: boolean): void => {
  if (open) {
    apply(preset)
    return
  }
  emit('deny', runningRules(preset))
}
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading
      :title="t('firewall.presets.heading')"
      :subtitle="t('firewall.presets.subtitle')"
    />
    <!-- Stacked on a phone so a long translated label gets the full width instead of forcing the
         row wider than the screen; side by side from `sm`. -->
    <div class="flex flex-col items-start gap-4 sm:flex-row sm:flex-wrap sm:items-center">
      <template v-for="preset in presets" :key="preset.key">
        <UiSwitch
          v-if="preset.closable"
          class="max-w-full"
          :model-value="isOpen(preset)"
          :label="labelOf(preset)"
          :disabled="busy"
          @update:model-value="toggle(preset, $event)"
        />
        <UiButton
          v-else
          class="max-w-full px-4"
          variant="secondary"
          :disabled="busy || isOpen(preset)"
          @click="apply(preset)"
        >
          {{ labelOf(preset) }}
        </UiButton>
      </template>
    </div>
  </UiCard>
</template>
