// A changed picture keeps its URL, so this version in the query string makes every mounted Avatar fetch it again.
let version = 0
const listeners = new Set<() => void>()

export const avatarVersion = () => version
export function refreshAvatars() {
  version++
  listeners.forEach((l) => l())
}
export function subscribeAvatars(l: () => void) {
  listeners.add(l)
  return () => { listeners.delete(l) }
}
