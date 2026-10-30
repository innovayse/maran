<script setup lang="ts">
/**
 * Changes one hosting account's owner address, from the account's own page.
 *
 * **What the address is for is said before it is changed.** It is not only a contact field: the
 * account's login is mailed at it — its invitation and every password reset — so the panel moves
 * the login's address together with the account's (`ChangeAccountOwnerEmailCommandHandler`). An
 * address another login already holds is refused by the panel, and its sentence is shown here
 * verbatim.
 *
 * The client check mirrors the server's validator only loosely (a non-empty address with one `@`
 * and no spaces, and the same length cap); the server is the authority and its message is what the
 * operator reads on a refusal (rules/vue.md "Forms").
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useAccountsStore } from '../../stores/accounts'
import type { AccountDetail } from '../../types/account'

/** Props accepted by {@link AccountOwnerEmailDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** The account being changed. */
  account: AccountDetail
}>()

/** Events emitted by the dialog. */
const emit = defineEmits<{
  /**
   * Fired when the dialog should close without a change; the page owns the open state.
   * @param e The event name.
   */
  (e: 'close'): void
  /**
   * Fired once the panel has accepted the change.
   * @param e The event name.
   */
  (e: 'saved'): void
}>()

/** A loose shape check: one `@`, something on each side, no whitespace. The server is the authority. */
const OWNER_EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** The narrower of the two columns the address is written to, as the server's validator caps it. */
const MAX_OWNER_EMAIL_LENGTH = 254

const { t } = useI18n()
const store = useAccountsStore()

/** The address being typed; set to the current one each time the dialog opens. */
const ownerEmail: Ref<string> = ref('')

/** Whether the operator has tried to save, after which field errors are shown. */
const submitted: Ref<boolean> = ref(false)

/** Why the address cannot be saved, or `null` when it can. */
const ownerEmailError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = ownerEmail.value.trim()
  if (value.length === 0) {
    return t('accounts.form.errors.ownerEmailRequired')
  }
  if (value.length > MAX_OWNER_EMAIL_LENGTH || !OWNER_EMAIL_PATTERN.test(value)) {
    return t('accounts.form.errors.ownerEmailInvalid')
  }
  return value === props.account.ownerEmail ? t('accounts.edit.ownerEmailUnchanged') : null
})

/**
 * Closes the dialog unless a request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!store.editing) {
    emit('close')
  }
}

/**
 * Sends the change, and reports success to the page; a refusal stays on screen.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (ownerEmailError.value !== null) {
    return
  }
  if (await store.changeOwnerEmail(props.account.id, ownerEmail.value.trim())) {
    emit('saved')
  }
}

// Every opening starts from the account's current address and a clean slate.
watch(
  (): boolean => {
    return props.open
  },
  (open: boolean): void => {
    if (!open) {
      return
    }
    ownerEmail.value = props.account.ownerEmail ?? ''
    submitted.value = false
    store.clearEditError()
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('accounts.edit.ownerEmailTitle', { account: account.name })"
    :close-label="t('common.close')"
    :dismissible="!store.editing"
    @close="close"
  >
    <UiSpinner v-if="store.editing" :label="t('accounts.edit.saving')" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.editErrorMessage !== null" variant="error">
          {{ store.editErrorMessage }}
        </UiAlert>
        <p class="text-sm text-text-muted">{{ t('accounts.edit.ownerEmailHint') }}</p>
        <UiInput
          v-model="ownerEmail"
          :label="t('accounts.form.fields.ownerEmail')"
          :error="ownerEmailError"
          required
          autocomplete="email"
          inputmode="email"
          :placeholder="t('accounts.form.placeholders.ownerEmail')"
        />
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.editing">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="submit">{{ t('accounts.edit.ownerEmailSubmit') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
