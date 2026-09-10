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

Для приватного использования готов однокликовый [Ajazz-Twitch-Setup.exe](ajazz-twitch-port/release/Ajazz-Twitch-Setup.exe). В нём уже находятся публичный Client ID, Node.js, установщик и текущая сборка Twitch-плагина.

EXE содержит файлы официального плагина Elgato, поэтому репозиторий должен оставаться приватным. Перед публичным распространением нужно проверить разрешение правообладателя либо перейти на схему, при которой патчер берёт официальный плагин из локальной установки пользователя.

Twitch Client ID относится к публичному OAuth-клиенту и не является Client Secret. Пользовательские access/refresh tokens в репозиторий не записываются; локальный порт хранит авторизацию отдельно в DPAPI-зашифрованном файле.
