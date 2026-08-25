# Ajazz Plugin Manager — Electron

Новый интерфейс менеджера на Electron, React, TypeScript, TanStack и shadcn/ui.
Существующая WinForms-версия находится рядом в `../ajazz-manager` и остаётся рабочей на время переноса.

## Уже перенесено

- обнаружение встроенных, Elgato- и AJAZZ-плагинов;
- чтение обычных и защищённых `ELGATO`-манифестов;
- отображение Discord и других защищённых пакетов;
- индексирование пользовательской библиотеки, `.sdIconPack` и профилей AJAZZ;
- группировка иконок как `источник → набор → подпапка`;
- поиск и виртуальная сетка, рассчитанная на тысячи изображений;
- диагностические статусы AJAZZ и Elgato;
- безопасный IPC через sandboxed preload без Node.js в React-интерфейсе.

Установка, импорт, резервные копии и рецепты адаптации пока выполняются стабильной WinForms-версией и будут перенесены следующими этапами.

## Запуск

```powershell
npm install
npm run dev
```

## Проверка сборки

```powershell
npm run typecheck
npm run build
npm run preview
```

## Сборка установщика

```powershell
npm run dist
```

При упаковке проверенный Twitch-порт берётся из `../ajazz-manager/release/Ajazz-Plugin-Manager/packages`.
