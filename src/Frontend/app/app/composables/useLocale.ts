export type SupportedLocale = 'th' | 'en'

const defaultLocale: SupportedLocale = 'th'

export function useLocale() {
  const locale = useState<SupportedLocale>('locale', () => defaultLocale)

  return {
    locale: readonly(locale),
    setLocale: (value: SupportedLocale) => {
      locale.value = value
    }
  }
}
