<script setup lang="ts">
/**
 * The PHP versions this server has, and the one control that adds to them.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under.
 *
 * **Why a screen existed for choosing a version and none for adding one.** The
 * agent has been able to install a version on demand since plan 3; nothing drove
 * it. An operator whose customer needed 8.4 could see that 8.4 was absent from
 * every site form on the panel and had no way to say so — the only remedy was a
 * shell on the server, which is the thing a control panel exists to avoid.
 *
 * **Administrator-only, and the server is what says so.** No route guard
 * duplicates that decision, for the reason the audit journal's route records:
 * a second copy of an authorization rule is a second place for it to be wrong,
 * and the copy in the browser is the one that cannot be trusted. A customer who
 * types this URL sees the panel's own refusal rendered on the page.
 *
 * **The install does not resolve quickly and the screen says so.** It is the
 * host's package manager fetching over whatever mirror it has. The button holds
 * its pending state for the whole run, and the tasks screen carries the progress
 * the panel records against it.
 */
import { onMounted, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import { useSitesStore } from '../../stores/sites'

const { t } = useI18n()
const sitesStore = useSitesStore()

/** The version typed into the install field, e.g. `8.4`. */
const version: Ref<string> = ref('')

onMounted(async () => {
  await sitesStore.loadPhpVersions()
})

/**
 * Names whether a version is the host's default CLI PHP.
 *
 * Three answers, not two. `null` means the agent could not establish it, which is
 * not the same as "no" — printing "no" for it would state a fact nobody observed
 * (the backend keeps the two apart for exactly this reason).
 * @param isDefault What the panel reported for this version.
 * @returns The word an operator reads.
 */
const defaultLabel = (isDefault: boolean | null): string => {
  if (isDefault === null) {
    return t('php.table.defaultUnknown')
  }

  return isDefault ? t('php.table.defaultYes') : t('php.table.defaultNo')
}

/**
 * Asks the server to install the typed version.
 *
 * The field is cleared only when the server reports the version among those
 * installed afterwards. A field cleared on "the request did not throw" would
 * read as done for an install that failed.
 * @returns Resolves once the attempt has settled.
 */
const install = async (): Promise<void> => {
  const wanted = version.value.trim()
  if (wanted.length === 0) {
    return
  }

  const installed = await sitesStore.installPhpVersion(wanted)
  if (installed) {
    version.value = ''
  }
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <UiPageHeading :title="t('php.title')" :description="t('php.description')" />

    <UiCard>
      <UiTable :caption="t('php.table.caption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('php.table.version') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('php.table.default') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="installed in sitesStore.phpVersions" :key="installed.version">
          <UiTableCell class="font-medium">{{ installed.version }}</UiTableCell>
          <UiTableCell>{{ defaultLabel(installed.isDefault) }}</UiTableCell>
        </UiTableRow>
      </UiTable>

      <UiEmptyState
        v-if="sitesStore.phpVersions.length === 0"
        :title="t('php.empty.title')"
        :description="t('php.empty.description')"
      />
    </UiCard>

    <UiCard>
      <div class="flex flex-col gap-3">
        <UiInput
          v-model="version"
          :label="t('php.install.label')"
          placeholder="8.4"
          :disabled="sitesStore.installingPhpVersion !== null"
        />
        <p class="text-sm text-text-secondary">{{ t('php.install.hint') }}</p>

        <UiAlert v-if="sitesStore.phpInstallErrorMessage" variant="error">
          {{ sitesStore.phpInstallErrorMessage }}
        </UiAlert>

        <div>
          <UiButton
            :disabled="version.trim().length === 0 || sitesStore.installingPhpVersion !== null"
            @click="install"
          >
            {{
              sitesStore.installingPhpVersion === null
                ? t('php.install.submit')
                : t('php.install.running', { version: sitesStore.installingPhpVersion })
            }}
          </UiButton>
        </div>
      </div>
    </UiCard>
  </section>
</template>
