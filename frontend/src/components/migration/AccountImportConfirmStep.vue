<script setup lang="ts">
/**
 * The typed confirmation that starts an import. The administrator retypes the account's name and the
 * sending server's key fingerprint, so a click is never enough to create an account from a package: it
 * must be the account they meant, from the server they checked.
 *
 * **The UI is advice, the panel is law.** The start control turns on only when both retypes match and
 * the dry run found no conflict, which saves a round trip and slows a hasty click; the panel compares
 * both confirmations itself, ordinally and case-sensitively, and refuses a start with a conflict. The
 * component therefore sends what was TYPED, so the panel judges the administrator's input, not ours,
 * and errs permissive on whitespace around a pasted fingerprint.
 *
 * Props in, events out: the page owns the store and the busy state.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { AccountImportAction } from '../../types/accountMove'

/** Props accepted by {@link AccountImportConfirmStep}. */
const props = defineProps<{
  /** The account's name, which the administrator retypes. */
  accountName: string
  /** The key fingerprint the administrator confirmed at upload, which they retype. */
  keyFingerprint: string
  /** Whether the judged plan's dry run holds no conflict. */
  canStart: boolean
  /** The action in flight, or `null`. */
  busy: AccountImportAction | null
}>()

/** Events emitted by {@link AccountImportConfirmStep}. */
const emit = defineEmits<{
  /** The administrator confirmed; carries what they typed. */
  (e: 'start', confirmAccountName: string, confirmKeyFingerprint: string): void
  /** The administrator asked to discard the package instead. */
  (e: 'discard'): void
}>()

const { t } = useI18n()

/** The account name as retyped. */
const typedName: Ref<string> = ref('')

/** The fingerprint as retyped or pasted. */
const typedFingerprint: Ref<string> = ref('')

/** Whether the retyped name is exactly the account's name. */
const nameMatches: ComputedRef<boolean> = computed(() => {
  return typedName.value === props.accountName
})

/** Whether the retyped fingerprint is exactly the confirmed one (surrounding whitespace aside). */
const fingerprintMatches: ComputedRef<boolean> = computed(() => {
  return typedFingerprint.value.trim() === props.keyFingerprint
})

/** The name field's error: shown once something was typed that is not the account's name. */
const nameError: ComputedRef<string | null> = computed(() => {
  return typedName.value === '' || nameMatches.value ? null : t('accountMove.import.confirm.mismatch')
})

/** The fingerprint field's error: shown once something was typed that is not the confirmed fingerprint. */
const fingerprintError: ComputedRef<string | null> = computed(() => {
  return typedFingerprint.value.trim() === '' || fingerprintMatches.value ? null : t('accountMove.import.confirm.mismatch')
})

/** Whether the start control may turn on. */
const ready: ComputedRef<boolean> = computed(() => {
  return props.canStart && nameMatches.value && fingerprintMatches.value && props.busy === null
})

/**
 * Sends the confirmation, only when both retypes match and the report holds no conflict.
 * @returns Nothing; the event is emitted synchronously.
 */
const start = (): void => {
  if (ready.value) {
    emit('start', typedName.value, typedFingerprint.value.trim())
  }
}

/**
 * Asks to discard the package.
 * @returns Nothing; the event is emitted synchronously.
 */
const discard = (): void => {
  emit('discard')
}
</script>

<template>
  <UiForm class="flex min-w-0 flex-col gap-4" data-testid="import-confirm-step" @submit="start">
    <UiSectionHeading :title="t('accountMove.import.confirm.title')" :subtitle="t('accountMove.import.confirm.intro')" />

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="flex flex-col gap-1.5">
        <UiInput
          v-model="typedName"
          :label="t('accountMove.import.confirm.nameLabel')"
          :error="nameError"
          mono
          autocomplete="off"
        />
        <p class="text-sm text-text-muted">{{ t('accountMove.import.confirm.nameHint', { name: accountName }) }}</p>
      </div>
      <div class="flex flex-col gap-1.5">
        <UiInput
          v-model="typedFingerprint"
          :label="t('accountMove.import.confirm.fingerprintLabel')"
          :error="fingerprintError"
          mono
          autocomplete="off"
        />
        <p class="text-sm text-text-muted">{{ t('accountMove.import.confirm.fingerprintHint') }}</p>
      </div>
    </div>

    <div class="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
      <UiButton variant="ghost" :disabled="busy !== null" @click="discard">{{ t('accountMove.import.confirm.discard') }}</UiButton>
      <UiButton type="submit" :disabled="!ready">
        {{ busy === 'start' ? t('accountMove.import.confirm.starting') : t('accountMove.import.confirm.start') }}
      </UiButton>
    </div>
  </UiForm>
</template>
