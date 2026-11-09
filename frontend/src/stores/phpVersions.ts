import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { InstallablePhpVersion, PhpExtension, PhpVersion } from '../types/phpVersion'

/**
 * Owns the PHP versions installed on the host, the install of a new one and the extensions of the
 * version whose panel is open. The site form, the
 * site list's filter, the detail page's version picker and the PHP settings page all read them
 * from here.
 *
 * Error text is never generated here: the backend's already-localized message is stored verbatim.
 */
export const usePhpVersionsStore = defineStore('phpVersions', () => {
  const api = useSitesApi()

  /** The PHP versions installed on the host, as last loaded. Never a list written into the SPA. */
  const phpVersions: Ref<PhpVersion[]> = ref([])

  /** Where the read of {@link phpVersions} stands; every failure sets `error`, whatever its cause. */
  const phpVersionsState: Ref<LoadingState> = ref('loading')

  /** The versions the host can install, installed ones flagged, as last loaded. Never written into the SPA. */
  const installablePhpVersions: Ref<InstallablePhpVersion[]> = ref([])

  /** Where the read of {@link installablePhpVersions} stands. */
  const installablePhpVersionsState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the most recent failed read of the installable versions, or `null`. */
  const installableErrorMessage: Ref<string | null> = ref(null)

  /** The version an install is running for, or `null` when none is. */
  const installingPhpVersion: Ref<string | null> = ref(null)

  /**
   * Why the last install attempt failed, verbatim from the panel, or `null`.
   *
   * Separate from the store's general `errorMessage` for the reason the create flow keeps its own:
   * an install that fails must not wipe the reason a different request failed, and the screen
   * shows this one next to the control that caused it.
   */
  const phpInstallErrorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed read of {@link phpVersions}, or `null`
   * when the last read succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** The version whose extensions {@link extensions} holds, or `null` before any were asked for. */
  const extensionsVersion: Ref<string | null> = ref(null)

  /** The extensions of {@link extensionsVersion}, as last read from the server. */
  const extensions: Ref<PhpExtension[]> = ref([])

  /** Where the read of {@link extensions} stands. */
  const extensionsState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the most recent failed read of {@link extensions}, or `null`. */
  const extensionsErrorMessage: Ref<string | null> = ref(null)

  /**
   * The catalogue name of the extension a switch is being applied to, or `null` when none is. One
   * switch at a time: a change reloads PHP for every account of the version, so a second request
   * queued behind the first would only race it.
   */
  const switchingExtension: Ref<string | null> = ref(null)

  /**
   * Why the last switch failed, verbatim from the panel, or `null`. Kept apart from
   * {@link extensionsErrorMessage} for the reason the install keeps its own: a refused switch must
   * not replace the list the operator is looking at with an error.
   */
  const extensionSwitchErrorMessage: Ref<string | null> = ref(null)

  /**
   * Counts the reads of {@link extensions} begun. An answer that arrives after a newer read began
   * (the operator moved to another version meanwhile) is dropped, so the list can never show one
   * version's extensions under another's name.
   */
  let extensionsRead = 0

  /**
   * Loads the PHP versions installed on the host, which the site form selects from.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadPhpVersions = async (): Promise<void> => {
    phpVersionsState.value = 'loading'
    try {
      phpVersions.value = await api.listPhpVersions()
      errorMessage.value = null
      phpVersionsState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      phpVersionsState.value = 'error'
    }
  }

  /**
   * Loads the versions the host can install, which the install picker selects from.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadInstallablePhpVersions = async (): Promise<void> => {
    installablePhpVersionsState.value = 'loading'
    try {
      installablePhpVersions.value = await api.listInstallablePhpVersions()
      installableErrorMessage.value = null
      installablePhpVersionsState.value = 'loaded'
    } catch (error) {
      installableErrorMessage.value = error instanceof ApiError ? error.message : null
      installablePhpVersionsState.value = 'error'
    }
  }

  /**
   * Asks the server to install a PHP version, and replaces the held list with what it answers.
   *
   * The list is REPLACED by the server's answer rather than appended to with the version that was
   * requested: what this host has is the agent's to report, and a list grown from the request
   * would show a version as installed on the strength of having asked for it.
   * @param version The version to install, e.g. `8.4`.
   * @returns `true` when the server reports the version among those installed afterwards.
   */
  const installPhpVersion = async (version: string): Promise<boolean> => {
    installingPhpVersion.value = version
    try {
      phpVersions.value = await api.installPhpVersion(version)
      phpInstallErrorMessage.value = null
      // The installed flags changed, so the picker's options are re-read from the server rather than
      // patched locally with the version that was asked for.
      await loadInstallablePhpVersions()
      return phpVersions.value.some((installed) => {
        return installed.version === version
      })
    } catch (error) {
      phpInstallErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      installingPhpVersion.value = null
    }
  }

  /**
   * Loads the extensions of one installed version, replacing the held list.
   *
   * Re-reading the version already on screen keeps its rows drawn until the answer replaces them;
   * asking for another version clears them first, so one version's extensions are never shown
   * under another's name while the answer is on its way.
   * @param version The installed version, e.g. `8.3`.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadExtensions = async (version: string): Promise<void> => {
    const read = ++extensionsRead
    if (extensionsVersion.value !== version || extensionsState.value !== 'loaded') {
      extensions.value = []
      extensionsState.value = 'loading'
    }
    extensionsVersion.value = version
    try {
      const answered = await api.listPhpExtensions(version)
      if (read !== extensionsRead) {
        return
      }
      extensions.value = answered
      extensionsErrorMessage.value = null
      extensionsState.value = 'loaded'
    } catch (error) {
      if (read !== extensionsRead) {
        return
      }
      extensions.value = []
      extensionsErrorMessage.value = error instanceof ApiError ? error.message : null
      extensionsState.value = 'error'
    }
  }

  /**
   * Switches one extension of a version on or off, then replaces the held list with what the
   * server reports.
   *
   * The new state is READ BACK rather than assumed: whether PHP loads an extension is the host's
   * to report, and a switch drawn as flipped on the strength of having asked would show a change
   * that a refused reload never made.
   * @param version The installed version, e.g. `8.3`.
   * @param name The extension's catalogue name.
   * @param enabled The state to leave it in.
   * @returns `true` when the server reports the extension in the requested state afterwards.
   */
  const setExtension = async (version: string, name: string, enabled: boolean): Promise<boolean> => {
    switchingExtension.value = name
    extensionSwitchErrorMessage.value = null
    try {
      await api.setPhpExtension(version, name, enabled)
      await loadExtensions(version)
      return (
        extensions.value.find((candidate) => {
          return candidate.name === name
        })?.enabled === enabled
      )
    } catch (error) {
      extensionSwitchErrorMessage.value = error instanceof ApiError ? error.message : null
      // A refusal may still have changed the host (a package installed before the reload failed),
      // so the list is read again rather than left showing the state from before the attempt.
      await loadExtensions(version)
      return false
    } finally {
      switchingExtension.value = null
    }
  }

  return {
    phpVersions,
    phpVersionsState,
    installablePhpVersions,
    installablePhpVersionsState,
    installableErrorMessage,
    installingPhpVersion,
    phpInstallErrorMessage,
    extensionsVersion,
    extensions,
    extensionsState,
    extensionsErrorMessage,
    switchingExtension,
    extensionSwitchErrorMessage,
    errorMessage,
    loadPhpVersions,
    loadInstallablePhpVersions,
    installPhpVersion,
    loadExtensions,
    setExtension,
  }
})
