<script setup lang="ts">
/**
 * Shows an FTPS password for the only time it will ever be shown, together with the connection facts
 * that make it usable.
 *
 * Same shape as the SFTP dialog beside it and deliberately not the same component: an FTPS login is
 * useless without the host name and the control port, and an SFTP one is not, so the two dialogs
 * show different sets of facts. Copying the shape and not the code is what the plan asked for.
 *
 * **The host name is the point of this dialog.** There is one server certificate for the whole
 * panel host, so a customer connects to the PANEL's hostname and never to their own domain: a client
 * pointed at `their-site.example` gets a certificate-name mismatch and either refuses or teaches the
 * customer to click through a warning. The value therefore comes from the panel's persisted status
 * and from nowhere else — never `window.location.host`, never the account's primary domain. When the
 * panel did not disclose the status (it is administrator-only) the fields are rendered as UNKNOWN
 * with a sentence saying to ask the administrator, because a guessed host name is worse than no host
 * name.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'

/** Props accepted by {@link FtpUserCreatedDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page, which holds the credential. */
  open: boolean
  /** The system login the password belongs to, prefixed exactly as the host holds it. */
  fullName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
  /**
   * The host name the customer must connect to, as the panel persisted it, or `null` when the panel
   * did not disclose it to this caller. Never composed here.
   */
  hostname: string | null
  /**
   * The control port the panel configured the daemon with, or `null` when it was not disclosed.
   * Backend-supplied like every other number on these screens.
   */
  controlPort: number | null
  /** Whether the server's certificate is the agent's self-signed placeholder. */
  certificateIsSelfSigned: boolean
}>()

/** Events emitted by {@link FtpUserCreatedDialog}. */
const emit = defineEmits<{
  /** Fired when the operator is finished with the credential; the page then forgets it. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/**
 * Whether the password has been copied to the clipboard during this showing.
 *
 * Reported because a copy button that looks the same before and after a click invites a second
 * click, and a second click is how somebody closes the dialog believing they copied.
 */
const copied: Ref<boolean> = ref(false)

/** The host to connect to, or the panel's placeholder for a value it did not disclose. */
const hostText: ComputedRef<string> = computed(() => {
  return props.hostname ?? t('common.emptyValue')
})

/** The control port, or the panel's placeholder for a value it did not disclose. */
const portText: ComputedRef<string> = computed(() => {
  return props.controlPort === null ? t('common.emptyValue') : String(props.controlPort)
})

/** Whether either connection fact is missing, so the dialog says why rather than showing two dashes. */
const isConnectionUndisclosed: ComputedRef<boolean> = computed(() => {
  return props.hostname === null || props.controlPort === null
})

/**
 * Copies the password to the clipboard.
 *
 * A failure is swallowed on purpose and leaves {@link copied} false: the clipboard is blocked
 * outright in some browser configurations, and the honest answer there is the unchanged button
 * beside a password the operator can still select by hand.
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

// A second credential must not open under the first one's "Copied" state.
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
    :title="t('ftp.credential.title')"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="close"
  >
    <UiAlert variant="error" class="mb-4">{{ t('ftp.credential.warning') }}</UiAlert>

    <dl class="flex flex-col gap-3">
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('ftp.credential.hostLabel') }}
        </dt>
        <dd data-testid="ftp-credential-host" class="mt-0.5 font-mono text-base break-all text-text-primary">
          {{ hostText }}
        </dd>
      </div>
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('ftp.credential.portLabel') }}
        </dt>
        <dd data-testid="ftp-credential-port" class="mt-0.5 font-mono text-base text-text-primary">
          {{ portText }}
        </dd>
      </div>
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('ftp.credential.protocolLabel') }}
        </dt>
        <dd class="mt-0.5 text-base text-text-primary">{{ t('ftp.credential.protocolValue') }}</dd>
      </div>
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('ftp.credential.loginLabel') }}
        </dt>
        <dd class="mt-0.5 font-mono text-base break-all text-text-primary">{{ fullName }}</dd>
      </div>
      <div>
        <dt class="text-xs font-medium tracking-wide text-text-muted uppercase">
          {{ t('ftp.credential.passwordLabel') }}
        </dt>
        <!-- Selectable text, not a masked field: the clipboard may be unavailable, and the only
             other way to keep this value is to read or select it. -->
        <dd
          data-testid="ftp-password"
          class="mt-0.5 rounded-lg border border-border-subtle bg-surface-2 px-3 py-2 font-mono text-base break-all text-text-primary"
        >
          {{ password }}
        </dd>
      </div>
    </dl>

    <!-- Not decoration. One certificate serves the whole host, so this name is the only one it
         matches; any other name is a mismatch the customer cannot tell from an attack. -->
    <p class="mt-3 text-sm text-text-muted">{{ t('ftp.credential.hostNote') }}</p>

    <p v-if="isConnectionUndisclosed" class="mt-2 text-sm text-text-muted">
      {{ t('ftp.credential.hostUndisclosed') }}
    </p>

    <p v-if="certificateIsSelfSigned" class="mt-2 text-sm text-text-muted">
      {{ t('ftp.credential.selfSignedNote') }}
    </p>

    <p class="mt-3 text-sm text-text-muted">{{ t('ftp.credential.resetHint') }}</p>

    <template #footer>
      <UiButton variant="secondary" @click="copyPassword">
        <UiIcon name="copy" size="sm" />
        {{ copied ? t('common.copied') : t('common.copyPassword') }}
      </UiButton>
      <UiButton @click="close">{{ t('common.done') }}</UiButton>
    </template>
  </UiModal>
</template>
