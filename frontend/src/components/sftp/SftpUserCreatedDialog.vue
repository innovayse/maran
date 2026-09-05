<script setup lang="ts">
/**
 * Shows an SFTP password for the only time it will ever be shown.
 *
 * Both of the panel's two password-bearing answers land here — the one that created the login and
 * the one that reset it — because the screen's problem is identical for both: the value exists in
 * this component's props and in no store, no cache, no log and no column anywhere in the product.
 * When the dialog closes, the last copy is gone.
 *
 * So the dialog is built around that fact rather than around the happy news:
 * - the warning is the first thing in the body, not a footnote under the value;
 * - the password is rendered as selectable text as well as being copyable, because a clipboard
 *   write can silently fail in a locked-down browser and a value nobody can select is a value
 *   nobody can save;
 * - the operator cannot dismiss it by accident (`:dismissible="false"`): neither a mis-aimed
 *   click beside the panel nor a reflexive Escape closes it, because either would destroy the
 *   credential outright. It closes through its own Done button and the header's close control,
 *   both of which are deliberate acts;
 * - and it names the recovery, so an operator who does lose it knows the one path back rather
 *   than opening a support ticket that cannot be answered.
 *
 * The prefixed login is shown, never the bare name: the host holds `<account>_<name>` in
 * `/etc/passwd`, and somebody who types the short form into an SFTP client simply cannot log in.
 * The value comes from the panel's own response — this component never assembles a prefixed name
 * from an account and a suffix.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'

/** Props accepted by {@link SftpUserCreatedDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page, which holds the credential. */
  open: boolean
  /** The system login the password belongs to, prefixed exactly as the host holds it. */
  fullName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
}>()

/** Events emitted by {@link SftpUserCreatedDialog}. */
const emit = defineEmits<{
  /** Fired when the operator is finished with the credential; the page then forgets it. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/**
 * Whether the password has been copied to the clipboard during this showing.
 *
 * Reported to the operator because a copy button that looks the same before and after a click
 * invites a second click, and a second click is how somebody closes the dialog believing they
 * copied when the first attempt threw.
 */
const copied: Ref<boolean> = ref(false)

/**
 * Copies the password to the clipboard.
 *
 * A failure is swallowed on purpose and leaves {@link copied} false: the clipboard is blocked
 * outright in some browser configurations, and the honest answer there is the unchanged button
 * beside a password the operator can still select by hand — not an error banner claiming the
 * panel did something wrong.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyPassword = async (): Promise<void> => {
  try {
    await navigator.clipboard.writeText(props.password)
    copied.value = true
  } catch {
    copied.value = false
  }
}

/**
 * Asks the page to forget the credential and close.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  emit('close')
}

// A second credential must not open under the first one's "Copied" state: that would tell the
// operator a value they have never copied is already on their clipboard.
watch(
  (): boolean => {
    return props.open
  },
  (): void => {
    copied.value = false
  },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('sftp.credential.title')"
    :close-label="t('sftp.credential.close')"
    :dismissible="false"
    @close="close"
  >
    <!-- The loud tone is deliberate. The panel has two, and this is the one an operator does not
         scroll past: the sentence under it is the only warning they will get before the value
         stops existing. -->
    <UiAlert variant="error" class="mb-4">{{ t('sftp.credential.warning') }}</UiAlert>

    <dl class="flex flex-col gap-3">
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('sftp.credential.loginLabel') }}
        </dt>
        <dd class="mt-0.5 font-mono text-base break-all text-text-primary">{{ fullName }}</dd>
      </div>
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('sftp.credential.passwordLabel') }}
        </dt>
        <!-- Selectable text, not a masked field: the clipboard may be unavailable, and the only
             other way to keep this value is to read or select it. -->
        <dd
          data-testid="sftp-password"
          class="mt-0.5 rounded-lg border border-border-subtle bg-surface-2 px-3 py-2 font-mono text-base break-all text-text-primary"
        >
          {{ password }}
        </dd>
      </div>
    </dl>

    <p class="mt-3 text-sm text-text-muted">{{ t('sftp.credential.resetHint') }}</p>

    <template #footer>
      <UiButton variant="secondary" @click="copyPassword">
        <UiIcon name="copy" size="sm" />
        {{ copied ? t('sftp.credential.copied') : t('sftp.credential.copy') }}
      </UiButton>
      <UiButton @click="close">{{ t('sftp.credential.done') }}</UiButton>
    </template>
  </UiModal>
</template>
