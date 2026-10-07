import { onBeforeUnmount, onMounted } from 'vue'

/** Reports a remote document's content height to its embedding shell. */
export function useIframeAutoHeight(shellOrigin: string) {
  let observer: ResizeObserver | undefined
  let frame: number | undefined

  const reportHeight = () => {
    if (window.parent === window) return

    const documentHeight = Math.max(
      document.body?.scrollHeight ?? 0,
      document.documentElement.scrollHeight
    )
    window.parent.postMessage({ type: 'aiyara.resize', height: documentHeight }, shellOrigin)
  }

  onMounted(() => {
    reportHeight()
    observer = new ResizeObserver(() => {
      if (frame !== undefined) cancelAnimationFrame(frame)
      frame = requestAnimationFrame(reportHeight)
    })
    if (document.body) observer.observe(document.body)
    observer.observe(document.documentElement)
  })

  onBeforeUnmount(() => {
    observer?.disconnect()
    if (frame !== undefined) cancelAnimationFrame(frame)
  })
}
