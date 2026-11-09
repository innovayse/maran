/**
 * Pure helpers for the permission bits the permissions dialog edits: the octal text a person may
 * type and the read/write/execute grid they may tick are two views of one number.
 *
 * Only the nine ordinary bits exist here. Setuid, setgid and the sticky bit are not offered, and the
 * server refuses them again at its boundary; this file makes no claim about what a mode is allowed
 * to be, it only converts between the two spellings.
 */

/** The three classes of person a mode speaks about, in the order the octal digits are written. */
export const PERMISSION_CLASSES = ['owner', 'group', 'others'] as const

/** One of {@link PERMISSION_CLASSES}. */
export type PermissionClass = (typeof PERMISSION_CLASSES)[number]

/** The three things a class may be allowed to do, from the highest bit of a digit to the lowest. */
export const PERMISSION_BITS = ['read', 'write', 'execute'] as const

/** One of {@link PERMISSION_BITS}. */
export type PermissionBit = (typeof PERMISSION_BITS)[number]

/** Which bits each class holds. */
export type PermissionGrid = Record<PermissionClass, Record<PermissionBit, boolean>>

/** The value of each bit inside one octal digit, in the order of {@link PERMISSION_BITS}. */
const BIT_VALUES: Readonly<Record<PermissionBit, number>> = { read: 4, write: 2, execute: 1 }

/**
 * Says whether typed text is an octal mode of one to three digits.
 * @param text The text as typed.
 * @returns True for `0` to `777` written with the digits 0 to 7.
 */
export const isOctalMode = (text: string): boolean => {
  return /^[0-7]{1,3}$/.test(text)
}

/**
 * Turns an octal mode into the grid of ticks. A listing carries a mode as `644`; a longer value
 * (`0644`) is read by its last three digits, the ordinary bits.
 * @param mode The mode as octal digits.
 * @returns The grid; every bit off when the text is not an octal mode.
 */
export const gridFromMode = (mode: string): PermissionGrid => {
  const digits = isOctalMode(mode.slice(-3)) ? mode.slice(-3).padStart(3, '0') : '000'
  /**
   * Whether one octal digit holds one permission bit.
   * @param digit One octal digit of the mode.
   * @param bit The permission asked about.
   * @returns True when the bit is set.
   */
  const held = (digit: string, bit: PermissionBit): boolean => {
    return (Number(digit) & BIT_VALUES[bit]) !== 0
  }
  /**
   * Turns one octal digit into the three ticks of a permission class.
   * @param digit One octal digit of the mode.
   * @returns Whether read, write and execute are held.
   */
  const classRow = (digit: string): Record<PermissionBit, boolean> => {
    return { read: held(digit, 'read'), write: held(digit, 'write'), execute: held(digit, 'execute') }
  }
  return { owner: classRow(digits[0]), group: classRow(digits[1]), others: classRow(digits[2]) }
}

/**
 * Suggests the mode folders get when a recursive change gives files `mode`: the same bits with execute
 * added wherever read is set, so that `644` suggests `755` and a folder is never left unenterable. It
 * is a prefill for the person to accept or edit and nothing more: the panel decides the real default
 * when none is sent, and refuses what it does not accept.
 * @param mode The files' mode as octal digits.
 * @returns Three octal digits; `000` when the text is not an octal mode.
 */
export const directoryModeFor = (mode: string): string => {
  const grid = gridFromMode(mode)
  for (const permissionClass of PERMISSION_CLASSES) {
    grid[permissionClass].execute = grid[permissionClass].execute || grid[permissionClass].read
  }
  return modeFromGrid(grid)
}

/**
 * Turns the grid of ticks into three octal digits.
 * @param grid The grid.
 * @returns The mode, always three digits, e.g. `644`.
 */
export const modeFromGrid = (grid: PermissionGrid): string => {
  return PERMISSION_CLASSES.map((permissionClass) => {
    return PERMISSION_BITS.reduce((sum, bit) => {
      return grid[permissionClass][bit] ? sum + BIT_VALUES[bit] : sum
    }, 0)
  }).join('')
}
