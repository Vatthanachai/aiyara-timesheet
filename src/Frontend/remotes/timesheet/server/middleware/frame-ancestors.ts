// Only the Shell may frame this page. Other pages that embed it cannot send it messages it would act on, and
// this keeps them from framing it at all. The Shell's origin is the same setting the page checks messages against.
export default defineEventHandler(event => {
  const shellOrigin = useRuntimeConfig(event).public.shellOrigin as string
  setResponseHeader(event, 'content-security-policy', `frame-ancestors 'self' ${shellOrigin}`)
})
