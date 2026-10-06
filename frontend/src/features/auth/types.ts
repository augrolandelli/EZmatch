export type UserRole = 'SuperAdmin' | 'Owner' | 'Staff'

export interface UserDto {
  id: string
  email: string
  fullName: string
  role: UserRole
  clubId: string | null
  clubName: string | null
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: UserDto
}

export const roleLabel: Record<UserRole, string> = {
  SuperAdmin: 'Administrador EZmatch',
  Owner: 'Dueño',
  Staff: 'Recepción',
}
