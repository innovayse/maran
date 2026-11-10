<script setup lang="ts">
/**
 * How an import ended, and what to do next.
 *
 * - **Completed:** the account exists; its link, the findings worth knowing after the fact (the cron
 *   entries imported switched off above all), and the control that fetches the generated credentials,
 *   once.
 * - **Rolled back:** the job failed and the account it had created was deleted; nothing remains.
 * - **Rolled back incompletely:** the job failed and the deletion failed too; the account remains and
 *   the administrator is told so.
 * - **Discarded:** the package was thrown away without starting.
 *
 * **A failure is shown as the panel's sentence, with the code beneath it.** The panel resolves the
 * stored code into the caller's language on every read (`failureName`), so the SPA renders that and
 * keeps no table of codes (rules/vue.md); the code stays for support. Props in, events out: the page
 * owns the store.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiLink from '../ui/UiLink.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import MigrationFindingList from './MigrationFindingList.vue'
import type { AccountImport, AccountImportAction, MigrationFinding } from '../../types/accountMove'

/** Props accepted by {@link AccountImportOutcome}. */
defineProps<{
  /** The import that ended. */
  accountImport: AccountImport
  /** The findings of the dry run that preceded the start, or empty when this page did not run one. */
  findings: MigrationFinding[]
  /** The action in flight, or `null`. */
  busy: AccountImportAction | null
}>()

/** Events emitted by {@link AccountImportOutcome}. */
const emit = defineEmits<{
  /** The administrator asked for the generated credentials. */
  (e: 'reveal'): void
}>()

const { t } = useI18n()

/**
 * Asks the page to fetch the credentials.
 * @returns Nothing; emits synchronously.
 */
const reveal = (): void => {
  emit('reveal')
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4" data-testid="import-outcome">
    <template v-if="accountImport.state === 'Completed'">
      <UiAlert variant="success">{{ t('accountMove.import.outcome.completed', { name: accountImport.accountName }) }}</UiAlert>

      <UiLink
        v-if="accountImport.targetAccountId !== null"
        :to="{ name: 'account-detail', params: { id: accountImport.targetAccountId } }"
        trailing-icon="chevronRight"
      >
        {{ t('accountMove.import.outcome.openAccount') }}
      </UiLink>

      <template v-if="findings.length > 0">
        <UiSectionHeading :title="t('accountMove.import.outcome.afterTitle')" />
        <MigrationFindingList :findings="findings" />
      </template>

      <template v-if="accountImport.hasIssuedSecrets">
        <UiSectionHeading :title="t('accountMove.import.outcome.secretsTitle')" :subtitle="t('accountMove.import.outcome.secretsIntro')" />
        <div>
          <UiButton :disabled="busy !== null" @click="reveal">
            {{ busy === 'claim' ? t('accountMove.import.outcome.revealing') : t('accountMove.import.outcome.reveal') }}
          </UiButton>
        </div>
      </template>
    </template>

    <template v-else-if="accountImport.state === 'RolledBack'">
      <UiAlert variant="error">{{ t('accountMove.import.outcome.rolledBack') }}</UiAlert>
    </template>

    <template v-else-if="accountImport.state === 'RolledBackIncomplete'">
      <UiAlert variant="error">{{ t('accountMove.import.outcome.rolledBackIncomplete', { name: accountImport.accountName }) }}</UiAlert>
      <UiLink
        v-if="accountImport.targetAccountId !== null"
        :to="{ name: 'account-detail', params: { id: accountImport.targetAccountId } }"
        trailing-icon="chevronRight"
      >
        {{ t('accountMove.import.outcome.openRemaining') }}
      </UiLink>
    </template>

    <UiAlert v-else-if="accountImport.state === 'Discarded'" variant="info">{{ t('accountMove.import.outcome.discarded') }}</UiAlert>

    <div v-if="accountImport.failureCode !== null" class="flex flex-col gap-1">
      <p v-if="accountImport.failureName !== null" class="text-sm text-text-primary wrap-anywhere" data-testid="import-failure-name">
        {{ accountImport.failureName }}
      </p>
      <p class="font-mono text-sm text-text-muted wrap-anywhere">
        {{ t('accountMove.import.outcome.failureCode', { code: accountImport.failureCode }) }}
      </p>
    </div>
  </div>
</template>
