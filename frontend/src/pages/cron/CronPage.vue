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
 * list written into the SPA. A customer owns exactly one account, so they get no picker at all: the
 * screen is pointed at their own account from their token (useAccountChoice).
 *
 * **Layout.** The account picker sits in the page header, because it is the context of everything
 * below it; the notice for a refused write comes first; then the form, the entries and the
 * environment, each in its own card. The form stays above the list because the empty state tells
 * the operator to "add the first entry above". The table scrolls inside its own container at phone
 * width, and a long command wraps at its spaces (never mid-word with `break-all`, which turns a
 * path into a column of fragments); a single unbroken token widens the column and the table's own
 * container scrolls.
 *
 * One page rather than a list plus a form page: an entry has no detail to open. What its last run
 * left behind is the only thing beyond the row, it costs a privileged read per entry, and it is
 * therefore a dialog opened on demand rather than a column nobody asked for.
 *
 * **Loading keeps the table's shape.** While an account's crontab is on the way the entries table
 * shows its real header over placeholder rows drawn from its own `columns`, inside a
 * {@link UiLoadingRegion}; a failed read is the region's alert, never a skeleton.
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
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import UiToast from '../../components/ui/UiToast.vue'
import CronEntryForm from '../../components/cron/CronEntryForm.vue'
import CronEntryOutputDialog from '../../components/cron/CronEntryOutputDialog.vue'
import CronEnvironmentEditor from '../../components/cron/CronEnvironmentEditor.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useAccountsStore } from '../../stores/accounts'
import { useCronStore } from '../../stores/cron'
import { useLocaleStore } from '../../stores/locale'
import { formatCronExpression } from '../../utils/cronSchedule'
import { describeCronSchedule } from '../../utils/describeCronSchedule'
import { weekdayName } from '../../utils/weekdayName'
import type { CronEntry, CronSchedule } from '../../types/cronEntry'
import type { CronEnvironmentVariable } from '../../types/cronEnvironmentVariable'

const { t } = useI18n()
const store = useCronStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { filter: accountListFilter } = useAccountFilter()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** The form, so an accepted install can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The entry being rewritten, or `null` while the form is installing a new one. */
const editing: Ref<{ entryId: string; schedule: CronSchedule; command: string } | null> = ref(null)

/** The success toast's text after an entry was added, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

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

/** The entries table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'schedule', label: t('cron.list.columns.schedule'), kind: 'mono' },
    { key: 'runs', label: t('cron.list.columns.runs'), kind: 'text' },
    { key: 'command', label: t('cron.list.columns.command'), kind: 'code' },
    { key: 'state', label: t('cron.list.columns.state'), kind: 'badge' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})


/** The confirmation's accessible name, naming the command that would stop running. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingEntry.value
  return pending === null ? '' : t('cron.list.confirmRemoveTitle', { command: pending.command })
})

/**
 * The label of the account the screen is pointed at, for the picker: that account is usually not
 * on the page of matches the next search brings, and the picker must still name it.
 */
const selectedAccountLabel: ComputedRef<string | undefined> = computed(() => {
  return accountsStore.accountNames[store.accountId] ?? firstAccountLabel.value
})

/** The name of the first listed account when the screen opened on it, as the choices named it. */
const firstAccountLabel: Ref<string | undefined> = ref(undefined)

/**
 * Points the screen at the account the URL names (`?account=<id>`, the link the account detail page
 * draws), or at the first account the panel lists.
 *
 * The first rather than none, because a screen that opens on an empty picker looks broken; there is
 * nothing invented in it — the account came from the panel, and the crontab shown is that account's.
 * A URL account is selected only once the panel has named it by its own read: an id it cannot name
 * is ignored, since the picker could not show it and the server would answer 404 for it anyway. A
 * customer reads no choices (an administrator-only read) and is pointed at their own account; the
 * parameter means nothing to them.
 * @returns Resolves once the account and its crontab have settled.
 */
const start = async (): Promise<void> => {
  const own = fixedAccountId.value
  if (own !== null) {
    if (store.accountId !== own) {
      await store.selectAccount(own)
    }
    return
  }
  const requested = accountListFilter.value.accountId
  if (requested !== undefined) {
    await accountsStore.loadAccountName(requested)
    if (accountsStore.accountNames[requested] !== undefined) {
      // A link to one account's crontab wins over whichever account the store last looked at.
      if (store.accountId !== requested) {
        await store.selectAccount(requested)
      }
      return
    }
  }
  if (store.accountId !== '') {
    // Back on the account the store last looked at, which the picker must still name.
    await accountsStore.loadAccountName(store.accountId)
    return
  }
  await searchAccounts('')
  const first = accountPicker.value.options[0]
  if (first !== undefined) {
    firstAccountLabel.value = first.label
    await store.selectAccount(first.value)
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
    if (current === null) {
      notice.value = t('cron.list.created')
    }
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

/**
 * Says when an entry runs in words, for the shapes that have a plain sentence.
 * @param entry The entry to describe.
 * @returns The sentence, or the em dash when the schedule is not one a sentence can carry exactly;
 * the expression beside it is then the whole description.
 */
const scheduleDescription = (entry: CronEntry): string => {
  const description = describeCronSchedule(entry.schedule)
  if (description === null) {
    return t('common.emptyValue')
  }
  switch (description.kind) {
    case 'everyMinute':
      return t('cron.describe.everyMinute')
    case 'everyNMinutes':
      return t('cron.describe.everyNMinutes', { count: description.minutes }, description.minutes)
    case 'hourly':
      return t('cron.describe.hourly', { minute: description.minute })
    case 'daily':
      return t('cron.describe.daily', { time: description.time })
    case 'weekly':
      return t('cron.describe.weekly', {
        time: description.time,
        weekday: weekdayName(description.weekday, localeStore.current),
      })
    default:
      return t('cron.describe.monthly', { time: description.time, day: description.day })
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(start)
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <!-- The account is the page's context, so its picker sits with the title rather than in a card
         of its own; under 1024px the header drops it below the title at full width. -->
    <UiPageHeading
      :eyebrow="t('cron.list.eyebrow')"
      :title="t('cron.list.heading')"
      :subtitle="t('cron.list.subtitle')"
    >
      <template #actions>
        <UiCombobox
          v-if="fixedAccountId === null"
          :model-value="store.accountId"
          v-bind="accountPicker"
          :selected-label="selectedAccountLabel"
          :label="t('cron.list.accountLabel')"
          :placeholder="t('cron.list.accountPlaceholder')"
          class="w-full min-w-0 lg:w-72"
          @update:model-value="changeAccount"
          @search="searchAccounts"
        />
      </template>
    </UiPageHeading>

    <!-- What needs attention goes first: a write the panel was refused. -->
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">
      {{ store.saveErrorMessage }}
    </UiAlert>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">
      {{ store.actionErrorMessage }}
    </UiAlert>

    <UiCard>
      <UiSectionHeading
        class="mb-4"
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

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('cron.list.entriesHeading')" />

      <!-- Until an account is chosen there is no crontab to read, so there is nothing to wait for. -->
      <UiLoadingRegion v-if="store.accountId !== ''" :state="store.loadState">
        <template #error>
          <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
        </template>

        <UiEmptyState
          v-if="store.isEmpty"
          :title="t('cron.list.emptyTitle')"
          :description="t('cron.list.emptyDescription')"
        >
          <template #icon><UiIcon name="clock" size="lg" /></template>
        </UiEmptyState>

        <UiTable
          v-else
          :caption="t('cron.list.tableCaption')"
          :columns="columns"
          :rows="store.entries"
          row-key="entryId"
          :loading="store.loadState === 'loading'"
        >
          <template #cell-schedule="{ row }">{{ scheduleText(row) }}</template>
          <template #cell-runs="{ row }"><div class="min-w-40">{{ scheduleDescription(row) }}</div></template>
          <!-- The command is shown exactly as the customer wrote it. It can carry a credential, which
               is why the panel keeps it out of its logs and its audit journal — but it is their own
               text on their own screen, and masking it would leave them unable to read their job. -->
          <template #cell-command="{ row }"><div class="min-w-56">{{ row.command }}</div></template>
          <template #cell-state="{ row }">
            <UiBadge :variant="row.enabled ? 'success' : 'neutral'">
              {{ row.enabled ? t('cron.list.enabled') : t('cron.list.disabled') }}
            </UiBadge>
          </template>
          <template #cell-actions="{ row }">
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('cron.list.rowActions', { command: row.command })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem @select="setEnabled(row)">
                {{ row.enabled ? t('cron.list.disable') : t('cron.list.enable') }}
              </UiDropdownItem>
              <UiDropdownItem @select="edit(row)">{{ t('cron.list.edit') }}</UiDropdownItem>
              <UiDropdownItem @select="showOutput(row.entryId)">
                {{ t('cron.list.lastOutput') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="askRemove(row.entryId)">
                {{ t('cron.list.remove') }}
              </UiDropdownItem>
            </UiDropdown>
          </template>
        </UiTable>
      </UiLoadingRegion>
    </UiCard>

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('cron.environment.heading')" />
      <UiAlert v-if="store.environmentErrorMessage !== null" variant="error" class="mb-4">
        {{ store.environmentErrorMessage }}
      </UiAlert>
      <CronEnvironmentEditor
        :variables="store.environment"
        :saving="store.savingEnvironment"
        @save="saveEnvironment"
      />
    </UiCard>

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

    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
