<script setup lang="ts">
/**
 * The two port presets that sit beside the raw rule form: the ports an operator opens so often that
 * typing them into three fields is friction, and nothing more than that.
 *
 * A preset is a shortcut to the SAME request the form sends — never a second way of doing it. It
 * composes rules out of the reported list and emits them; the page sends them through the store and
 * the panel re-validates every one, exactly as it does a hand-typed rule.
 *
 * **Both presets read their own state out of the rules the panel reported**, rather than keeping a
 * flag of their own. A toggle holding its own idea of whether MySQL is open would disagree with the
 * firewall the moment somebody changed the rule from the table beside it, or from another session —
 * and the toggle is the one the operator would believe.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import { ANY_IPV4_SOURCE } from '../../utils/anySourceRange'
import type { FirewallRule } from '../../types/firewall'

/** Props accepted by {@link FirewallPresetButtons}. */
const props = defineProps<{
  /** The rules the panel reported, which are what both presets read their state from. */
  rules: readonly FirewallRule[]
  /** Whether a rule change is already in flight, which disables both controls. */
  busy: boolean
}>()

/** Events emitted by {@link FirewallPresetButtons}. */
const emit = defineEmits<{
  /** Fired with the rules to install, in the order they should be sent. */
  (e: 'allow', rules: FirewallRule[]): void
  /** Fired with the rules to remove, spelled exactly as the listing reported them. */
  (e: 'deny', rules: FirewallRule[]): void
}>()

/** The ports a web server answers on: plain HTTP and HTTPS. */
const WEB_PORTS = [80, 443]

/** The port MySQL listens on, which is the one this toggle opens to the outside world or does not. */
const MYSQL_PORT = 3306

/** The rules the web preset installs, open to every source as a public web server must be. */
const WEB_RULES: FirewallRule[] = WEB_PORTS.map((port) => {
  return { port, protocol: 'tcp', sourceCidr: ANY_IPV4_SOURCE }
})

const { t } = useI18n()

/** The web ports the panel is not already running a TCP rule for. */
const missingWebRules: ComputedRef<FirewallRule[]> = computed(() => {
  return WEB_RULES.filter((candidate) => {
    return !props.rules.some((rule) => {
      return rule.protocol === 'tcp' && rule.port === candidate.port
    })
  })
})

/**
 * Every TCP rule the panel is running for MySQL's port, whatever source each is scoped to.
 *
 * The scoped ones are included on purpose: turning the toggle off has to remove what is actually
 * there, and a deny naming a source range the rule was not installed with matches nothing while
 * still reporting success — the operator would then read a closed toggle over an open port.
 */
const mysqlRules: ComputedRef<FirewallRule[]> = computed(() => {
  return props.rules.filter((rule) => {
    return rule.protocol === 'tcp' && rule.port === MYSQL_PORT
  })
})

/** Whether MySQL's port is reachable from outside the host at all. */
const isMysqlOpen: ComputedRef<boolean> = computed(() => {
  return mysqlRules.value.length > 0
})

/**
 * Opens the web ports the host is not already serving.
 *
 * Only the missing ones are sent: the panel answers a duplicate rule with a conflict, so a preset
 * that re-sent both every time would fail on the second press for no reason an operator could act
 * on.
 * @returns Nothing; emits synchronously.
 */
const applyWebPreset = (): void => {
  emit('allow', missingWebRules.value)
}

/**
 * Opens or closes MySQL's port to the outside world.
 * @param open The state the operator asked for.
 * @returns Nothing; emits synchronously.
 */
const toggleMysql = (open: boolean): void => {
  if (open) {
    emit('allow', [{ port: MYSQL_PORT, protocol: 'tcp', sourceCidr: ANY_IPV4_SOURCE }])
    return
  }
  emit('deny', [...mysqlRules.value])
}
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1 p-4.5">
    <h2 class="text-lg font-semibold text-text-primary">{{ t('firewall.presets.heading') }}</h2>
    <p class="mt-1 text-sm text-text-muted">{{ t('firewall.presets.subtitle') }}</p>
    <div class="mt-3.5 flex flex-wrap items-center gap-4">
      <UiButton
        variant="secondary"
        :disabled="busy || missingWebRules.length === 0"
        @click="applyWebPreset"
      >
        {{ t('firewall.presets.web') }}
      </UiButton>
      <UiSwitch
        :model-value="isMysqlOpen"
        :label="t('firewall.presets.mysqlExternal')"
        :disabled="busy"
        @update:model-value="toggleMysql"
      />
    </div>
  </div>
</template>
