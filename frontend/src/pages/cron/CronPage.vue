<script setup lang="ts">
/**
 * Scheduled tasks screen: the account being looked at, the form that installs an entry, the
 * account's crontab preamble, the entries themselves, and what one entry's last run left behind.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. State comes exclusively from the cron store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * **The account is chosen on the screen rather than carried in the route**, because it is what
 * every one of the module's calls names: cron keeps no rows, so an entry id means nothing until it
 * is asked of one account's crontab. The list of accounts comes from the panel; nothing here is a
 * list written into the SPA.
 *
 * One page rather than a list plus a form page: an entry has no detail to open. What its last run
 * left behind is the only thing beyond the row, it costs a privileged read per entry, and it is
 * therefore a dialog opened on demand rather than a column nobody asked for.
 */
import { computed, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import CronEntryForm from '../../components/cron/CronEntryForm.vue'
import CronEntryOutputDialog from '../../components/cron/CronEntryOutputDialog.vue'
import CronEnvironmentEditor from '../../components/cron/CronEnvironmentEditor.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useCronStore } from '../../stores/cron'
import { formatCronExpression } from '../../utils/cronSchedule'
import type { CronEntry, CronSchedule } from '../../types/cronEntry'
import type { CronEnvironmentVariable } from '../../types/cronEnvironmentVariable'

const { t } = useI18n()
const store = useCronStore()
const accountsStore = useAccountsStore()

/** The form, so an accepted install can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The entry being rewritten, or `null` while the form is installing a new one. */
const editing: Ref<{ entryId: string; schedule: CronSchedule; command: string } | null> = ref(null)

/** The entry awaiting a removal confirmation, or the empty string when none is. */
const pendingRemovalId: Ref<string> = ref('')

/** The entry the removal confirmation is open for, or `null` when none is. */
const pendingEntry: ComputedRef<CronEntry | null> = computed(() => {
  return (
    store.entries.find((entry) => {
      return entry.entryId === pendingRemovalId.value
    }) ?? null
  )
})

/** The confirmation's accessible name, naming the command that would stop running. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingEntry.value
  return pending === null ? '' : t('cron.list.confirmRemoveTitle', { command: pending.command })
})

/** The accounts one crontab can be read for, as the select wants them. */
const accountOptions: ComputedRef<SelectOption[]> = computed(() => {
  return accountsStore.accounts.map((account) => {
    return { value: account.id, label: account.name }
  })
})

/**
 * Loads the accounts and points the screen at the first of them.
 *
 * The first rather than none, because a screen that opens on an empty picker looks broken; there is
 * nothing invented in it — the account came from the panel, and the crontab shown is that account's.
 * @returns Resolves once the accounts and the first account's crontab have settled.
 */
const start = async (): Promise<void> => {
  await accountsStore.load()
  const first = accountsStore.accounts[0]
  if (first !== undefined && store.accountId === '') {
    await store.selectAccount(first.id)
  }
}

/**
 * Points the screen at a different account.
 * @param id The account chosen.
 * @returns Resolves once that account's crontab has been read.
 */
const changeAccount = async (id: string): Promise<void> => {
  editing.value = null
  pendingRemovalId.value = ''
  await store.selectAccount(id)
}

/**
 * Writes the schedule and command the form produced — installing an entry, or rewriting one.
 * @param value The schedule and the command, already checked by the form against this panel's
 * mirrors of the module's validators.
 * @param value.schedule When the entry is to run.
 * @param value.command The command line, verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (value: { schedule: CronSchedule; command: string }): Promise<void> => {
  const current = editing.value
  const written =
    current === null
      ? await store.create(value.schedule, value.command)
      : await store.update(current.entryId, value.schedule, value.command)

  if (written) {
    editing.value = null
    form.value?.reset()
  }
}

/**
 * Opens the form on an existing entry.
 * @param entry The entry to rewrite.
 * @returns Nothing.
 */
const edit = (entry: CronEntry): void => {
  editing.value = { entryId: entry.entryId, schedule: entry.schedule, command: entry.command }
}

/**
 * Abandons an edit and returns the form to installing.
 * @returns Nothing.
 */
const cancelEdit = (): void => {
  editing.value = null
}

/**
 * Switches an entry on or off. The state is sent explicitly, never toggled server-side.
 * @param entry The entry acted on.
 * @returns Resolves once the request has settled.
 */
const setEnabled = async (entry: CronEntry): Promise<void> => {
  await store.setEnabled(entry.entryId, !entry.enabled)
}

/**
 * Asks for confirmation before removing an entry.
 * @param entryId The entry the operator acted on.
 * @returns Nothing.
 */
const askRemove = (entryId: string): void => {
  pendingRemovalId.value = entryId
}

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancelRemove = (): void => {
  pendingRemovalId.value = ''
}

/**
 * Carries out the confirmed removal.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  await store.remove(pendingRemovalId.value)

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pendingRemovalId.value = ''
}

/**
 * Opens the dialog showing what an entry's last run left behind.
 * @param entryId The entry to read.
 * @returns Resolves once the reading has settled.
 */
const showOutput = async (entryId: string): Promise<void> => {
  await store.openOutput(entryId)
}

/**
 * Closes the last-run dialog.
 * @returns Nothing.
 */
const closeOutput = (): void => {
  store.closeOutput()
}

/**
 * Writes the crontab preamble the editor produced.
 * @param variables The complete new set.
 * @returns Resolves once the attempt has settled.
 */
const saveEnvironment = async (variables: CronEnvironmentVariable[]): Promise<void> => {
  await store.saveEnvironment(variables)
}

/**
 * Writes one entry's schedule the way a crontab line does.
 * @param entry The entry to describe.
 * @returns The five fields, single-spaced.
 */
const scheduleText = (entry: CronEntry): string => {
  return formatCronExpression(entry.schedule)
}

onMounted(start)
</script>

<template>
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="t('cron.list.heading')" :subtitle="t('cron.list.subtitle')" />

    <UiSelect
      :model-value="store.accountId"
      :options="accountOptions"
      :label="t('cron.list.accountLabel')"
      :placeholder="t('cron.list.accountPlaceholder')"
      class="mb-4 max-w-sm"
      @update:model-value="changeAccount"
    />

    <UiAlert v-if="store.saveErrorMessage !== null" variant="error" class="mb-4">
      {{ store.saveErrorMessage }}
    </UiAlert>

    <UiCard class="mb-6">
      <UiSectionHeading
        class="mb-3"
        :title="editing === null ? t('cron.form.createHeading') : t('cron.form.editHeading')"
      />
      <CronEntryForm
        ref="form"
        :submitting="store.saving"
        :editing="editing"
        @submit="submit"
        @cancel="cancelEdit"
      />
    </UiCard>

    <UiCard class="mb-6">
      <UiSectionHeading class="mb-3" :title="t('cron.environment.heading')" />
      <UiAlert v-if="store.environmentErrorMessage !== null" variant="error" class="mb-3">
        {{ store.environmentErrorMessage }}
      </UiAlert>
      <CronEnvironmentEditor
        :variables="store.environment"
        :saving="store.savingEnvironment"
        @save="saveEnvironment"
      />
    </UiCard>

    <UiSpinner v-if="store.loading" :label="t('cron.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else-if="store.isEmpty"
      :title="t('cron.list.emptyTitle')"
      :description="t('cron.list.emptyDescription')"
    >
      <template #icon><UiIcon name="clock" size="lg" /></template>
    </UiEmptyState>

    <UiTable v-else-if="store.entries.length > 0" :caption="t('cron.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('cron.list.columns.schedule') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('cron.list.columns.command') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('cron.list.columns.state') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('common.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="entry in store.entries" :key="entry.entryId">
        <UiTableCell class="font-mono">{{ scheduleText(entry) }}</UiTableCell>
        <!-- The command is shown exactly as the customer wrote it. It can carry a credential, which
             is why the panel keeps it out of its logs and its audit journal — but it is their own
             text on their own screen, and masking it would leave them unable to read their job. -->
        <UiTableCell class="font-mono break-all">{{ entry.command }}</UiTableCell>
        <UiTableCell>
          <UiBadge :variant="entry.enabled ? 'success' : 'neutral'">
            {{ entry.enabled ? t('cron.list.enabled') : t('cron.list.disabled') }}
          </UiBadge>
        </UiTableCell>
        <UiTableCell>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('cron.list.rowActions', { command: entry.command })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem @select="setEnabled(entry)">
                {{ entry.enabled ? t('cron.list.disable') : t('cron.list.enable') }}
              </UiDropdownItem>
              <UiDropdownItem @select="edit(entry)">{{ t('cron.list.edit') }}</UiDropdownItem>
              <UiDropdownItem @select="showOutput(entry.entryId)">
                {{ t('cron.list.lastOutput') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="askRemove(entry.entryId)">
                {{ t('cron.list.remove') }}
              </UiDropdownItem>
            </UiDropdown>
          </div>
        </UiTableCell>
      </UiTableRow>
    </UiTable>

    <!-- One dialog for the whole table rather than one per row: only one removal can be awaiting
         an answer, and the menu that opened it is already gone by the time it appears. -->
    <UiConfirm
      :open="pendingRemovalId.length > 0"
      :title="confirmationTitle"
      :question="t('cron.list.confirmRemove')"
      :confirm-label="t('cron.list.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('cron.list.working')"
      @close="cancelRemove"
      @confirm="confirmRemove"
    />

    <!-- `:open` is bound to a value that really changes, and the dialog is NOT wrapped in a `v-if`:
         a component created with `open` already true never runs `UiModal`s open-watcher, so its
         focus trap sits inert and Escape never reaches the panel. -->
    <CronEntryOutputDialog
      :open="store.isOutputOpen"
      :output="store.output"
      :loading="store.outputLoading"
      :error-message="store.outputErrorMessage"
      @close="closeOutput"
    />
  </section>
</template>
