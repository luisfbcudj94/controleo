// Service worker for push notifications on mobile (Android & iOS PWA)
// Timer lives here so notifications fire even when the app is in background.

var notifTimer = null;
var INTERVAL_MS = 5000;
var tickCount = 0;

function fireNotification() {
  tickCount++;
  self.registration.showNotification('Controleo 🌿', {
    body: 'Revisa tus finanzas — mantén el control de tus gastos. (#' + tickCount + ')',
    icon: '/favicon.ico',
    badge: '/favicon.ico',
    tag: 'controleo-periodic',
    renotify: true
  });
}

function startLoop() {
  if (notifTimer) return;
  tickCount = 0;
  fireNotification();
  notifTimer = setInterval(fireNotification, INTERVAL_MS);
  broadcastState(true);
}

function stopLoop() {
  if (notifTimer) {
    clearInterval(notifTimer);
    notifTimer = null;
  }
  tickCount = 0;
  broadcastState(false);
}

function broadcastState(active) {
  self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clients) {
    clients.forEach(function (client) {
      client.postMessage({ type: 'NOTIF_STATE', active: active });
    });
  });
}

self.addEventListener('message', function (event) {
  if (!event.data) return;
  if (event.data.type === 'START_NOTIFICATIONS') {
    startLoop();
  } else if (event.data.type === 'STOP_NOTIFICATIONS') {
    stopLoop();
  } else if (event.data.type === 'QUERY_STATE') {
    event.source.postMessage({ type: 'NOTIF_STATE', active: !!notifTimer });
  }
});

self.addEventListener('notificationclick', function (event) {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clientList) {
      for (var i = 0; i < clientList.length; i++) {
        if (clientList[i].visibilityState === 'visible') {
          return clientList[i].focus();
        }
      }
      return self.clients.openWindow('/');
    })
  );
});

self.addEventListener('install', function () {
  self.skipWaiting();
});

self.addEventListener('activate', function (event) {
  event.waitUntil(self.clients.claim());
});
