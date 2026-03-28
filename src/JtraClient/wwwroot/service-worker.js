const CACHE_NAME = 'jtra-cache-v1';
const STATIC_ASSETS = [
    '/',
    '/index.html',
    '/css/app.css',
    '/css/bootstrap/bootstrap.min.css',
    '/manifest.json',
    '/icon-192.png'
];

self.addEventListener('install', event => {
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => caches.delete(cacheName))
            );
        })
    );
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    
    if (url.pathname.startsWith('/_framework/') || 
        url.pathname.startsWith('/_blazor/') ||
        url.pathname.endsWith('.wasm') ||
        url.pathname.endsWith('.dll') ||
        url.pathname.endsWith('.pdb')) {
        return;
    }
    
    event.respondWith(
        fetch(event.request).catch(() => caches.match(event.request))
    );
});
