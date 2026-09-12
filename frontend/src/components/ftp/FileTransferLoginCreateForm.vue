<script setup lang="ts">
/**
 * The create-a-file-transfer-login form: the account picker, the protocol choice, the login name,
 * and the client-side mirror of the server's validator.
 *
 * The protocol choice is the part that is new. Both modules take the same two fields, so one form
 * serves both and the choice decides which endpoint the page calls — it is never sent in a body.
 *
 * **A disabled FTPS daemon is a normal state, not an error.** The daemon ships installed, stopped
 * and bound to nothing until an administrator turns it on, so this form renders that as a choice
 * that is present and not choosable, with a sentence saying why. It does not hide the choice (a
 * missing option reads as a panel that has no FTPS at all) and it does not show an alert (nothing
 * has gone wrong).
 *
 * **When the panel did not tell this caller whether FTPS is on, the choice stays open.** The status
 * endpoint is administrator-only, so a customer cannot learn the daemon's state; blocking the choice
 * on an unknown would take FTPS away from every customer on a working server. The server is the
 * authority either way and its already-localized refusal is what the operator reads — a client-side
 * check errs permissive on purpose (rules/security.md: enforcement is the backend's).
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer, and it
 * refuses to emit until its own rules pass.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiRadioGroup, { type RadioOption } from '../ui/UiRadioGroup.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { Account } from '../../types/account'
import type { CreateFileTransferLoginRequest } from '../../types/fileTransferLogin'

/**
 * What this form has been told about the FTPS daemon, which is not the same question as whether it
 * is running.
 *
 * `unknown` is a member because it is a real answer and not a missing one: `GET /api/v1/ftps-server`
 * is administrator-only, so a customer's panel genuinely does not know. Folding it into `off` would
 * disable FTPS for every customer on a server where it works.
 */
type FtpsAvailability = 'available' | 'off' | 'unknown'

/**
 * The server's suffix alphabet, character for character: lowercase ASCII letters and digits, and
 * nothing else. Narrower than a legal Unix user name on purpose — the value becomes a `useradd`
 * argument and a path segment — and it excludes the underscore separator, so account `alice` cannot
 * ask for `bob_deploy` and be handed a login that reads as `bob`'s in `/etc/passwd`.
 *
 * This is advice that saves a round trip. The server re-validates it, and its already-localized
 * rejection is what the operator reads when the two disagree.
 */
const SUFFIX_PATTERN = /^[a-z0-9]+$/

/** The longest suffix the server accepts before the account prefix is applied. */
const MAX_SUFFIX_LENGTH = 30

/** Props accepted by {@link FileTransferLoginCreateForm}. */
const props = defineProps<{
  /** The accounts a login may be created for, as the panel reported them. */
  accounts: readonly Account[]
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
  /** What the panel has told this caller about the FTPS daemon. */
  ftpsAvailability: FtpsAvailability
}>()

/** Events emitted by {@link FileTransferLoginCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateFileTransferLoginRequest): void
}>()

const { t } = useI18n()

/** The account that will own the new login. */
const accountId: Ref<string> = ref('')

/** Which daemon the login is created for. SFTP is the default because it is always available. */
const protocol: Ref<string> = ref('sftp')

/** The login name the customer chose, without the account prefix. */
const name: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The accounts the picker offers, as the panel reported them. */
const accountOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.accounts.map((account) => {
    return { value: account.id, label: `${account.name} · ${account.primaryDomain}` }
  })
})

/** Whether the panel has said, in so many words, that the FTPS daemon is turned off. */
const isFtpsOff: ComputedRef<boolean> = computed(() => {
  return props.ftpsAvailability === 'off'
})

/** The two protocols, with FTPS present-but-unchoosable when the panel says the daemon is off. */
const protocolOptions: ComputedRef<RadioOption[]> = computed(() => {
  return [
    { value: 'sftp', label: t('ftp.protocols.sftp') },
    { value: 'ftps', label: t('ftp.protocols.ftps'), disabled: isFtpsOff.value },
  ]
})

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return accountId.value.length === 0 ? t('ftp.form.errors.accountRequired') : null
})

/** Validation message for the login name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('ftp.form.errors.nameRequired')
  }
  return name.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(name.value)
    ? t('ftp.form.errors.nameInvalid')
    : null
})

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return accountError.value === null && nameError.value === null
})

/**
 * Records the protocol the operator picked.
 * @param value The chosen protocol's machine value.
 * @returns Nothing.
 */
const chooseProtocol = (value: string): void => {
  protocol.value = value
}

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  emit('submit', {
    accountId: accountId.value,
    name: name.value,
    // Narrowed here rather than typed on the ref: `UiRadioGroup`'s model is a plain string, and the
    // only two values this form ever puts in it are the two it offers.
    protocol: protocol.value === 'ftps' ? 'ftps' : 'sftp',
  })
}

/**
 * Empties the login name and forgets that a submit was attempted, so the next login is typed into a
 * clean form rather than into one still showing the last one's value.
 *
 * The account and the protocol are deliberately kept: creating several logins of one kind for one
 * customer is the common case.
 * @returns Nothing.
 */
const reset = (): void => {
  name.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-2">
        <UiSelect
          v-model="accountId"
          :label="t('ftp.form.fields.accountId')"
          :placeholder="t('ftp.form.placeholders.accountId')"
          :options="accountOptions"
          :error="accountError"
          required
        />
        <UiInput
          v-model="name"
          :label="t('ftp.form.fields.name')"
          :placeholder="t('ftp.form.placeholders.name')"
          :error="nameError"
          required
        />
        <div class="sm:col-span-2">
          <UiRadioGroup
            :model-value="protocol"
            :legend="t('ftp.form.fields.protocol')"
            :options="protocolOptions"
            required
            @update:model-value="chooseProtocol"
          />
          <!-- Stated as a fact about the server, not as a failure: the daemon ships installed and
               stopped, and an administrator turning it on is a deliberate act with firewall
               consequences. -->
          <p v-if="isFtpsOff" data-testid="ftps-off-note" class="mt-2 text-sm text-text-muted">
            {{ t('ftp.form.ftpsOffNote') }}
          </p>
        </div>
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('ftp.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">{{ t('ftp.form.submit') }}</UiButton>
      </div>
    </UiForm>
  </div>
</template>
