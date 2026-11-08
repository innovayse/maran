<script setup lang="ts">
/**
 * Sets a new password on one mailbox, from the mailboxes list. The new password is typed by the
 * owner and never shown back: the panel keeps a hash of it and returns nothing, so there is no
 * credential to display and no "shown once" notice to heed. Setting it does not end a session that
 * is already open, and the dialog says so, because a person changing a password after a leak will
 * assume it does.
 *
 * The only client check is the shortest length the panel states in its choices; strength is the
 * server's to judge, and its already-localized refusal is shown inside the dialog.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useMailboxesStore } from '../../stores/mailboxes'
import type { MailLimits } from '../../types/mailChoice'
import type { Mailbox } from '../../types/mailbox'

/** Props accepted by {@link MailboxPasswordDialog}; the page mounts it only while a password is being set. */
const props = defineProps<{
  /** The mailbox being re-credentialled. */
  mailbox: Mailbox
  /** The bounds the panel states for the form, or `null` while they have not arrived. */
  limits: MailLimits | null
}>()

/** Events emitted by {@link MailboxPasswordDialog}. */
const emit = defineEmits<{
  /** The dialog should close without a change. */
  (e: 'close'): void
  /** The panel stored the new password. */
  (e: 'saved'): void
}>()

const { t } = useI18n()
const store = useMailboxesStore()

/** The new password as typed. */
const password: Ref<string> = ref('')

/** Whether a save has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the password, or `null`. */
const passwordError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (password.value === '') {
    return t('mail.mailboxes.form.errors.passwordRequired')
  }
  const shortest = props.limits?.minimumPasswordLength ?? 0
  return password.value.length < shortest ? t('mail.mailboxes.form.errors.passwordShort', { n: shortest }) : null
})

/**
 * Closes the dialog unless a request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!store.acting) {
    emit('close')
  }
}

/**
 * Sends the new password, and reports success to the page; a refusal stays on screen.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (passwordError.value !== null) {
    return
  }
  if (await store.changePassword(props.mailbox.id, { password: password.value })) {
    emit('saved')
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('mail.mailboxes.password.title', { name: mailbox.address })"
    :close-label="t('common.close')"
    :dismissible="!store.acting"
    @close="close"
  >
    <UiSpinner v-if="store.acting" :label="t('mail.working')" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
        <p class="text-sm text-text-muted">{{ t('mail.mailboxes.password.hint') }}</p>
        <UiPasswordInput
          v-model="password"
          :label="t('mail.mailboxes.password.field')"
          :error="passwordError"
          autocomplete="new-password"
          generate
          required
        />
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.acting">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="submit">{{ t('mail.mailboxes.password.submit') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
