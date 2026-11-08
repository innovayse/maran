<script setup lang="ts">
/**
 * The create-a-database-user form: the account picker and the user's name, with the client-side
 * mirror of the server's validator. A user is created with no privileges and no password of the
 * caller's choosing: the panel mints the password, and privileges are granted afterwards from the
 * list, one database at a time.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer, and it
 * refuses to emit until its own rules pass. The server re-validates everything and its localized
 * rejection is what the operator reads when the two disagree.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { CreateDatabaseUserRequest } from '../../types/databaseUser'

/**
 * The server's suffix alphabet, character for character: lowercase ASCII letters and digits. The
 * name is interpolated into DDL that takes no placeholders, so nothing else may reach it. Advice
 * that saves a round trip; the server re-validates.
 */
const SUFFIX_PATTERN = /^[a-z0-9]+$/

/** The longest suffix the server accepts before the account prefix is applied. */
const MAX_SUFFIX_LENGTH = 30

/** Props accepted by {@link DatabaseUserCreateForm}. */
const props = defineProps<{
  /** The account picker's state, read by the page from its store. */
  accountPicker: AccountPickerState
  /** Whether a create request is in flight, which disables the submit control. */
  submitting: boolean
  /** The account every request is for when the caller has no choice (a customer's own), else `null`. */
  fixedAccountId: string | null
}>()

/** Events emitted by {@link DatabaseUserCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateDatabaseUserRequest): void
  /** Fired when the account picker needs the accounts matching what was typed into it. */
  (e: 'searchAccounts', term: string): void
}>()

const { t } = useI18n()

/** The account that will own the new user. */
const accountId: Ref<string> = ref('')

/** The user's name, without the account prefix. */
const nameSuffix: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || props.fixedAccountId !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('databases.users.form.errors.accountRequired') : null
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (nameSuffix.value.length === 0) {
    return t('databases.users.form.errors.nameRequired')
  }
  return nameSuffix.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(nameSuffix.value)
    ? t('databases.users.form.errors.nameInvalid')
    : null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (accountError.value !== null || nameError.value !== null) {
    return
  }
  emit('submit', { accountId: props.fixedAccountId ?? accountId.value, nameSuffix: nameSuffix.value })
}

/**
 * Returns every field to its starting value after the panel accepted a create, so a form still
 * showing what it just sent does not invite the same submit twice.
 * @returns Nothing.
 */
const reset = (): void => {
  accountId.value = ''
  nameSuffix.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <UiSectionHeading
        class="mb-4"
        :title="t('databases.users.form.heading')"
        :subtitle="t('databases.users.form.hint')"
      />
      <div class="grid gap-4 sm:grid-cols-2">
        <div v-if="fixedAccountId === null" class="min-w-0">
          <UiCombobox
            v-model="accountId"
            v-bind="accountPicker"
            :label="t('databases.users.form.fields.account')"
            :placeholder="t('databases.users.form.placeholders.account')"
            :error="accountError"
            required
            @search="emit('searchAccounts', $event)"
          />
        </div>
        <div class="min-w-0">
          <UiInput
            v-model="nameSuffix"
            :label="t('databases.users.form.fields.name')"
            :placeholder="t('databases.users.form.placeholders.name')"
            :error="nameError"
            required
          />
        </div>
      </div>
      <div class="mt-5 flex justify-end">
        <UiButton type="submit" :disabled="submitting">{{ t('databases.users.form.submit') }}</UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
