import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import LoginPage from './features/auth/LoginPage'
import { RequireAuth, RequireRole } from './features/auth/RequireAuth'
import { AppLayout } from './shared/components/AppLayout'
import AgendaPage from './features/agenda/AgendaPage'
import AccountPage from './features/account/AccountPage'
import ConfigPage from './features/config/ConfigPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          <Route element={<AppLayout />}>
            <Route index element={<AgendaPage />} />
            <Route path="cuenta" element={<AccountPage />} />
            <Route element={<RequireRole roles={['Owner', 'SuperAdmin']} />}>
              <Route path="configuracion" element={<ConfigPage />} />
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
