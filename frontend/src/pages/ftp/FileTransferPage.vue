<script setup lang="ts">
/**
 * The File transfer screen: every login that can write into an account's files, whichever daemon
 * accepts it, plus the administrator's view of the FTPS daemon itself.
 *
 * It is one screen for both protocols, because SFTP and FTPS logins are DIFFERENT system users on
 * one host. A customer holds both kinds, a suspension has
 * to cover both, and an operator asking "who can write here" is asking one question — so this is one
 * table with a protocol column, not two screens that each look complete.
 *
 * **The composition is the SPA's, not the server's.** Two modules, two endpoints, two stores, and no
 * merged endpoint is asked for: the panel keeps its module boundary and this layer does what it is
 * for. A failure on one half leaves the other half rendered, because they are two requests.
 *
 * **What this screen does NOT show, said here rather than left as a blank.** The agent reports how
 * many logins share an account's uid that the panel did not create — the part of a suspension
 * attestation that says what was NOT covered. That count exists on the wire and in
 * `AccountSuspensionStateDto`, and it reaches no response THIS screen reads: `ListSftpUsersQuery`
 * and `ListFtpUsersQuery` consult no host user database, so there is nothing here to render it from
 * and inventing a number would be worse than the gap. The gap is reported to the owner rather than
 * papered over (rules/vue.md: "Report the gap and add the endpoint — do not paper over it in the
 * UI").
 *
 * The count itself is NOT lost, and the invariant it exists for — an absent number must never read
 * as zero — is held one layer down rather than here. `UnmanagedLoginPolicy.Describe` words it as
 * three distinct sentences: a count, an explicit "no login outside the panel's own", and an explicit
 * "the host did not say, so that number is unknown rather than zero". `SuspendAccountCommandHandler`
 * puts that sentence on the task through `ITaskRecorder.ReportAsync`, it lands in `PanelTask.log`,
 * and `TaskLivePane` renders that log verbatim on the Tasks screen. So the invariant holds on the
 * surface that has the datum — the suspension, and only the suspension: the reactivate handler's
 * attestation leaves the clause out on purpose, because a login the panel never locked is not one a
 * resumption failed to restore. What is missing HERE is a per-account figure of this screen's own,
 * which would need a field on an account-scoped response that does not exist yet.
 *
 * **A refusal is not a failure, and this screen does not pretend to tell them apart.** When the agent
 * refuses because the account is busy, the wire carries no code that distinguishes it from a host
 * failure. The backend's already-localized sentence is rendered verbatim and nothing here parses it
 * (rules/vue.md forbids machine text on screen, and a fake distinction would be worse than none).
 *
 * **What the screen is made of, top to bottom (the redesign's panel language).** The page heading,
 * then whatever needs attention — a failed FTPS-server call or a rejected create, as notices — then
 * the administrator's FTPS server panel, then the create form in its own card, then the logins in
 * theirs. Panels are 16px apart on the content panel; nothing floats bare. The list card swaps its
 * body between a spinner, the backend's own error, and the table, and an account with no logins at
 * all gets the empty state instead of a card around nothing. The table scrolls inside its own
 * container, so the page never scrolls sideways at 390px, in any of the three languages.
 *
 * Renders a `<section>`, not a `<main>` — the single landmark lives in the layout.
 */
import {
  computed,
  onBeforeUnmount,
  onMounted,
  ref,
  useTemplateRef,
  type ComputedRef,
  type Ref,
} from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import FileTransferLoginCreateForm from '../../components/ftp/FileTransferLoginCreateForm.vue'
import FtpUserCreatedDialog from '../../components/ftp/FtpUserCreatedDialog.vue'
import FtpsServerPanel from '../../components/ftp/FtpsServerPanel.vue'
import SftpUserCreatedDialog from '../../components/sftp/SftpUserCreatedDialog.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useAuthStore } from '../../stores/auth'
import { useFirewallStore } from '../../stores/firewall'
import { useFtpUsersStore } from '../../stores/ftpUsers'
import { useFtpsServerStore } from '../../stores/ftpsServer'
import { useLocaleStore } from '../../stores/locale'
import { useSftpStore } from '../../stores/sftp'
import { fileTransferProtocolOf } from '../../utils/fileTransferProtocolOf'
import { formatDate } from '../../utils/formatDate'
import type { EnableFtpsRequest } from '../../types/ftpsStatus'
import type { FirewallRule } from '../../types/firewall'
import type {
  CreateFileTransferLoginRequest,
  FileTransferLogin,
  FileTransferProtocol,
} from '../../types/fileTransferLogin'

/** Which action a row is waiting for confirmation on. */
type PendingAction = 'resetPassword' | 'remove'

const { t } = useI18n()
const accountsStore = useAccountsStore()
const authStore = useAuthStore()
const firewallStore = useFirewallStore()
const ftpUsersStore = useFtpUsersStore()
const ftpsServerStore = useFtpsServerStore()
const localeStore = useLocaleStore()
const sftpStore = useSftpStore()

/** The create form, so an accepted create can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The login whose action is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/** Which protocol that login belongs to, so the right module is asked to carry the action out. */
const pendingProtocol: Ref<FileTransferProtocol> = ref('sftp')

/** Which action that login is waiting on. */
const pendingAction: Ref<PendingAction> = ref('remove')

/**
 * Whether this caller may see the administrator's half of the screen.
 *
 * `role !== 'customer'` rather than `role === 'admin'`, matching the one other place the SPA branches
 * on the role: a role added above administrator must not silently lose the panel. It is presentation
 * only — every endpoint behind it enforces its own policy.
 */
const isAdmin: ComputedRef<boolean> = computed(() => {
  return authStore.user !== null && authStore.user.role !== 'customer'
})

/**
 * Every login on the host, whichever daemon holds it, newest first.
 *
 * The protocol is resolved per row rather than assumed per source: an FTPS row carries the server's
 * own token, and an SFTP row falls back to what that endpoint answers by construction only while the
 * `Sftp` module has no protocol member to send.
 */
const logins: ComputedRef<FileTransferLogin[]> = computed(() => {
  const merged: FileTransferLogin[] = [
    ...sftpStore.sftpUsers.map((user): FileTransferLogin => {
      return {
        id: user.id,
        accountId: user.accountId,
        fullName: user.fullName,
        protocol: fileTransferProtocolOf(user.protocol, 'sftp'),
        createdAt: user.createdAt,
      }
    }),
    ...ftpUsersStore.ftpUsers.map((user): FileTransferLogin => {
      return {
        id: user.id,
        accountId: user.accountId,
        fullName: user.fullName,
        protocol: fileTransferProtocolOf(user.protocol, 'ftps'),
        createdAt: user.createdAt,
      }
    }),
  ]

  // Sorted by the login the host holds rather than by arrival: the two lists arrive as two
  // responses, so an unsorted table would reorder itself depending on which request answered first.
  return merged.sort((left, right) => {
    return left.fullName.localeCompare(right.fullName)
  })
})

/** True while either list request is in flight. */
const loading: ComputedRef<boolean> = computed(() => {
  return sftpStore.loading || ftpUsersStore.loading
})

/** True while any mutation on either module is in flight. */
const acting: ComputedRef<boolean> = computed(() => {
  return sftpStore.acting || ftpUsersStore.acting
})

/**
 * The backend's own message for the most recent failure on either half, or `null`.
 *
 * Rendered verbatim. A refusal because the account is busy and a genuine host failure arrive
 * indistinguishable on the wire, and this screen shows what is true rather than inventing the
 * distinction.
 */
const errorMessage: ComputedRef<string | null> = computed(() => {
  return sftpStore.errorMessage ?? ftpUsersStore.errorMessage
})

/** The backend's own message for the most recent rejected create, or `null`. */
const createErrorMessage: ComputedRef<string | null> = computed(() => {
  return sftpStore.createErrorMessage ?? ftpUsersStore.createErrorMessage
})

/** Whether both halves answered and neither reported a login. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return sftpStore.isLoaded && logins.value.length === 0
})

/**
 * Which body the logins area shows. One answer rather than a chain of `v-if`s in the template, so
 * the precedence — loading, then the backend's error, then the empty state, then the table — is
 * written down once and cannot drift between the card and the empty state.
 */
const listView: ComputedRef<'loading' | 'error' | 'empty' | 'table' | 'none'> = computed(() => {
  if (loading.value) {
    return 'loading'
  }
  if (errorMessage.value !== null) {
    return 'error'
  }
  if (isEmpty.value) {
    return 'empty'
  }
  return logins.value.length > 0 ? 'table' : 'none'
})

/**
 * What this caller has been told about the FTPS daemon.
 *
 * Three answers, because there are three situations: the panel said it is on, the panel said it is
 * off, and the panel did not say — the status is administrator-only, so a customer is genuinely not
 * told. The form errs permissive on the third.
 */
const ftpsAvailability: ComputedRef<'available' | 'off' | 'unknown'> = computed(() => {
  if (!ftpsServerStore.isDisclosed || ftpsServerStore.status === null) {
    return 'unknown'
  }
  return ftpsServerStore.status.enabled ? 'available' : 'off'
})

/** The name of the row whose action is awaiting an answer, so the dialog says what it asks about. */
const pendingName: ComputedRef<string> = computed(() => {
  const pending = logins.value.find((item) => {
    return item.id === pendingId.value
  })
  return pending?.fullName ?? ''
})

/** The confirmation's title, naming the row and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return pendingAction.value === 'remove'
    ? t('ftp.list.confirmRemoveTitle', { name: pendingName.value })
    : t('ftp.list.confirmResetPasswordTitle', { name: pendingName.value })
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationText: ComputedRef<string> = computed(() => {
  return pendingAction.value === 'remove'
    ? t('ftp.list.confirmRemove')
    : t('ftp.list.confirmResetPassword')
})

/**
 * Names the account a login belongs to, for a column that would otherwise print a GUID.
 * @param id The owning account's identity, as the login row reports it.
 * @returns The account's own short name, or a placeholder when the accounts list has none.
 */
const accountName = (id: string): string => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === id
  })
  return owner?.name ?? t('common.emptyValue')
}

/**
 * The label a row's protocol is rendered with.
 * @param protocol The protocol the row resolved to.
 * @returns The protocol's name, or the placeholder for one this bundle does not recognise.
 */
const protocolLabel = (protocol: FileTransferProtocol): string => {
  if (protocol === 'sftp') {
    return t('ftp.protocols.sftp')
  }
  return protocol === 'ftps' ? t('ftp.protocols.ftps') : t('common.emptyValue')
}

/**
 * Loads everything the screen is made of. The administrator's two extra reads are only attempted for
 * an administrator: both endpoints answer 403 otherwise, and asking anyway would spend two requests
 * to learn what the role already says.
 * @returns Resolves once every request has settled.
 */
const refresh = async (): Promise<void> => {
  const reads: Promise<void>[] = [sftpStore.load(), ftpUsersStore.load(), accountsStore.load()]

  if (isAdmin.value) {
    reads.push(ftpsServerStore.load(), firewallStore.load())
  }

  await Promise.all(reads)
}

/**
 * Sends a create to whichever module the chosen protocol belongs to. The protocol itself is never
 * sent: it decides the endpoint, and neither request body carries it.
 * @param request The account, the name and the protocol the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateFileTransferLoginRequest): Promise<void> => {
  const accepted =
    request.protocol === 'ftps'
      ? await ftpUsersStore.create({ accountId: request.accountId, name: request.name })
      : await sftpStore.create({ accountId: request.accountId, name: request.name })

  if (accepted) {
    form.value?.reset()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param login The row the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (login: FileTransferLogin, action: PendingAction): void => {
  pendingId.value = login.id
  pendingProtocol.value = login.protocol
  pendingAction.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Carries out the confirmed action against the module that owns the row.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const id = pendingId.value
  const isFtps = pendingProtocol.value === 'ftps'

  if (pendingAction.value === 'remove') {
    await (isFtps ? ftpUsersStore.remove(id) : sftpStore.remove(id))
  } else {
    await (isFtps ? ftpUsersStore.resetPassword(id) : sftpStore.resetPassword(id))
  }

  // Closed after the request settles rather than before it is sent: the dialog holds the spinner
  // that tells the operator the panel is working.
  pendingId.value = ''
}

/**
 * Forgets whichever credential is on screen, ending the only showing it gets.
 * @returns Nothing.
 */
const dismissCredentials = (): void => {
  sftpStore.dismissCredential()
  ftpUsersStore.dismissCredential()
}

/**
 * Turns the FTPS daemon on and re-reads the firewall, so the reachability warning reflects the
 * moment after the change rather than the one before it.
 * @param request The hostname and passive address the panel form collected.
 * @returns Resolves once both requests have settled.
 */
const enableFtps = async (request: EnableFtpsRequest): Promise<void> => {
  await ftpsServerStore.enable(request)
  await firewallStore.load()
}

/**
 * Turns the FTPS daemon off. The firewall is deliberately left alone; the panel's second warning is
 * what makes the ports it left open visible.
 * @returns Resolves once the request has settled.
 */
const disableFtps = async (): Promise<void> => {
  await ftpsServerStore.disable()
}

/**
 * Installs the rules the daemon needs, then re-reads them so the warning answers to the firewall
 * rather than to what was asked for.
 * @param rules The rules the panel derived from the backend-supplied ports.
 * @returns Resolves once the change and the re-read have settled.
 */
const openFtpsPorts = async (rules: FirewallRule[]): Promise<void> => {
  await firewallStore.allowPorts(rules)
}

/**
 * Removes the rules a stopped daemon left behind.
 * @param rules The rules the panel derived from the backend-supplied ports.
 * @returns Resolves once the change has settled.
 */
const closeFtpsPorts = async (rules: FirewallRule[]): Promise<void> => {
  await firewallStore.denyPorts(rules)
}

onMounted(refresh)

// Neither credential must survive a navigation. The stores outlive this page.
onBeforeUnmount(dismissCredentials)
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('ftp.list.eyebrow')"
      :title="t('ftp.list.heading')"
      :subtitle="t('ftp.list.subtitle')"
      :note="t('ftp.list.prefixNote')"
    />

    <!-- What needs attention goes first: a failure on either request is the thing an operator must
         read before the rest of the screen means anything. The backend's own sentence, verbatim. -->
    <UiAlert v-if="ftpsServerStore.errorMessage !== null" variant="error">
      {{ ftpsServerStore.errorMessage }}
    </UiAlert>

    <UiAlert v-if="createErrorMessage !== null" variant="error">
      {{ createErrorMessage }}
    </UiAlert>

    <FtpsServerPanel
      v-if="isAdmin && ftpsServerStore.isDisclosed && ftpsServerStore.status !== null"
      :status="ftpsServerStore.status"
      :rules="firewallStore.rules"
      :acting="ftpsServerStore.acting || firewallStore.acting"
      @enable="enableFtps"
      @disable="disableFtps"
      @open-ports="openFtpsPorts"
      @close-ports="closeFtpsPorts"
    />

    <FileTransferLoginCreateForm
      ref="form"
      :accounts="accountsStore.accounts"
      :submitting="sftpStore.creating || ftpUsersStore.creating"
      :ftps-availability="ftpsAvailability"
      @submit="create"
    />

    <UiEmptyState
      v-if="listView === 'empty'"
      :title="t('ftp.list.emptyTitle')"
      :description="t('ftp.list.emptyDescription')"
    >
      <template #icon><UiIcon name="folderKey" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else-if="listView !== 'none'">
      <UiSectionHeading class="mb-4" :title="t('ftp.list.sectionHeading')" />

      <UiSpinner v-if="listView === 'loading'" :label="t('ftp.list.loading')" />

      <UiAlert v-else-if="listView === 'error'" variant="error">{{ errorMessage }}</UiAlert>

      <UiTable v-else :caption="t('ftp.list.tableCaption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('ftp.list.columns.fullName') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('ftp.list.columns.protocol') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('ftp.list.columns.account') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('ftp.list.columns.createdAt') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('common.actions') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="login in logins" :key="`${login.protocol}-${login.id}`">
          <!-- The prefixed login, because it is the one a client accepts. -->
          <UiTableCell mono class="font-medium whitespace-nowrap">{{ login.fullName }}</UiTableCell>
          <UiTableCell>
            <!-- The whole point of the merged screen: two daemons, two system users, one account. -->
            <UiBadge :variant="login.protocol === 'ftps' ? 'info' : 'neutral'">
              {{ protocolLabel(login.protocol) }}
            </UiBadge>
          </UiTableCell>
          <UiTableCell tone="secondary" class="whitespace-nowrap">{{ accountName(login.accountId) }}</UiTableCell>
          <UiTableCell mono tone="muted" class="whitespace-nowrap">
            {{ formatDate(login.createdAt, localeStore.current) }}
          </UiTableCell>
          <UiTableCell align="end">
            <div class="flex flex-wrap items-center justify-end gap-2">
              <UiDropdown
                :label="t('common.actions')"
                :aria-label="t('ftp.list.rowActions', { name: login.fullName })"
                align="end"
                variant="bare"
                :chevron="false"
              >
                <template #trigger>
                  <UiIcon name="ellipsis" size="md" />
                </template>
                <UiDropdownItem @select="ask(login, 'resetPassword')">
                  {{ t('ftp.list.resetPassword') }}
                </UiDropdownItem>
                <UiDropdownItem destructive @select="ask(login, 'remove')">
                  {{ t('ftp.list.remove') }}
                </UiDropdownItem>
              </UiDropdown>
            </div>
          </UiTableCell>
        </UiTableRow>
      </UiTable>
    </UiCard>

    <UiConfirm
      :open="pendingId.length > 0"
      :title="confirmationTitle"
      :question="confirmationText"
      :confirm-label="t('common.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="acting"
      :acting-label="t('ftp.list.working')"
      :destructive="pendingAction === 'remove'"
      @close="cancel"
      @confirm="confirm"
    />

    <!-- Two dialogs and not one: an FTPS credential is useless without the host and the port, and an
         SFTP one carries neither. Only one of them can be open, because only one create or reset can
         have just answered. -->
    <SftpUserCreatedDialog
      :open="sftpStore.revealedCredential !== null"
      :full-name="sftpStore.revealedCredential?.fullName ?? ''"
      :password="sftpStore.revealedCredential?.password ?? ''"
      @close="dismissCredentials"
    />

    <FtpUserCreatedDialog
      :open="ftpUsersStore.revealedCredential !== null"
      :full-name="ftpUsersStore.revealedCredential?.fullName ?? ''"
      :password="ftpUsersStore.revealedCredential?.password ?? ''"
      :hostname="ftpsServerStore.status?.hostname ?? null"
      :control-port="ftpsServerStore.status?.controlPort ?? null"
      :certificate-is-self-signed="ftpsServerStore.status?.certificateIsSelfSigned ?? false"
      @close="dismissCredentials"
    />
  </section>
</template>
