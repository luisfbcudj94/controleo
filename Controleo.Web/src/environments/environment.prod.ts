export const environment = {
  production: true,
  apiBaseUrl: 'https://controleo-api.azurewebsites.net',
  auth: {
    clientId: '2d2b48c6-e11f-4bd1-86e0-f33da35f3884',
    authority: 'https://login.microsoftonline.com/common',
    redirectUri: typeof window !== 'undefined' ? window.location.origin : 'https://YOUR_WEB_DOMAIN',
    postLogoutRedirectUri: typeof window !== 'undefined' ? `${window.location.origin}/login` : 'https://YOUR_WEB_DOMAIN/login',
    scopes: ['User.Read']
  }
};
