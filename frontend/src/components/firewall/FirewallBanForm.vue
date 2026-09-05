<script setup lang="ts">
/**
 * The form that bans an address by hand — the only way a ban whose reason reads "Manual" ever comes
 * into being, since every other row on the table was placed by the brute-force detector.
 *
 * **The address's FORM is deliberately not checked here.** The panel parses it in exactly one place
 * (`IpAddressNormalizer`, which is also what maps `::ffff:a.b.c.d` onto plain IPv4), and a second
 * format rule in the browser would be a check that masks the one doing the work: it would refuse
 * spellings the panel accepts, and it would keep passing if the real parse were ever broken. So
 * this form checks that something was typed, and the panel's own already-localized refusal is what
 * the operator reads when the address is not one.
 *
 * The duration IS checked, because the panel checks it: an empty field means "until somebody lifts
 * it", and a zero has to be refused before it becomes a permanent ban nobody asked for.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import type { BanAddressRequest } from '../../types/firewall'

/** Props accepted by {@link FirewallBanForm}. */
const props = defineProps<{
  /** Whether a ban request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link FirewallBanForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the ban to place. */
  (e: 'submit', request: BanAddressRequest): void
}>()

/** A decimal number with no leading zero — the only spelling of a duration accepted. */
const DECIMAL = /^(0|[1-9][0-9]*)$/

/** The shortest ban a duration may ask for. */
const MIN_DURATION_MINUTES = 1

/**
 * The longest ban a duration may ask for: a year, in minutes, mirroring the panel's own limit. Not
 * a technical ceiling but the point past which "temporary" has stopped meaning anything — an
 * operator who wants longer wants a permanent ban, and says so by leaving the field empty.
 */
const MAX_DURATION_MINUTES = 525600

const { t } = useI18n()

/** The address to ban, exactly as it was typed. */
const address: Ref<string> = ref('')

/** How long the ban lasts in minutes, or empty for one that lasts until somebody lifts it. */
const durationMinutes: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the address, or `null`. */
const addressError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return address.value.length === 0 ? t('firewall.bans.form.errors.addressRequired') : null
})

/** Validation message for the duration, or `null`. */
const durationError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || durationMinutes.value.length === 0) {
    return null
  }
  const parsed = Number(durationMinutes.value)
  return !DECIMAL.test(durationMinutes.value) ||
    parsed < MIN_DURATION_MINUTES ||
    parsed > MAX_DURATION_MINUTES
    ? t('firewall.bans.form.errors.durationInvalid')
    : null
})

/** Whether every field currently passes the client's own mirror of the panel's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return addressError.value === null && durationError.value === null
})

/**
 * Validates, and emits only when the ban is one the panel has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value || props.submitting) {
    return
  }
  emit('submit', {
    address: address.value,
    // An empty field is `null` and not `0`: the contract spells "until somebody lifts it" as an
    // absent duration, so a permanent ban stays something the operator chose rather than something
    // a zero produced.
    durationMinutes: durationMinutes.value.length === 0 ? null : Number(durationMinutes.value),
  })
}

/**
 * Empties both fields and forgets that a submit was attempted.
 *
 * Exposed for the page to call once the panel has placed the ban: the fields live here, and so does
 * `submitted`, which has to be cleared with them or an emptied form would immediately turn red.
 * @returns Nothing.
 */
const reset = (): void => {
  address.value = ''
  durationMinutes.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-2">
        <UiInput
          v-model="address"
          :label="t('firewall.bans.form.fields.address')"
          :placeholder="t('firewall.bans.form.placeholders.address')"
          :error="addressError"
          required
        />
        <UiInput
          v-model="durationMinutes"
          :label="t('firewall.bans.form.fields.durationMinutes')"
          :placeholder="t('firewall.bans.form.placeholders.durationMinutes')"
          :error="durationError"
        />
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('firewall.bans.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">{{ t('firewall.bans.form.submit') }}</UiButton>
      </div>
    </UiForm>
  </div>
</template>
