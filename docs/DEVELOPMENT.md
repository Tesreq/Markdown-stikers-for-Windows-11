# Разработка

Требуются Windows 11 x64 и .NET 10 SDK. Выполняйте команды из корня проекта. Версия приложения задана в `src/StickyDesk.App/StickyDesk.App.csproj`; зависимость Markdig закреплена в проекте Core.

## Основные команды

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\publish.ps1
```

Сборка по умолчанию Release. Для отладки: `build.ps1 -Configuration Debug`, затем запуск `src/StickyDesk.App` из IDE или `dotnet run --project src/StickyDesk.App`.

После первого восстановления пакетов команды можно выполнять без сети:

```powershell
.\scripts\build.ps1 -Offline
.\scripts\test.ps1 -Offline
.\scripts\publish.ps1 -Offline
```

`test.ps1` собирает решение и запускает 36 консольных и 91 нативную проверку. `-NoBuild` использует готовую сборку. Данные каждого запуска находятся в отдельном подкаталоге `.local/test-runs`.

## Публикация

По умолчанию получается автономная x64-сборка в `artifacts/StickyDesk-win-x64`. Для её запуска SDK и установленный .NET не нужны. Рядом сохраняется `THIRD_PARTY_NOTICES.txt` с лицензией Markdig.

Если EXE используется работающим приложением, скрипт откажется перезаписывать его. Сначала завершите StickyDesk через трей либо используйте другую папку:

```powershell
.\scripts\publish.ps1 -Offline -OutputDirectory '.local\publish-check'
```

Для сборки с внешним .NET Desktop Runtime: `publish.ps1 -FrameworkDependent`. Эта сборка требует установленный .NET 10 Desktop Runtime.

Сборка EXE-установщика и его проверка описаны в [INSTALLATION.md](INSTALLATION.md).

## Локальные файлы

```text
.local/nuget/packages/   кэш NuGet, включая пакеты среды выполнения
.local/test-runs/        данные и результаты проверок
.local/benchmarks/       данные замеров производительности
.local/diagnostics/      снимки состояния окон
.local/backups/          сохранённые копии пользовательских данных
```

`.local`, `artifacts`, `bin`, `obj` и готовый `StickyDesk-Setup-*.exe` в корне исключены из Git. В `.local/backups` могут находиться личные заметки; этот каталог не входит в исходники или дистрибутив.

Проект имеет локальную историю Git: исходное состояние 0.3.1 и последующие изменения сохраняются отдельными коммитами. Удалённый репозиторий не задан. Локальная история позволяет сравнивать и откатывать код, но не заменяет резервную копию вне этого диска. Перед коммитом проверяйте `git diff --check` и `git status --short`.

## Очистка

```powershell
.\scripts\clean.ps1 -WhatIf   # показать, что будет очищено
.\scripts\clean.ps1           # удалить bin и obj у проектов
.\scripts\clean.ps1 -TestData # дополнительно удалить данные запусков тестов
.\scripts\clean.ps1 -AllGenerated # кэши, промежуточные сборки и старые материалы; оставить текущий установщик и резервные копии
```

Обычная очистка удаляет только `bin`/`obj`; `-TestData` добавляет данные проверок. `-AllGenerated` удаляет также локальные кэши, компилятор установщика, промежуточные сборки, лишние дистрибутивы и исторические отчёты. Сохраняются актуальный EXE-установщик и `.local/backups`. После полной очистки нужны обычный `build.ps1` с сетью и повторная подготовка компилятора установщика. Установленная программа и `%LOCALAPPDATA%\StickyDesk` не затрагиваются.

Скрипт проверяет все абсолютные пути перед удалением, защищает резервные копии и текущий установщик, отказывается работать со ссылками и используемыми EXE. `-WhatIf` показывает план без удаления.

## Диагностика и замеры

После сборки решения:

```powershell
.\tools\StickyDesk.DesktopDiagnostics\bin\Release\net10.0\StickyDesk.DesktopDiagnostics.exe <PID> --verify
.\scripts\measure-performance.ps1 -Seconds 10
```

Диагностика только читает состояние HWND. Замер временно показывает 10 тестовых стикеров и завершает только запущенный им процесс. Результат сохраняется в `.local/benchmarks`; короткий замер не заменяет длительную приёмку.

Устройство модулей описано в [ARCHITECTURE.md](ARCHITECTURE.md), проверки и ограничения — в [TESTING.md](TESTING.md).
