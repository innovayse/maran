<script setup lang="ts">
/**
 * The grant-repair screen: what the database server's grant table holds, what a repair would change,
 * and — only after that has been read — the repair itself. Renders a `<section>`, not a `<main>`; the
 * single `<main>` landmark lives in the layout this page is nested under. State comes exclusively from
 * the grants store; the page never touches the API layer (rules/vue.md).
 *
 * **The inspection is the only way in, and the screen is built that way rather than persuaded into
 * it.** The page loads the report on mount and offers no repair control until it is holding one; the
 * button sends the figure that report gave, and the server re-classifies the host and refuses with a
 * 409 if it has moved. A single unconditional button was the alternative, and what it would have cost
 * is the whole of this branch's work: one click rewriting live database access for every customer on
 * the host, by an operator who had seen neither the rows that would change nor the rows the panel
 * would refuse — and no way afterwards to tell the two apart.
 *
 * **What this screen must never imply.** A repair closes the reach GOING FORWARD. It says nothing
 * about whether the defect was used, the host is not "clean" afterwards, and an empty exposure list on
 * a repaired row does not clear that row — a matching database may have been created and dropped in
 * between, and a pattern is matched when a client connects rather than when the agent looks
 * (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md). So there is no success banner, no
 * tick, and no "your server is secure": the sentence after a repair states what changed and then says,
 * in the same breath, what it does not establish. That paragraph is the most load-bearing text on the
 * page and a well-meaning reassurance added beside it would undo the note it comes from.
 *
 * **Refused rows are the work the screen hands back.** They are rows the panel has now promised never
 * to touch, so each carries the server's raw columns verbatim, the backend's sentence for what was
 * decided, and the backend's sentence for what to do next. The raw names frequently belong to somebody
 * else — which is why the endpoint is administrator-only — and the screen says that too, because a
 * reader shown an unfamiliar database name will otherwise assume the panel has lost track of one of
 * theirs.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import { useDatabaseGrantsStore } from '../../stores/databaseGrants'
import type { RepairedGrant } from '../../types/grantRepair'

const { t } = useI18n()
const store = useDatabaseGrantsStore()

/** Whether the repair's confirmation dialog is open. Local to this page; the store owns the request. */
const pending: Ref<boolean> = ref(false)

/**
 * The rows this census says were, or would be, rewritten.
 *
 * One list for both buckets, because a row's shape is identical and the screen's headings already say
 * which pass it is reading. It is keyed on `isReportOnly` rather than on emptiness: a repair that found
 * nothing returns the same two empty lists an inspection of a clean host does.
 */
const changedRows: ComputedRef<RepairedGrant[]> = computed(() => {
  const held = store.report
  if (held === null) {
    return []
  }

  return held.isReportOnly ? held.wouldRepair : held.repaired
})

/** Whether the panel answered and reported a host with nothing at all to repair. */
const nothingToRepair: ComputedRef<boolean> = computed(() => {
  return store.report !== null && changedRows.value.length === 0
})

/**
 * How many grants the confirmation is about, as a phrase in the reader's language.
 *
 * Its own key, interpolated into the question rather than the question carrying the
 * plural forms itself. Russian needs three forms and Armenian one, so a pluralised
 * question would mean writing that long sentence three times in one locale and once in
 * another — four places for one sentence to drift. Here the sentence stays single and
 * only the counted noun inflects, which is also the only part that can.
 *
 * **The key is named after its one call site, and that is deliberate.** A counted noun
 * is not case-neutral: this phrase lands after `для` in russian, which governs the
 * genitive, so the forms are `права / прав / прав` and NOT the dictionary
 * `право / права / прав`. The nominative forms were written here first and rendered
 * «для 1 право» on screen — grammatically wrong in a way no plural rule can catch,
 * because the rule chose correctly and the choices themselves were in the wrong case.
 * A key called `grantCount` invites reuse in a sentence with different grammar, which
 * would be wrong again and just as quietly; `questionGrantCount` belongs to
 * `confirm.question` and to nothing else.
 */
const grantCountPhrase: ComputedRef<string> = computed(() => {
  return t('databases.grantRepair.confirm.questionGrantCount', changedRows.value.length)
})

/**
 * Reads the census again, discarding whatever is held.
 * @returns Resolves once the request has settled.
 */
const refresh = async (): Promise<void> => {
  await store.load()
}

/**
 * Opens the confirmation for the repair.
 * @returns Nothing; sets local state synchronously.
 */
const ask = (): void => {
  pending.value = true
}

/**
 * Abandons the confirmation without sending anything.
 * @returns Nothing; sets local state synchronously.
 */
const cancel = (): void => {
  pending.value = false
}

/**
 * Performs the repair and closes the dialog once the request has settled.
 *
 * Closed AFTER the request rather than before it: the dialog holds the spinner that tells the operator
 * the panel is working on every customer's grants at once.
 * @returns Resolves once the request has settled, successfully or not.
 */
const confirm = async (): Promise<void> => {
  await store.repair()
  pending.value = false
}

onMounted(() => {
  void store.load()
})
</script>

<template>
  <section class="w-full max-w-3xl">
    <UiPageHeading
      class="mb-4"
      :title="t('databases.grantRepair.heading')"
      :subtitle="t('databases.grantRepair.subtitle')"
      :note="t('databases.grantRepair.adminNote')"
    />

    <UiAlert v-if="store.errorMessage !== null" variant="error" class="mb-4">
      {{ store.errorMessage }}
    </UiAlert>

    <UiSpinner v-if="store.loading" :label="t('databases.grantRepair.loading')" />

    <!-- Not an error, and deliberately not drawn as one: the endpoint is administrator-only and a
         customer's refusal is the panel behaving correctly. The sentence says who may run this
         rather than implying that something went wrong or that the host is clean. -->
    <UiEmptyState
      v-else-if="!store.isDisclosed"
      :title="t('databases.grantRepair.notDisclosedTitle')"
      :description="t('databases.grantRepair.notDisclosedDescription')"
    >
      <template #icon><UiIcon name="shieldCheck" aria-hidden="true" /></template>
    </UiEmptyState>

    <template v-else-if="store.report !== null">
      <UiCard class="mb-4">
        <div class="mb-3 flex items-center gap-2">
          <UiSectionHeading :title="t('databases.grantRepair.census.heading')" />
          <UiBadge :variant="store.report.isReportOnly ? 'info' : 'success'">
            {{
              store.report.isReportOnly
                ? t('databases.grantRepair.census.inspected')
                : t('databases.grantRepair.census.acted')
            }}
          </UiBadge>
        </div>
        <!-- Four buckets and the total, side by side, because the four sum to the total and that is
             what makes this a census of the grant table rather than a list of what happened to be
             interesting. The two labels that change with the pass are the pair that must never be
             confused: "would be narrowed" and "narrowed" are an inspection and an action. -->
        <UiDescriptionList>
          <UiDescriptionItem :term="t('databases.grantRepair.census.examined')" mono>
            {{ store.report.examinedGrants }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('databases.grantRepair.census.alreadyCorrect')" mono>
            {{ store.report.alreadyCorrect }}
          </UiDescriptionItem>
          <UiDescriptionItem
            :term="
              store.report.isReportOnly
                ? t('databases.grantRepair.census.wouldRepair')
                : t('databases.grantRepair.census.repaired')
            "
            mono
          >
            {{ changedRows.length }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('databases.grantRepair.census.refused')" mono>
            {{ store.report.refused.length }}
          </UiDescriptionItem>
        </UiDescriptionList>

        <p class="mt-3 text-sm text-text-secondary">
          {{ t('databases.grantRepair.census.sumNote') }}
        </p>
      </UiCard>

      <UiCard class="mb-4">
        <UiSectionHeading
          class="mb-3"
          :title="
            store.report.isReportOnly
              ? t('databases.grantRepair.changed.plannedHeading')
              : t('databases.grantRepair.changed.doneHeading')
          "
          :subtitle="t('databases.grantRepair.changed.subtitle')"
        />

        <UiEmptyState
          v-if="nothingToRepair"
          :title="t('databases.grantRepair.changed.emptyTitle')"
          :description="t('databases.grantRepair.changed.emptyDescription')"
        >
          <template #icon><UiIcon name="check" aria-hidden="true" /></template>
        </UiEmptyState>

        <div
          v-for="row in changedRows"
          v-else
          :key="`${row.databaseName}/${row.dbUsername}`"
          class="mb-4 last:mb-0"
        >
          <UiDescriptionList>
            <UiDescriptionItem :term="t('databases.grantRepair.changed.database')" mono>
              {{ row.databaseName }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('databases.grantRepair.changed.user')" mono>
              {{ row.dbUsername }}
            </UiDescriptionItem>
            <!-- Both branches of the exposure list are sentences, and the empty one is the branch
                 that matters: an empty list is NOT an all-clear. A matching database may have been
                 created and dropped while the row stood, and the pattern was matched when a client
                 connected rather than when the agent looked. -->
            <UiDescriptionItem :term="t('databases.grantRepair.changed.alsoMatched')">
              <template v-if="row.alsoMatchedDatabases.length > 0">
                <span class="font-mono">{{ row.alsoMatchedDatabases.join(', ') }}</span>
              </template>
              <span v-else class="text-text-secondary">
                {{ t('databases.grantRepair.changed.alsoMatchedNone') }}
              </span>
            </UiDescriptionItem>
          </UiDescriptionList>
        </div>

        <!-- The paragraph the threat note exists to put on a screen. It is rendered whether the pass
             inspected or acted, and it is the only thing this page says about consequences: a repair
             closes the reach going forward and establishes nothing about whether it was used. -->
        <p class="mt-3 text-sm text-text-secondary">
          {{ t('databases.grantRepair.changed.exposureNote') }}
        </p>
        <p class="mt-2 text-sm text-text-secondary">
          {{ t('databases.grantRepair.changed.notCleanNote') }}
        </p>
      </UiCard>

      <UiCard class="mb-4">
        <UiSectionHeading
          class="mb-3"
          :title="t('databases.grantRepair.refused.heading')"
          :subtitle="t('databases.grantRepair.refused.subtitle')"
        />

        <UiEmptyState
          v-if="store.report.refused.length === 0"
          :title="t('databases.grantRepair.refused.emptyTitle')"
          :description="t('databases.grantRepair.refused.emptyDescription')"
        >
          <template #icon><UiIcon name="check" aria-hidden="true" /></template>
        </UiEmptyState>

        <div
          v-for="row in store.report.refused"
          v-else
          :key="`${row.grantHost}/${row.databaseName}/${row.dbUsername}`"
          class="mb-4 last:mb-0"
        >
          <div class="mb-2 flex items-center gap-2">
            <!-- The backend's own sentence. The machine reason is shown beside it, never instead of
                 it: an operator quoting a support ticket needs one spelling that does not change
                 with their language, and rules/vue.md forbids the machine word standing alone. -->
            <span class="text-sm font-semibold text-text-primary">{{ row.reasonDisplayName }}</span>
            <UiBadge variant="warning">{{ row.reason }}</UiBadge>
          </div>
          <UiDescriptionList>
            <UiDescriptionItem :term="t('databases.grantRepair.refused.grantHost')" mono>
              {{ row.grantHost }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('databases.grantRepair.refused.storedName')" mono>
              {{ row.databaseName }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('databases.grantRepair.refused.user')" mono>
              {{ row.dbUsername }}
            </UiDescriptionItem>
          </UiDescriptionList>
          <p class="mt-2 text-sm text-text-secondary">{{ row.reasonAdvice }}</p>
        </div>

        <p class="mt-3 text-sm text-text-secondary">
          {{ t('databases.grantRepair.refused.notYoursNote') }}
        </p>
      </UiCard>

      <UiCard>
        <UiSectionHeading
          class="mb-3"
          :title="t('databases.grantRepair.action.heading')"
          :subtitle="t('databases.grantRepair.action.subtitle')"
        />
        <div class="flex flex-wrap items-center gap-3">
          <UiButton variant="secondary" :disabled="store.loading" @click="refresh">
            {{ t('databases.grantRepair.action.refresh') }}
          </UiButton>
          <!-- Offered on a held INSPECTION and on nothing else. After a repair the same shape comes
               back with `isReportOnly` false, and a control here would invite a second host-wide
               rewrite over a census nobody had read as a plan. -->
          <UiButton
            v-if="store.canRepair"
            variant="destructive"
            :disabled="nothingToRepair || store.repairing"
            @click="ask"
          >
            {{ t('databases.grantRepair.action.repair') }}
          </UiButton>
        </div>
        <p v-if="!store.canRepair" class="mt-3 text-sm text-text-secondary">
          {{ t('databases.grantRepair.action.readReportFirst') }}
        </p>
        <p v-else-if="nothingToRepair" class="mt-3 text-sm text-text-secondary">
          {{ t('databases.grantRepair.action.nothingToDo') }}
        </p>
      </UiCard>
    </template>

    <UiConfirm
      :open="pending"
      :title="t('databases.grantRepair.confirm.title')"
      :question="
        t('databases.grantRepair.confirm.question', { grants: grantCountPhrase })
      "
      :confirm-label="t('databases.grantRepair.confirm.confirm')"
      :cancel-label="t('databases.grantRepair.confirm.cancel')"
      :close-label="t('databases.grantRepair.confirm.close')"
      :acting="store.repairing"
      :acting-label="t('databases.grantRepair.confirm.acting')"
      @close="cancel"
      @confirm="confirm"
    />
  </section>
</template>
