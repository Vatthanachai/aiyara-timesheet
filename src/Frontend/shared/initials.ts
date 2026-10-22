/** Thai vowels written before the consonant they follow in speech (เ แ โ ใ ไ). */
const LEADING_THAI_VOWEL = /[เ-ไ]/

/**
 * Avatar initials: the first letter of each name part, upper-cased. A Thai name's leading vowel is
 * skipped for its consonant, so ใจดี gives จ rather than ใ.
 */
export function nameInitials(...parts: (string | null | undefined)[]) {
  return parts
    .map(part => [...(part ?? '').trim()].find(letter => !LEADING_THAI_VOWEL.test(letter)) ?? '')
    .join('')
    .toUpperCase()
}
