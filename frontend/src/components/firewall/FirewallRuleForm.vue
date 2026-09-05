<script setup lang="ts">
/**
 * The raw rule form: a port, a protocol and a source range — the three values a firewall rule IS,
 * because a rule has no identifier on either side of the wire.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md); it reports a validated rule and the page decides what to do with it, which is
 * where the lockout confirmation lives. It also refuses to emit until its own rules pass, so the
 * round trip it saves is a real saving rather than a message printed beside a request that went
 * anyway.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import { ANY_IPV4_SOURCE } from '../../utils/anySourceRange'
import { isUsableCidr } from '../../utils/isUsableCidr'
import type { FirewallProtocol, FirewallRule } from '../../types/firewall'

/** Props accepted by {@link FirewallRuleForm}. */
const props = defineProps<{
  /** Whether a rule change is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link FirewallRuleForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the rule to install. */
  (e: 'submit', rule: FirewallRule): void
}>()

/**
 * A decimal number with no leading zero — the only spelling of a port accepted, mirroring the
 * panel's own parse.
 */
const DECIMAL = /^(0|[1-9][0-9]*)$/

/** The lowest number a rule may name, mirroring `FirewallOptions.IsUsablePort`. */
const MIN_PORT = 1

/**
 * The highest number a rule may name. Zero is excluded at the other end for a reason worth knowing:
 * it is the proto3 default of every port field on the agent contract, so it is what "nobody set
 * this" looks like by the time it reaches the wire.
 */
const MAX_PORT = 65535

const { t } = useI18n()

/** The port the operator typed, held as text so a half-typed number is not silently coerced. */
const port: Ref<string> = ref('')

/** The transport protocol the rule applies to. */
const protocol: Ref<FirewallProtocol> = ref('tcp')

/**
 * The source range the rule is scoped to, starting at "everything".
 *
 * The default is the value that admits every source, because that is what opening a port ordinarily
 * means and typing it out every time is friction with nothing behind it. Narrowing it is the deliberate
 * act, and it is the one that raises the lockout confirmation.
 */
const sourceCidr: Ref<string> = ref(ANY_IPV4_SOURCE)

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The two protocols the agent contract knows, labelled by this SPA's own chrome. */
const protocolOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: 'tcp', label: t('firewall.protocols.tcp') },
    { value: 'udp', label: t('firewall.protocols.udp') },
  ]
})

/** Validation message for the port, or `null`. */
const portError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (port.value.length === 0) {
    return t('firewall.rules.form.errors.portRequired')
  }
  const parsed = Number(port.value)
  return !DECIMAL.test(port.value) || parsed < MIN_PORT || parsed > MAX_PORT
    ? t('firewall.rules.form.errors.portInvalid')
    : null
})

/** Validation message for the source range, or `null`. */
const sourceError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (sourceCidr.value.length === 0) {
    return t('firewall.rules.form.errors.sourceRequired')
  }
  return isUsableCidr(sourceCidr.value) ? null : t('firewall.rules.form.errors.sourceInvalid')
})

/** Whether every field currently passes the client's own mirror of the panel's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return portError.value === null && sourceError.value === null
})

/**
 * Narrows the picker's plain string back onto the two protocols the contract knows.
 *
 * `UiSelect` speaks `string`, as a generic primitive must; this component knows the option list it
 * gave it, so the narrowing happens here rather than by asserting the type at the binding.
 * @param value The value the picker reported.
 * @returns Nothing; sets the protocol synchronously.
 */
const onProtocolChange = (value: string): void => {
  protocol.value = value === 'udp' ? 'udp' : 'tcp'
}

/**
 * Validates, and emits only when the rule is one the panel has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value || props.submitting) {
    return
  }
  emit('submit', { port: Number(port.value), protocol: protocol.value, sourceCidr: sourceCidr.value })
}

/**
 * Empties the port and returns the source to "everything", so the next rule is typed into a clean
 * form rather than into one still showing the last one's values.
 *
 * The protocol is deliberately kept: opening several TCP ports in a row is the common case.
 *
 * Exposed for the page to call once the panel has accepted a change. The page cannot clear the
 * fields itself — they live here, and so does `submitted`, which has to be cleared with them or an
 * emptied form would immediately turn red.
 * @returns Nothing.
 */
const reset = (): void => {
  port.value = ''
  sourceCidr.value = ANY_IPV4_SOURCE
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-3">
        <UiInput
          v-model="port"
          :label="t('firewall.rules.form.fields.port')"
          :placeholder="t('firewall.rules.form.placeholders.port')"
          :error="portError"
          required
        />
        <UiSelect
          :model-value="protocol"
          :label="t('firewall.rules.form.fields.protocol')"
          :options="protocolOptions"
          @update:model-value="onProtocolChange"
        />
        <UiInput
          v-model="sourceCidr"
          :label="t('firewall.rules.form.fields.sourceCidr')"
          :placeholder="t('firewall.rules.form.placeholders.sourceCidr')"
          :error="sourceError"
          required
        />
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('firewall.rules.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">
          {{ t('firewall.rules.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </div>
</template>
