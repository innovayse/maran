/**
 * The languages the code editor can highlight. `plain` is the answer for a file whose name says
 * nothing: it is shown with line numbers and no colour, never refused.
 */
export type CodeLanguage =
  | 'php'
  | 'javascript'
  | 'typescript'
  | 'jsx'
  | 'tsx'
  | 'css'
  | 'scss'
  | 'html'
  | 'vue'
  | 'json'
  | 'yaml'
  | 'xml'
  | 'sql'
  | 'shell'
  | 'ini'
  | 'nginx'
  | 'markdown'
  | 'python'
  | 'plain'

/**
 * The handle the editor widget hands its owner: the few things a Vue component needs to steer a
 * CodeMirror view without importing CodeMirror itself (the library stays in its lazy chunk).
 */
export interface CodeEditorHandle {
  /**
   * The editor's current text.
   * @returns The whole document, lines joined with `\n`.
   */
  text: () => string
  /**
   * Replaces the document with text that came from outside (a file just read) and forgets the undo
   * history, so Undo can never bring back the previous file.
   * @param text The new document.
   * @returns Nothing.
   */
  setText: (text: string) => void
  /**
   * Switches the highlighting.
   * @param language The language of the file now open.
   * @returns Resolves once the grammar has loaded and is applied.
   */
  setLanguage: (language: CodeLanguage) => Promise<void>
  /**
   * Allows or refuses typing; a refused editor is read-only and announces it.
   * @param editable Whether the text may be changed.
   * @returns Nothing.
   */
  setEditable: (editable: boolean) => void
  /**
   * Keeps long lines on one line (scrolling sideways) or wraps them.
   * @param noWrap True to keep each line on one row.
   * @returns Nothing.
   */
  setNoWrap: (noWrap: boolean) => void
  /**
   * Chooses what the Tab key does: indent (true) or move focus on (false, the default).
   * @param tabIndents True when Tab inserts indentation.
   * @returns Nothing.
   */
  setTabIndents: (tabIndents: boolean) => void
  /**
   * Opens the find and replace panel.
   * @returns Nothing.
   */
  openSearch: () => void
  /**
   * Opens the go-to-line prompt.
   * @returns Nothing.
   */
  openGoToLine: () => void
  /**
   * Moves keyboard focus into the text.
   * @returns Nothing.
   */
  focus: () => void
  /**
   * Tears the view down and releases its listeners.
   * @returns Nothing.
   */
  destroy: () => void
}

/** What {@link CodeEditorHandle} is created from. */
export interface CodeEditorOptions {
  /** The element the editor is drawn inside. */
  parent: HTMLElement
  /** The starting text. */
  text: string
  /** The starting language. */
  language: CodeLanguage
  /** The id of the element whose text names the editor for assistive technology. */
  labelledBy: string
  /** The translated texts of the search panel, keyed by CodeMirror's English phrase. */
  phrases: Record<string, string>
  /** Whether the text may be changed. */
  editable: boolean
  /** Whether long lines stay on one row. */
  noWrap: boolean
  /** Whether Tab inserts indentation instead of moving focus on. */
  tabIndents: boolean
  /** Called with the whole text after every edit made by the person (never for {@link CodeEditorHandle.setText}). */
  onChange: (text: string) => void
  /** Called when the person presses the save shortcut inside the text. */
  onSave: () => void
}
