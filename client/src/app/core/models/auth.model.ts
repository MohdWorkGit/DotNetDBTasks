export interface LoginRequest {
  username: string;
  password: string;
}

export interface AuthResult {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  username: string;
  roles: string[];
}

export interface RefreshTokenRequest {
  accessToken: string;
  refreshToken: string;
}
