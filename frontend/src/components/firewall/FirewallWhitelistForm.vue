<script setup lang="ts">
/**
 * The form that exempts a range from the automatic bans: the range itself, and a note saying what
 * it is.
 *
 * The note is not decoration. A whitelist row is the panel being told never to ban somebody, and
 * six months later the only thing standing between an operator and an exemption nobody can account
 * for is what the person who added it wrote down. The panel accepts an empty note; this form asks
 * for one anyway, because the row it produces outlives the reason for it.
 *
 * The range is parsed before it is sent, mirroring the panel's own rule that a range carrying host
 * bits beyond its prefix is refused rather than masked: `203.0.113.7/24` exempts either one machine
 * or two hundred and fifty-six of them, and an exemption must not be wider than the person who
 * wrote it believes.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import { isUsableCidr } from '../../utils/isUsableCidr'
import type { AddWhitelistEntryRequest } from '../../types/firewall'

/** Props accepted by {@link FirewallWhitelistForm}. */
const props = defineProps<{
  /** Whether a whitelist change is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link FirewallWhitelistForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the exemption to add. */
  (e: 'submit', request: AddWhitelistEntryRequest): void
}>()

/** The longest note the panel's column holds. */
const MAX_NOTE_LENGTH = 200

const { t } = useI18n()

/** The range to exempt, in CIDR notation. */
const cidr: Ref<string> = ref('')

/** What the range is, in the administrator's own words. */
const note: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the range, or `null`. */
const cidrError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (cidr.value.length === 0) {
    return t('firewall.whitelist.form.errors.cidrRequired')
  }
  return isUsableCidr(cidr.value) ? null : t('firewall.whitelist.form.errors.cidrInvalid')
})

/** Validation message for the note, or `null`. */
const noteError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return note.value.length > MAX_NOTE_LENGTH ? t('firewall.whitelist.form.errors.noteTooLong') : null
})

/** Whether every field currently passes the client's own mirror of the panel's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return cidrError.value === null && noteError.value === null
})

/**
 * Validates, and emits only when the exemption is one the panel has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value || props.submitting) {
    return
  }
  emit('submit', { cidr: cidr.value, note: note.value })
}

/**
 * Empties both fields and forgets that a submit was attempted.
 *
 * Exposed for the page to call once the panel has added the row: the fields live here, and so does
 * `submitted`, which has to be cleared with them or an emptied form would immediately turn red.
 * @returns Nothing.
 */
const reset = (): void => {
  cidr.value = ''
  note.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-2">
        <UiInput
          v-model="cidr"
          :label="t('firewall.whitelist.form.fields.cidr')"
          :placeholder="t('firewall.whitelist.form.placeholders.cidr')"
          :error="cidrError"
          required
        />
        <UiInput
          v-model="note"
          :label="t('firewall.whitelist.form.fields.note')"
          :placeholder="t('firewall.whitelist.form.placeholders.note')"
          :error="noteError"
        />
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('firewall.whitelist.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">
          {{ t('firewall.whitelist.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </div>
</template>
