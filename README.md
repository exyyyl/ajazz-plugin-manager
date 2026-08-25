# Ajazz Plugin Manager

Менеджер плагинов и иконок Elgato Stream Deck для устройств Stream Dock AJAZZ.

## Проекты

- `ajazz-manager-electron` — новый интерфейс на Electron, React, TypeScript, TanStack и shadcn/ui;
- `ajazz-manager` — первая рабочая WinForms-версия;
- `ajazz-twitch-port` — launcher, установочные скрипты и документация порта Twitch.

Основная разработка ведётся в Electron-версии. Она обнаруживает локальные плагины Elgato и AJAZZ, включая пакеты с защищёнными манифестами, а также индексирует иконки по источникам, наборам и подпапкам.

## Запуск Electron-версии

```powershell
cd ajazz-manager-electron
npm install
npm run dev
```

Проверка production-сборки:

```powershell
npm run build
npm run preview
```

## Распространение плагинов

Репозиторий содержит наш код менеджера, launcher и скрипты, но не распространяет оригинальные плагины, бинарники и ресурсы Elgato. Долгосрочная схема проекта — брать официальный плагин из локальной установки пользователя и применять отдельный рецепт совместимости для AJAZZ.

Twitch Client ID относится к публичному OAuth-клиенту и не является Client Secret. Пользовательские access/refresh tokens в репозиторий не записываются; локальный порт хранит авторизацию отдельно в DPAPI-зашифрованном файле.
