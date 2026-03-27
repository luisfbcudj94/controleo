export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5051',
  auth: {
    clientId: '2d2b48c6-e11f-4bd1-86e0-f33da35f3884',
    authority: 'https://login.microsoftonline.com/common',
    redirectUri: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4200',
    postLogoutRedirectUri: typeof window !== 'undefined' ? `${window.location.origin}/login` : 'http://localhost:4200/login',
    scopes: ['User.Read']
  }
};
