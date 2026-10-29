<script setup lang="ts">
/**
 * Shows a database password for the only time it will ever be shown.
 *
 * Both of the panel's two password-bearing answers land here — the one that created the database
 * and the one that reset it — because the screen's problem is identical for both: the value exists
 * in this component's props and in no store, no cache, no log and no column anywhere in the
 * product. When the dialog closes, the last copy is gone.
 *
 * So the dialog is built around that fact rather than around the happy news:
 * - the warning is the first thing in the body, in amber, not a footnote under the value;
 * - the password is rendered as selectable text as well as being copyable, because a clipboard
 *   write can silently fail in a locked-down browser and a value nobody can select is a value
 *   nobody can save;
 * - the operator cannot dismiss it by accident (`:dismissible="false"`): neither a mis-aimed
 *   click beside the panel nor a reflexive Escape closes it, because either would destroy the
 *   credential outright. It closes through its own Done button and the header's close control,
 *   both of which are deliberate acts;
 * - the control that copies keeps its accessible name ("Copy password"), and the value is never
 *   written to the console or to storage — it lives in this component's props and its markup only;
 * - and it names the recovery, so an operator who does lose it knows the one path back rather
 *   than opening a support ticket that cannot be answered.
 *
 * The prefixed names are shown, never the bare ones: the login is `<account>_<name>` in MySQL, and
 * somebody who copies the short form and pastes it into a client is told the database does not
 * exist. The values come from the panel's own response — this component never assembles a
 * prefixed name from an account and a suffix.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'

/** Props accepted by {@link DatabaseCreatedDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page, which holds the credential. */
  open: boolean
  /** The fully-qualified database the login opens, or `null` when the panel named no database. */
  databaseFullName: string | null
  /** The fully-qualified MySQL user the password belongs to. */
  dbUserName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
}>()

/** Events emitted by {@link DatabaseCreatedDialog}. */
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
    :title="t('databases.credential.title')"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="close"
  >
    <!-- Amber, and first in the body: this sentence is the only warning there is before the value
         stops existing. It is a warning rather than an error because nothing has failed — the
         database was made — and red is kept for the things that did go wrong. -->
    <UiAlert variant="warning" class="mb-4">{{ t('databases.credential.warning') }}</UiAlert>

    <!-- The kit's description list owns the label/value markup, so these labels match every other
         detail screen. The raised block is only the frame around it. The password spans both columns
         because it is the longest value and the one the operator must be able to read whole. -->
    <UiDescriptionList class="rounded-control border border-border-subtle bg-surface-2 p-4">
      <UiDescriptionItem v-if="databaseFullName !== null" :term="t('databases.credential.databaseLabel')" mono>
        <span class="break-all">{{ databaseFullName }}</span>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('databases.credential.loginLabel')" mono>
        <span class="break-all">{{ dbUserName }}</span>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('databases.credential.passwordLabel')" mono class="sm:col-span-2">
        <!-- Selectable text, not a masked field: the clipboard may be unavailable, and the only
             other way to keep this value is to read or select it. `select-all` makes one click
             select the whole value, which is what somebody copying it by hand needs. It sits on
             the page colour inside the raised block so the value reads as the thing to take. The
             test id is on this span so it holds exactly the password text and nothing else. -->
        <span
          data-testid="database-password"
          class="block rounded-control border border-border-strong bg-page px-3 py-2.5 text-base font-medium break-all text-text-primary select-all"
        >{{ password }}</span>
      </UiDescriptionItem>
    </UiDescriptionList>

    <p class="mt-4 text-sm text-text-muted">{{ t('databases.credential.resetHint') }}</p>

    <!-- A name change on a button is not announced by itself, so the copy result is also spoken
         from a status region that is in the page before it has anything to say. -->
    <span class="sr-only" role="status">{{ copied ? t('databases.credential.copied') : '' }}</span>

    <template #footer>
      <UiButton variant="secondary" @click="copyPassword">
        <UiIcon name="copy" size="sm" />
        {{ copied ? t('databases.credential.copied') : t('databases.credential.copy') }}
      </UiButton>
      <UiButton @click="close">{{ t('databases.credential.done') }}</UiButton>
    </template>
  </UiModal>
</template>
