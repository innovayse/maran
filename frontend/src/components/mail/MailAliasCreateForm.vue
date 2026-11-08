<script setup lang="ts">
/**
 * The create-an-alias form: the domain picker, what the alias is (an alias, a forwarder or a
 * catch-all), the part of the address before the `@` (not asked for a catch-all, which takes what
 * no other address claims) and the destinations, one per line. The owning account is never asked
 * for — it is the chosen domain's own.
 *
 * The client-side checks are only the ones the panel states in its choices (the most destinations);
 * whether an entry is a legal address, an outside one, or a loop is the server's to decide, and its
 * already-localized refusal is what the person reads (rules/vue.md "Forms").
 *
 * **It is its own panel.** The form draws a `UiCard` with a section heading. Dumb by the usual
 * contract: props in, emits out. It never touches a store or the API layer.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import type { MailDomainPickerState } from '../../composables/useMailDomainChoice'
import { parseMailTargets } from '../../utils/mailTargets'
import type { CreateMailAliasRequest, MailAliasKind } from '../../types/mailAlias'
import type { MailChoice, MailLimits } from '../../types/mailChoice'

/** Props accepted by {@link MailAliasCreateForm}. */
const props = defineProps<{
  /** The domain picker's state: the page of domains matching its search. */
  domainPicker: MailDomainPickerState
  /** The kinds of alias the panel offers, already labelled in the reader's language. */
  kinds: readonly MailChoice[]
  /** The bounds the panel states for the form, or `null` while they have not arrived. */
  limits: MailLimits | null
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link MailAliasCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateMailAliasRequest): void
  /** Fired when the domain picker needs the domains matching what was typed into it. */
  (e: 'searchDomains', term: string): void
}>()

const { t } = useI18n()

/** The domain the alias lives in. */
const mailDomainId: Ref<string> = ref('')

/** What the alias does, as the wire spells it; starts at the first kind the panel offers. */
const kind: Ref<string> = ref('')

/** The part of the address before the `@`. */
const localPart: Ref<string> = ref('')

/** The destinations as typed, one per line. */
const targets: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The kind choices as the select lists them. */
const kindOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.kinds.map((choice) => {
    return { value: choice.value, label: choice.label }
  })
})

/** Whether the chosen kind is a catch-all, which has no local part to ask for. */
const isCatchAll: ComputedRef<boolean> = computed(() => {
  return kind.value === 'catchAll'
})

/** Validation message for the domain picker, or `null`. */
const domainError: ComputedRef<string | null> = computed(() => {
  return submitted.value && mailDomainId.value === '' ? t('mail.aliases.form.errors.domainRequired') : null
})

/** Validation message for the local part, or `null`. */
const localPartError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || isCatchAll.value) {
    return null
  }
  return localPart.value.trim() === '' ? t('mail.aliases.form.errors.localPartRequired') : null
})

/** Validation message for the destinations, or `null`. */
const targetsError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const entries = parseMailTargets(targets.value)
  if (entries.length === 0) {
    return t('mail.aliases.form.errors.targetsRequired')
  }
  const most = props.limits?.maximumAliasTargets
  return most !== undefined && entries.length > most ? t('mail.aliases.form.errors.targetsTooMany', { n: most }) : null
})

/** Whether every field currently passes the client's own rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return domainError.value === null && localPartError.value === null && targetsError.value === null && kind.value !== ''
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  emit('submit', {
    mailDomainId: mailDomainId.value,
    // The kind is one of the panel's own choices, so the wire value is already a member of the union.
    kind: kind.value as MailAliasKind,
    localPart: isCatchAll.value ? '' : localPart.value.trim(),
    targets: parseMailTargets(targets.value),
  })
}

/**
 * Returns every field to its starting value and forgets that a submit was attempted, so the next
 * alias starts from a clean form.
 * @returns Nothing.
 */
const reset = (): void => {
  mailDomainId.value = ''
  localPart.value = ''
  targets.value = ''
  kind.value = props.kinds[0]?.value ?? ''
  submitted.value = false
}

defineExpose({ reset })

// The kinds are the panel's, so the first one can only be chosen once they have arrived.
watch(
  (): readonly MailChoice[] => {
    return props.kinds
  },
  (kinds: readonly MailChoice[]): void => {
    if (kind.value === '' && kinds.length > 0) {
      kind.value = kinds[0]?.value ?? ''
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('mail.aliases.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiCombobox
          v-model="mailDomainId"
          v-bind="domainPicker"
          :label="t('mail.aliases.form.fields.domain')"
          :placeholder="t('mail.aliases.form.placeholders.domain')"
          :error="domainError"
          required
          @search="emit('searchDomains', $event)"
        />
        <UiSelect v-model="kind" :label="t('mail.aliases.form.fields.kind')" :options="kindOptions" required />
        <UiInput
          v-if="!isCatchAll"
          v-model="localPart"
          mono
          :label="t('mail.aliases.form.fields.localPart')"
          :placeholder="t('mail.aliases.form.placeholders.localPart')"
          :error="localPartError"
          autocomplete="off"
          required
        />
        <UiTextarea
          v-model="targets"
          mono
          class="sm:col-span-2"
          :rows="3"
          :label="t('mail.aliases.form.fields.targets')"
          :placeholder="t('mail.aliases.form.placeholders.targets')"
          :error="targetsError"
          required
        />
      </div>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('mail.aliases.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('mail.aliases.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
