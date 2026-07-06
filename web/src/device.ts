// rough "Chrome on Windows" from the user agent; the order matters (Edge and Chrome also say Safari, Android says Linux)
export function device(ua: string) {
  const browser = ['Edg/', 'Firefox', 'Chrome', 'Safari'].find((b) => ua.includes(b))
  const os = ['Windows', 'Android', 'iPhone', 'Mac', 'Linux'].find((o) => ua.includes(o))
  return `${browser === 'Edg/' ? 'Edge' : (browser ?? 'Browser')} on ${os ?? 'unknown system'}`
}
