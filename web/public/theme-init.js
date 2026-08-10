// Runs before first paint (an external file, because the CSP is script-src 'self').
// Sets data-theme on <html> from the saved choice or the OS. Keep the two colours equal to --bg in src/index.css (scripts/check-contrast.mjs checks this).
try {
  var saved = null
  try { saved = localStorage.getItem('theme') } catch { /* storage blocked: follow the OS */ }
  var light = saved === 'light' || (saved !== 'dark' && matchMedia('(prefers-color-scheme: light)').matches)
  document.documentElement.setAttribute('data-theme', light ? 'light' : 'dark')
  var meta = document.querySelector('meta[name=theme-color]')
  if (meta) meta.setAttribute('content', light ? '#f6f2ea' : '#141311')
} catch { /* stay on the dark default */ }
