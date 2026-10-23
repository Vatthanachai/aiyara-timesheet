import { createHmac } from 'node:crypto'
import { sleep } from './stack'

const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function fromBase32(encoded: string) {
  let buffer = 0
  let bits = 0
  const bytes: number[] = []
  for (const character of encoded.replace(/[\s=]/g, '').toUpperCase()) {
    buffer = (buffer << 5) | alphabet.indexOf(character)
    bits += 5
    if (bits >= 8) { bytes.push((buffer >> (bits - 8)) & 0xff); bits -= 8 }
  }
  return Buffer.from(bytes)
}

/** RFC 6238: HMAC-SHA1, 30-second steps, six digits. */
export function totpAt(secret: string, step: number) {
  const counter = Buffer.alloc(8)
  counter.writeBigInt64BE(BigInt(step))
  const hash = createHmac('sha1', fromBase32(secret)).update(counter).digest()
  const offset = hash[hash.length - 1]! & 0x0f
  const binary = ((hash[offset]! & 0x7f) << 24) | (hash[offset + 1]! << 16) | (hash[offset + 2]! << 8) | hash[offset + 3]!
  return String(binary % 1_000_000).padStart(6, '0')
}

/**
 * An authenticator app. The server accepts the current 30-second step or one either side, but never a
 * step it has already accepted. So every code here is for a step later than the one used before: the
 * current step, or the next one when the current one was just used. If both are used up, it waits for
 * the clock to move on, which also covers a call made right at a step boundary.
 */
export class Authenticator {
  constructor(readonly secret: string, public lastStep = 0) {}

  async code() {
    for (;;) {
      const now = Math.floor(Date.now() / 1000 / 30)
      const step = Math.max(now, this.lastStep + 1)
      if (step <= now + 1) {
        this.lastStep = step
        return totpAt(this.secret, step)
      }
      await sleep(1000)
    }
  }
}
