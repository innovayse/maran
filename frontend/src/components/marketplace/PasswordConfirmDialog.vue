<script setup lang="ts">
/**
 * The marketplace's one password dialog: a modal that states what is about to happen, optionally
 * shows more content above the field, and asks the administrator for their current password again
 * before the panel changes anything. The switch, an install, an update, a removal and a rollback all
 * use it, so the re-entry is one behaviour in one place.
 *
 * The password is never kept: the field is emptied every time the dialog opens and again as soon as
 * it is submitted, so a refused attempt starts from an empty field and nothing lingers in the page
 * state. Whether the password is right is the panel's to say (a refusal arrives as `errorMessage`,
 * the panel's own sentence); this dialog only refuses an empty field. `canSubmit` lets a caller add
 * a further condition (the install acknowledgement). While `acting` the dialog shows a spinner and
 * cannot be dismissed, because the request is already with the server. Escape and the close button
 * close it, and `UiModal` returns focus to the control that opened it.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiSpinner from '../ui/UiSpinner.vue'

/** Props accepted by {@link PasswordConfirmDialog}. */
const props = withDefaults(
  defineProps<{
    /** Whether the dialog is shown; owned by the caller. */
    open: boolean
    /** The dialog's heading and accessible name, already translated. */
    title: string
    /** One sentence saying what the password confirms, already translated; empty draws none. */
    question?: string
    /** The submit button's label, already translated; it names the action. */
    submitLabel: string
    /** Whether the request is in flight. */
    acting?: boolean
    /** What is happening while `acting`, already translated. */
    actingLabel: string
    /** The panel's sentence for the last refusal, or `null`. */
    errorMessage?: string | null
    /** A further condition for submitting beyond a non-empty password. */
    canSubmit?: boolean
    /** The submit button's weight: destructive for a removal. */
    destructive?: boolean
  }>(),
  { question: '', acting: false, errorMessage: null, canSubmit: true, destructive: false },
)

/** Events emitted by {@link PasswordConfirmDialog}. */
const emit = defineEmits<{
  /** The administrator declined: cancel, the close control or Escape. */
  (e: 'close'): void
  /** The administrator submitted; carries the typed password. */
  (e: 'submit', password: string): void
}>()

const { t } = useI18n()

/** The password as typed; emptied on open and on submit. */
const password: Ref<string> = ref('')

/** Whether the form may be sent: something typed, the caller's condition met, nothing in flight. */
const ready: ComputedRef<boolean> = computed(() => {
  return password.value !== '' && props.canSubmit && !props.acting
})

/**
 * Closes the dialog unless the request is already with the server.
 * @returns Nothing.
 */
const close = (): void => {
  if (!props.acting) {
    emit('close')
  }
}

/**
 * Sends the password to the caller and forgets it here.
 * @returns Nothing.
 */
const submit = (): void => {
  if (!ready.value) {
    return
  }
  const typed = password.value
  password.value = ''
  emit('submit', typed)
}

// A dialog never opens with a password already in it.
watch(
  (): boolean => {
    return props.open
  },
  (isOpen: boolean): void => {
    if (isOpen) {
      password.value = ''
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiModal :open="open" :title="title" :close-label="t('marketplace.close')" :dismissible="!acting" @close="close">
    <UiSpinner v-if="acting" :label="actingLabel" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="errorMessage !== null" variant="error" data-testid="marketplace-dialog-error">
          {{ errorMessage }}
        </UiAlert>
        <p v-if="question !== ''">{{ question }}</p>
        <slot />
        <UiPasswordInput
          v-model="password"
          :label="t('marketplace.password.label')"
          autocomplete="current-password"
          required
        />
        <p class="text-text-muted">{{ t('marketplace.password.hint') }}</p>
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!acting">
        <UiButton variant="secondary" @click="close">{{ t('marketplace.cancel') }}</UiButton>
        <UiButton :variant="destructive ? 'destructive' : 'primary'" :disabled="!ready" @click="submit">
          {{ submitLabel }}
        </UiButton>
      </template>
    </template>
  </UiModal>
</template>
