<script setup lang="ts">
/**
 * The panel's yes/no confirmation: a modal that names what is about to happen,
 * states its consequence, and takes one deliberate answer.
 *
 * It exists because rules/vue.md requires a destructive action to be confirmed
 * in a dialog, and because the panel had grown a hand-written copy of the same
 * question on every screen that asks one — a sentence and a "Yes, do it" pair
 * spliced into the page flow, differing slightly each time it was copied. A
 * confirmation is one behaviour, so it is one component; a screen supplies the
 * words and performs the action, and nothing else.
 *
 * **It is not the typed confirmation.** `AccountDeleteDialog` and the restore
 * dialog ask the operator to type the account's own name, because what they do
 * cannot be undone by the control beside them. This dialog is the lighter
 * gesture, and the two are deliberately not merged: raising every question to a
 * typed one trains the operator to type without reading, and lowering the typed
 * ones to a click gives away the only protection they have.
 *
 * **Focus does not start on the confirm button.** `UiModal` moves focus to the
 * first focusable element in the panel, which is the header's close control —
 * so a reflexive Enter on a dialog that appeared unexpectedly DISMISSES it. A
 * confirmation whose default answer is "yes" is weaker than the inline sentence
 * it replaced, not stronger.
 *
 * **The wait is shown here, not on the page behind.** While `acting` is true the
 * dialog stays open over a spinner naming the operation, its buttons are gone,
 * and it refuses Escape and backdrop dismissal: the request is already with the
 * server, so a dialog that vanished mid-flight would leave the operator with
 * neither an outcome nor a reason to wait. The caller closes it when the request
 * has settled, and any refusal is rendered by the page underneath.
 */
import UiButton from './UiButton.vue'
import UiModal from './UiModal.vue'
import UiSpinner from './UiSpinner.vue'

/** Props accepted by {@link UiConfirm}. */
const props = withDefaults(
  defineProps<{
    /** Whether the dialog is shown; owned by the caller. */
    open: boolean
    /** What is being confirmed, already translated — the dialog's accessible name. Names the subject ("Delete site shop.example.com"), never a bare "Confirm". */
    title: string
    /** The consequence the operator is being asked to weigh, already translated. */
    question: string
    /** Label of the button that performs the action, already translated. */
    confirmLabel: string
    /** Label of the button that abandons it, already translated. */
    cancelLabel: string
    /** Accessible name for the header's close control, already translated. */
    closeLabel: string
    /** Whether the confirmed request is in flight; the dialog then shows only `actingLabel`. */
    acting?: boolean
    /** What is happening while `acting` is true, already translated. */
    actingLabel: string
    /** Whether the action removes or interrupts something, which is how its button is weighted. */
    destructive?: boolean
  }>(),
  { acting: false, destructive: true },
)

/** Events emitted by {@link UiConfirm}. */
const emit = defineEmits<{
  /** Fired when the operator declines — cancel, the close control, Escape, or the backdrop. */
  (e: 'close'): void
  /** Fired when the operator confirms. The CALLER performs the action and owns `acting`. */
  (e: 'confirm'): void
}>()

/**
 * Declines the question, unless the answer is already with the server.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  if (props.acting) {
    return
  }
  emit('close')
}

/**
 * Answers the question. Guarded against a second press while the first is in
 * flight, which on a row action would send the same destructive request twice.
 * @returns Nothing; emits synchronously.
 */
const submit = (): void => {
  if (props.acting) {
    return
  }
  emit('confirm')
}
</script>

<template>
  <UiModal
    :open="open"
    :title="title"
    :close-label="closeLabel"
    :dismissible="!acting"
    @close="close"
  >
    <UiSpinner v-if="acting" :label="actingLabel" />
    <!-- Carries a test id because the consequence text is the whole point of this dialog: an
         end-to-end check has to be able to assert the WHOLE sentence, and a substring match on the
         dialog would also pass on copy that had quietly lost a clause. -->
    <p v-else data-testid="confirm-message">{{ question }}</p>

    <template #footer>
      <!-- Cancel first, and confirm last, so the destructive answer is the one
           the hand and the Tab key reach last. -->
      <template v-if="!acting">
        <UiButton variant="secondary" @click="close">{{ cancelLabel }}</UiButton>
        <UiButton :variant="destructive ? 'destructive' : 'primary'" @click="submit">
          {{ confirmLabel }}
        </UiButton>
      </template>
    </template>
  </UiModal>
</template>
