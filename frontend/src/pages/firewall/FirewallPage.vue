<script setup lang="ts">
/**
 * The firewall screen: what the host lets through, who it is currently refusing, and who it will
 * never refuse. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. State comes exclusively from the firewall store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * One page rather than three, because the three lists answer one question between them and an
 * operator judging a ban is reading the whitelist in the same breath. None of them has a detail to
 * open: a rule has no identity beyond its three values, a ban is one row, and an exemption is a
 * range and a note.
 *
 * **Every rule change that could cost the operator their way in passes through
 * `FirewallLockoutDialog` first**, which is where the reasoning about that lives. The panel accepts
 * either operation whether or not the dialog was shown — the agent's SSH fallback makes removal
 * fail-open by design — so this is a moment to read the change back, not an enforcement point.
 */
import { computed, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import FirewallBanForm from '../../components/firewall/FirewallBanForm.vue'
import FirewallBansTable from '../../components/firewall/FirewallBansTable.vue'
import FirewallLockoutDialog from '../../components/firewall/FirewallLockoutDialog.vue'
import FirewallPresetButtons from '../../components/firewall/FirewallPresetButtons.vue'
import FirewallRuleForm from '../../components/firewall/FirewallRuleForm.vue'
import FirewallRulesTable from '../../components/firewall/FirewallRulesTable.vue'
import FirewallWhitelistForm from '../../components/firewall/FirewallWhitelistForm.vue'
import FirewallWhitelistTable from '../../components/firewall/FirewallWhitelistTable.vue'
import { useFirewallStore } from '../../stores/firewall'
import { isAnySourceRange } from '../../utils/anySourceRange'
import type {
  AddWhitelistEntryRequest,
  BanAddressRequest,
  FirewallRule,
  FirewallRuleChange,
} from '../../types/firewall'

const { t } = useI18n()
const store = useFirewallStore()

/** The rule form, so an accepted change can empty the fields it owns. */
const ruleForm = useTemplateRef<{ reset: () => void }>('ruleForm')

/** The ban form, so an accepted ban can empty the fields it owns. */
const banForm = useTemplateRef<{ reset: () => void }>('banForm')

/** The whitelist form, so an accepted exemption can empty the fields it owns. */
const whitelistForm = useTemplateRef<{ reset: () => void }>('whitelistForm')

/** The rules awaiting the lockout confirmation, or empty when nothing is. */
const pendingRules: Ref<FirewallRule[]> = ref([])

/** Which way the pending change goes. */
const pendingIntent: Ref<FirewallRuleChange> = ref('allow')

/** Whether the pending change came from the raw form, whose fields are emptied once it lands. */
const pendingFromForm: Ref<boolean> = ref(false)

/** Whether the panel answered successfully and reported no rules at all. */
const hasNoRules: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.rules.length === 0
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
 * Loads the three lists the screen is made of.
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
  if ((await store.allowPorts(rules)) && fromForm) {
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
 * Places a ban the form has already validated.
 * @param request The address and duration the operator typed.
 * @returns Resolves once the attempt has settled.
 */
const placeBan = async (request: BanAddressRequest): Promise<void> => {
  if (await store.banAddress(request)) {
    banForm.value?.reset()
  }
}

/**
 * Lifts every ban in force for one address.
 * @param address The address to let back in.
 * @returns Resolves once the attempt has settled.
 */
const liftBan = async (address: string): Promise<void> => {
  await store.unbanAddress(address)
}

/**
 * Adds an exemption the form has already validated.
 * @param request The range and note the operator typed.
 * @returns Resolves once the attempt has settled.
 */
const addExemption = async (request: AddWhitelistEntryRequest): Promise<void> => {
  if (await store.addWhitelistEntry(request)) {
    whitelistForm.value?.reset()
  }
}

/**
 * Removes an exemption.
 * @param id The row to remove.
 * @returns Resolves once the attempt has settled.
 */
const removeExemption = async (id: string): Promise<void> => {
  await store.removeWhitelistEntry(id)
}

onMounted(refresh)
</script>

<template>
  <section class="w-full">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('firewall.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('firewall.subtitle') }}</p>
      <p class="mt-1 text-sm text-text-muted">{{ t('firewall.sshNote') }}</p>
    </div>

    <UiSpinner v-if="store.loading" :label="t('firewall.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>

    <template v-else>
      <h2 class="mb-2.5 text-lg font-semibold text-text-primary">{{ t('firewall.rules.heading') }}</h2>

      <UiAlert v-if="store.ruleErrorMessage !== null" variant="error" class="mb-4">
        {{ store.ruleErrorMessage }}
      </UiAlert>

      <FirewallPresetButtons
        class="mb-4"
        :rules="store.rules"
        :busy="store.acting"
        @allow="submitPreset"
        @deny="requestDeny"
      />

      <FirewallRuleForm ref="ruleForm" class="mb-4" :submitting="store.acting" @submit="submitRule" />

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
        @remove="removeRule"
      />

      <h2 class="mt-8 mb-2.5 text-lg font-semibold text-text-primary">
        {{ t('firewall.bans.heading') }}
      </h2>
      <p class="mb-2.5 text-sm text-text-muted">{{ t('firewall.bans.subtitle') }}</p>

      <UiAlert v-if="store.banErrorMessage !== null" variant="error" class="mb-4">
        {{ store.banErrorMessage }}
      </UiAlert>

      <FirewallBanForm ref="banForm" class="mb-4" :submitting="store.acting" @submit="placeBan" />

      <UiEmptyState
        v-if="store.bans.length === 0"
        :title="t('firewall.bans.emptyTitle')"
        :description="t('firewall.bans.emptyDescription')"
      />

      <FirewallBansTable v-else :bans="store.bans" :busy="store.acting" @unban="liftBan" />

      <h2 class="mt-8 mb-2.5 text-lg font-semibold text-text-primary">
        {{ t('firewall.whitelist.heading') }}
      </h2>
      <p class="mb-1 text-sm text-text-muted">{{ t('firewall.whitelist.subtitle') }}</p>
      <!-- The seeded row is not marked as one on the wire, so the screen says what to look for
           rather than deciding for itself which row the installer left behind. -->
      <p class="mb-2.5 max-w-[70ch] text-sm text-text-muted">{{ t('firewall.whitelist.seedNote') }}</p>

      <UiAlert v-if="store.whitelistErrorMessage !== null" variant="error" class="mb-4">
        {{ store.whitelistErrorMessage }}
      </UiAlert>

      <FirewallWhitelistForm
        ref="whitelistForm"
        class="mb-4"
        :submitting="store.acting"
        @submit="addExemption"
      />

      <UiEmptyState
        v-if="store.whitelist.length === 0"
        :title="t('firewall.whitelist.emptyTitle')"
        :description="t('firewall.whitelist.emptyDescription')"
      />

      <FirewallWhitelistTable
        v-else
        :entries="store.whitelist"
        :busy="store.acting"
        @remove="removeExemption"
      />
    </template>

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
  </section>
</template>
