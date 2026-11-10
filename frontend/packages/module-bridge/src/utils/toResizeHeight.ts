/**
 * Turns a measured content height into the integer the `resize` message carries. The shell clamps the
 * value to its own range, so no range is repeated here.
 *
 * @param height The height in CSS pixels.
 * @returns The height rounded up to a whole pixel.
 * @throws RangeError when the height is not a finite, non-negative number.
 */
export const toResizeHeight = (height: number): number => {
  if (!Number.isFinite(height) || height < 0) {
    throw new RangeError('The height must be a finite, non-negative number of pixels.')
  }

  return Math.ceil(height)
}
