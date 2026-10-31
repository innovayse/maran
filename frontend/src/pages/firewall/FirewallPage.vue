<script setup lang="ts">
/**
 * The firewall's rules screen: what the host lets through, and from where. Renders a `<section>`,
 * not a `<main>` — the single `<main>` landmark lives in the layout this page is nested under. State
 * comes exclusively from the firewall store; the page never touches the API layer (rules/vue.md: API
 * composables are called from stores only). The bans and the whitelist are pages of their own
 * (`FirewallBansPage`, `FirewallWhitelistPage`), each server-paged with its state in the URL.
 *
 * **The rule list is the one list in the panel that is not paged, on purpose.** The removal
 * confirmation below asks whether any rule for a port would survive, and that question is about
 * every rule the host runs: a page of them would answer it wrongly. A rule has no identity beyond
 * its three values, so there is no detail to open either.
 *
 * **Every rule change that could cost the operator their way in passes through
 * `FirewallLockoutDialog` first**, which is where the reasoning about that lives. The panel accepts
 * either operation whether or not the dialog was shown — the agent's SSH fallback makes removal
 * fail-open by design — so this is a moment to read the change back, not an enforcement point.
 */
import { computed, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiToast from '../../components/ui/UiToast.vue'
import FirewallLockoutDialog from '../../components/firewall/FirewallLockoutDialog.vue'
import FirewallPresetButtons from '../../components/firewall/FirewallPresetButtons.vue'
import FirewallRuleForm from '../../components/firewall/FirewallRuleForm.vue'
import FirewallRulesTable from '../../components/firewall/FirewallRulesTable.vue'
import { useFirewallStore } from '../../stores/firewall'
import { isAnySourceRange } from '../../utils/anySourceRange'
import type { FirewallRule, FirewallRuleChange } from '../../types/firewall'

const { t } = useI18n()
const store = useFirewallStore()

/** The rule form, so an accepted change can empty the fields it owns. */
const ruleForm = useTemplateRef<{ reset: () => void }>('ruleForm')

/** The rules awaiting the lockout confirmation, or empty when nothing is. */
const pendingRules: Ref<FirewallRule[]> = ref([])

/** Which way the pending change goes. */
const pendingIntent: Ref<FirewallRuleChange> = ref('allow')

/** Whether the pending change came from the raw form, whose fields are emptied once it lands. */
const pendingFromForm: Ref<boolean> = ref(false)

/** The success toast's text after a rule was installed, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the rules are still on their way: the table draws placeholder rows meanwhile. */
const listsLoading: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loading'
})

/** Whether the panel answered successfully and reported no rules at all. */
const hasNoRules: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loaded' && store.rules.length === 0
})

/**
 * Whether the pending removal would leave the host running no rule at all for one of the ports it
 * names — the case the confirmation calls out, because if that port is the SSH port the panel's
 * unconditional accept comes back and SSH is reachable from every source again.
 */
const leavesPortsUnruled: ComputedRef<boolean> = computed(() => {
  if (pendingIntent.value !== 'deny') {
    return false
  }

  // What the host would still be running once the pending removal has been sent. A rule is matched
  // on all three of its values because all three of them ARE the rule — there is no identifier to
  // compare instead.
  const survivors = store.rules.filter((existing) => {
    return !pendingRules.value.some((removed) => {
      return (
        removed.port === existing.port &&
        removed.protocol === existing.protocol &&
        removed.sourceCidr === existing.sourceCidr
      )
    })
  })

  return pendingRules.value.some((pending) => {
    return !survivors.some((survivor) => {
      return survivor.port === pending.port
    })
  })
})

/**
 * Loads every rule the host is running.
 * @returns Resolves once the request has settled.
 */
const refresh = async (): Promise<void> => {
  await store.load()
}

/**
 * Sends an accepted set of allows and empties the form when the form is where they came from.
 * @param rules The rules to install.
 * @param fromForm Whether the raw form produced them.
 * @returns Resolves once the change has settled.
 */
const allow = async (rules: readonly FirewallRule[], fromForm: boolean): Promise<void> => {
  if (!(await store.allowPorts(rules))) {
    return
  }
  notice.value = t('firewall.rules.created', rules.length)
  if (fromForm) {
    ruleForm.value?.reset()
  }
}

/**
 * Starts an allow, raising the lockout confirmation for the one shape of addition that can cut the
 * operator off: a TCP rule scoped to a narrower range than "everything".
 *
 * Which port SSH listens on is a host fact the panel never sends to the browser, so this cannot ask
 * "is that the SSH port". It asks about every rule that COULD be, which errs towards a confirmation
 * too many rather than the one that matters going unasked.
 * @param rules The rules the operator asked for.
 * @param fromForm Whether the raw form produced them.
 * @returns Resolves once the change has settled, or immediately when it is awaiting confirmation.
 */
const requestAllow = async (rules: FirewallRule[], fromForm: boolean): Promise<void> => {
  if (rules.length === 0) {
    return
  }

  const risky = rules.some((rule) => {
    return rule.protocol === 'tcp' && !isAnySourceRange(rule.sourceCidr)
  })
  if (risky) {
    pendingIntent.value = 'allow'
    pendingFromForm.value = fromForm
    pendingRules.value = rules
    return
  }

  await allow(rules, fromForm)
}

/**
 * Starts a removal, which always waits for the confirmation: any rule here can be the one
 * restricting SSH, and the screen has no way to tell which.
 * @param rules The rules to remove, spelled exactly as the listing reported them.
 * @returns Nothing; the dialog carries it from here.
 */
const requestDeny = (rules: FirewallRule[]): void => {
  if (rules.length === 0) {
    return
  }
  pendingIntent.value = 'deny'
  pendingFromForm.value = false
  pendingRules.value = rules
}

/**
 * Sends the confirmed change.
 * @returns Resolves once it has settled.
 */
const confirmPending = async (): Promise<void> => {
  const rules = pendingRules.value
  const intent = pendingIntent.value
  const fromForm = pendingFromForm.value
  pendingRules.value = []

  if (intent === 'allow') {
    await allow(rules, fromForm)
    return
  }
  await store.denyPorts(rules)
}

/**
 * Abandons the pending change.
 * @returns Nothing.
 */
const cancelPending = (): void => {
  pendingRules.value = []
}

/**
 * Forwards a rule the form has already validated.
 * @param rule The rule the operator typed.
 * @returns Resolves once the change has settled or been queued for confirmation.
 */
const submitRule = async (rule: FirewallRule): Promise<void> => {
  await requestAllow([rule], true)
}

/**
 * Forwards the rules a preset composed.
 * @param rules The rules the preset asked for.
 * @returns Resolves once the change has settled or been queued for confirmation.
 */
const submitPreset = async (rules: FirewallRule[]): Promise<void> => {
  await requestAllow(rules, false)
}

/**
 * Starts the removal of one rule from the table.
 * @param rule The row the operator acted on.
 * @returns Nothing.
 */
const removeRule = (rule: FirewallRule): void => {
  requestDeny([rule])
}

/**
 * Drops the rule form's last refusal: it described the values that were sent, not the ones typed now.
 * @returns Nothing.
 */
const clearRuleError = (): void => {
  store.ruleErrorMessage = null
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(refresh)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('firewall.eyebrow')"
      :title="t('firewall.heading')"
      :subtitle="t('firewall.subtitle')"
      :note="t('firewall.sshNote')"
    />

    <UiLoadingRegion :state="store.loadState" class="flex flex-col gap-4">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <FirewallPresetButtons
        :rules="store.rules"
        :busy="store.acting"
        @allow="submitPreset"
        @deny="requestDeny"
      />

      <!-- The same three blocks, in the same order, as the Bans and Never-banned screens: a refusal
           first, the form in a card of its own, then the list under its own heading (#218). -->
      <UiAlert v-if="store.ruleErrorMessage !== null" variant="error">
        {{ store.ruleErrorMessage }}
      </UiAlert>

      <UiCard>
        <FirewallRuleForm
          ref="ruleForm"
          :submitting="store.acting"
          @submit="submitRule"
          @edit="clearRuleError"
        />
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('firewall.rules.listTitle')" />

        <UiEmptyState
          v-if="hasNoRules"
          :title="t('firewall.rules.emptyTitle')"
          :description="t('firewall.rules.emptyDescription')"
        >
          <template #icon><UiIcon name="brickWall" size="lg" /></template>
        </UiEmptyState>

        <FirewallRulesTable
          v-else
          :rules="store.rules"
          :busy="store.acting"
          :loading="listsLoading"
          @remove="removeRule"
        />
      </UiCard>
    </UiLoadingRegion>

    <!-- `:open` is bound to a value that really changes, and the dialog is NOT wrapped in a `v-if`:
         created with the prop already true, `UiModal`s open-watcher never runs, so focus never
         enters the dialog and Escape never reaches it. It renders nothing while closed. -->
    <FirewallLockoutDialog
      :open="pendingRules.length > 0"
      :intent="pendingIntent"
      :rules="pendingRules"
      :leaves-ports-unruled="leavesPortsUnruled"
      :busy="store.acting"
      @confirm="confirmPending"
      @close="cancelPending"
    />

    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
