import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Browse from './pages/Browse'
import TitleDetail from './pages/TitleDetail'
import Trending from './pages/Trending'
import Rankings from './pages/Rankings'
import MyList from './pages/MyList'
import Login from './pages/Login'
import Register from './pages/Register'
import CheckEmail from './pages/CheckEmail'
import ConfirmEmail from './pages/ConfirmEmail'
import { AuthProvider } from './auth'

export default function App() {
  return (
    <AuthProvider>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Browse />} />
          <Route path="/titles/:id" element={<TitleDetail />} />
          <Route path="/trending" element={<Trending />} />
          <Route path="/rankings" element={<Rankings />} />
          <Route path="/my-list" element={<MyList />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/check-email" element={<CheckEmail />} />
          <Route path="/confirm-email" element={<ConfirmEmail />} />
          <Route path="*" element={<h1>Page not found</h1>} />
        </Route>
      </Routes>
    </BrowserRouter>
    </AuthProvider>
  )
}
