<script setup lang="ts">
/**
 * The export form: the account picker and the button, with the one client-side rule the panel has,
 * that an account must be named. Dumb by the usual contract: props in, emits out. It reports a chosen
 * account and the page decides what to do with it.
 *
 * The button says what the wait will be like. Exporting takes a backup of the account first and the
 * panel does not answer until it is done, so on a large account the form sits submitting for minutes;
 * a control that only greyed out would read as a broken page.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'

/** Props accepted by {@link AccountExportForm}. */
const props = defineProps<{
  /** The account picker's state, read by the page from its store; the form only renders it. */
  accountPicker: AccountPickerState
  /** Whether the export is already being made, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link AccountExportForm}. */
const emit = defineEmits<{
  /** Fired only when an account has been chosen; carries its identity. */
  (e: 'submit', accountId: string): void
  /** Fired when the picker needs the accounts matching what was typed into it. */
  (e: 'searchAccounts', term: string): void
}>()

const { t } = useI18n()

/** The account to export. */
const accountId: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  return submitted.value && accountId.value.length === 0 ? t('accountMove.export.form.accountRequired') : null
})

/**
 * Emits the chosen account, once one is chosen.
 * @returns Nothing; emits synchronously.
 */
const submit = (): void => {
  submitted.value = true
  if (accountId.value.length > 0 && !props.submitting) {
    emit('submit', accountId.value)
  }
}
</script>

<template>
  <UiCard>
    <UiForm class="flex flex-col gap-4" @submit="submit">
      <div class="max-w-md">
        <UiCombobox
          v-model="accountId"
          v-bind="accountPicker"
          :label="t('accountMove.export.form.account')"
          :placeholder="t('accountMove.export.form.accountPlaceholder')"
          :error="accountError"
          required
          @search="emit('searchAccounts', $event)"
        />
      </div>
      <div class="flex flex-wrap items-center justify-between gap-x-6 gap-y-3">
        <p class="min-w-0 flex-1 basis-64 text-sm text-text-muted">{{ t('accountMove.export.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">
          {{ submitting ? t('accountMove.export.form.submitting') : t('accountMove.export.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
