// Browse keeps its loaded list for the Back button. A saved or removed rating changes the averages in that list,
// so it bumps this version and Browse throws the saved list away when the version differs. Storage can be blocked.
const KEY = 'browse-version'

export const browseVersion = () => {
  try { return sessionStorage.getItem(KEY) ?? '0' } catch { return '0' }
}

export const bumpBrowseVersion = () => {
  try { sessionStorage.setItem(KEY, String(Date.now())) } catch { /* ignore */ }
}
